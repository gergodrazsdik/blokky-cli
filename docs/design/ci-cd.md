# CI/CD

Status: **draft** (T-0067, 2026-11-07). Source: brief §7, [ADR 0001](../adr/0001-baseline.md) (distribution), [ADR 0004](../adr/0004-platform-invocation-conformance.md) (conformance checkout), [ADR 0007](../adr/0007-runtime-plan.md) (releases). Tracker: T-0040 (tag signature), T-0041 (protected environment), T-0068 (conformance in CI), T-0072 (release 0.1).

## 1. Workflows

| Workflow | Trigger | Jobs | Status |
|---|---|---|---|
| `lint.yml` | PR, push to `main` | `just lint` (markdownlint, yamllint, actionlint, ShellCheck, typos, editorconfig-checker); DCO check on PRs | exists |
| `build.yml` | PR, push to `main` | build and unit tests; AOT publish; CLI tests; conformance (§2) | with the .NET solution (T-0068) |
| `fuzz.yml` | schedule, manual | long fuzzing runs of the YAML front end ([validation-strategy.md](validation-strategy.md#6-fuzzing)) | with the parser (T-0069) |
| `codeql.yml` | PR, push to `main`, schedule | CodeQL for C# and for workflows | with the .NET solution |
| `release.yml` | push of a `cli-v*` tag | verify, build, gate, publish (§3) | before 0.1 (T-0072) |

Dependabot already updates GitHub Actions weekly; its NuGet block (commented out in `.github/dependabot.yml`) is enabled with the solution.

## 2. Build, test and conformance

`build.yml`, per runner in the matrix (§4):

1. **Checkout** without persisted credentials; `actions/setup-dotnet` from `global.json`.
2. **Restore** in locked mode (`packages.lock.json`), NuGet cache keyed on the lock files.
3. **Build** with warnings as errors; `dotnet format --verify-no-changes`.
4. **Unit, golden and executor tests** (`dotnet test`).
5. **AOT publish** `blokky` and `blokky-testkit` for the runner's RID; upload both as workflow artifacts (short retention). The testkit is never part of a release (§3).
6. **Platform and CLI tests** against the published binaries.
7. **Conformance:**
   - check out blokky-specs at the SHA in `conformance.lock` and verify that the pinned tag points to it (ADR 0004);
   - run the conformance runner against the AOT binaries;
   - write the totals and the **pass rate** to the job summary, upload the JSON lines and JUnit results;
   - fail the job on any `fail` or `error` (the release gate, [validation-strategy.md](validation-strategy.md#release-gate)).

Shells: `bash` and `sh` come with the image; `pwsh` is installed at a pinned version where the image's differs from the tested one (minimum version from spike 14). Container tests (0.7) run on Linux only, with the engine the spike E matrix picks.

Branch protection on `main` requires `lint`, `dco` and the build/conformance jobs.

## 3. Release

Releases are cut from `cli-v0.x.y` tags, which only the maintainer creates: annotated and signed ([ADR 0007](../adr/0007-runtime-plan.md), CONTRIBUTING.md). `release.yml`:

1. **Verify the tag** (T-0040): import the maintainer's public key from the repo, `git verify-tag` the tag object, and fail if the signature, the signer or the tag format is wrong. GitHub's tag rulesets can't check tag-object signatures (F-0001), so this step is the check.
2. **Check the version:** the tag matches the project version, and `CHANGELOG.md` has an entry for it.
3. **Build per RID, natively** (no cross-compilation): `linux-x64` and `linux-arm64` in 0.1; `win-x64` from 0.6; `osx-arm64` with the macOS port. No caches in release jobs.
4. **Gate:** CLI tests and the full conformance run on every RID against the binaries about to be released.
5. **Package:** `blokky-<version>-<rid>.tar.gz` (Linux, macOS) or `.zip` (Windows) holding `blokky`, `LICENSE`, `NOTICE` and `README.md`; a check fails the job if an archive holds anything else (the testkit in particular, [testkit.md](testkit.md#6-shipping)).
6. **Publish**, in a job bound to the protected `release` environment with manual approval (T-0041):
   - `SHA256SUMS` over the archives;
   - artifact attestations for every archive (`actions/attest-build-provenance`);
   - an SBOM per archive, attested with `actions/attest-sbom`;
   - a GitHub Release with notes from `CHANGELOG.md`, the spec version, the feature matrix and the conformance pass rate per OS.

Binaries are unsigned in 0.x; there is no `self-update` command; packaging comes later ([ROADMAP.md](../../ROADMAP.md)). `install.md` makes `gh attestation verify` the primary verification and the checksums the fallback.

## 4. Runners

| Runner | From | Used for |
|---|---|---|
| `ubuntu-24.04` | now | lint, build, tests, conformance, release (`linux-x64`) |
| `ubuntu-24.04-arm` | 0.1 | build, tests, conformance, release (`linux-arm64`) |
| `windows-2025` | 0.6 (spikes before) | build, tests, conformance, release (`win-x64`) |
| macOS (arm64) | the macOS port | the only macOS testing (no local Mac): spikes, tests, conformance, release (`osx-arm64`) |

Linux is also tested locally under WSL; Windows locally from WSL and in CI (ADR 0007). Spikes use the same runners on their unmerged `spikes/*` branches ([spike-plan.md](spike-plan.md)).

## 5. Hardening

- **Pinned actions:** every `uses:` is pinned to a full commit SHA with a version comment; `just pin` (pinact) pins new ones, actionlint checks them, Dependabot updates them.
- **Least privilege:** `permissions: {}` at the top of every workflow; each job asks only for what it needs (`contents: read` by default; `id-token: write` and `attestations: write` only in the publish job; `contents: write` only to create the Release).
- `persist-credentials: false` on checkout; no `pull_request_target`; untrusted values (branch names, PR titles) never interpolated into `run:` scripts.
- **Protected `release` environment** with the maintainer as required reviewer (T-0041); tag and branch protection with required checks; CODEOWNERS on `.github/workflows/**`.
- Timeouts on every job; concurrency groups cancel superseded PR runs, never release runs.
- Locked NuGet restore, package source mapping to nuget.org, CodeQL, Dependabot.

## 6. Open points

- **Bumping `conformance.lock`:** by hand (a one-line PR when a spec draft is tagged) or by automation (ADR 0004 left this to this doc).
- **SBOM generator and format** (SPDX or CycloneDX; Syft or a .NET tool).
- **Release-signing key distribution** for T-0040: a key file in the repo, or the maintainer's GitHub-published GPG keys.
- **A known-failures allowance** in the conformance gate ([validation-strategy.md](validation-strategy.md#release-gate)).

# Security policy

## Reporting a vulnerability

Please report vulnerabilities through **GitHub private vulnerability reporting**: on the repository's **Security** tab, choose **Report a vulnerability** ([direct link](https://github.com/gergodrazsdik/blokky-cli/security/advisories/new)). Do not open a public issue.

Include what you found, how to reproduce it, and the impact you expect. Problems in the language specification (for example a construct that hides what will run) can be reported here too.

## Supported versions

None yet. Blokky is in its specification phase and has no releases. Once releases exist, this section lists which versions get security fixes.

## Response

Blokky has a single maintainer, so responses are best effort. I aim to acknowledge a report within a week and will keep you updated while it's investigated.

## Scope

Before reporting, please read how Blokky reasons about security (the threat model will live in [docs/security/threat-model.md](docs/security/threat-model.md); the design brief's §1.2 summarizes it):

- **Blokky is not a sandbox.** The baseline is "the same thing without Blokky": what an agent could do with a plain shell call. A hostile plan is as dangerous as an unreviewed script from the internet.
- **In scope:** anything where using Blokky is *less* safe than the equivalent shell, for example granting capabilities, bypassing the agent runner's permission system, hiding what will run from `blokky leaves`/`blokky plan` or the hook, or deleting files outside a run's own artefacts.
- **Hygiene vs controls:** strict-mode lint, secret masking and display sanitization are hygiene, not security boundaries. Gaps in them are welcome as ordinary issues. The runner's permission system, the hook and the validator are the controls.
- **Resource exhaustion** (output floods, runaway loops) is possible without Blokky too and is not a vulnerability by itself.

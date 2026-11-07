# Installing Blokky

Per-platform install, attestation and checksum verification, macOS quarantine, upgrade and uninstall.

Status: not started. The sections below record decisions already made ([ADR 0008](adr/0008-spike-findings.md)); the rest is written with 0.1 (T-0071).

## Supported platforms

- **Linux** (`linux-x64`, `linux-arm64`): glibc 2.36 or later, for example Debian 12+, Ubuntu 24.04, Fedora 37+ and RHEL 10. There is no musl build yet.
- Windows follows in 0.6 and macOS later ([ADR 0007](adr/0007-runtime-plan.md)).

## Editor support

Plans are YAML files, and the plan schema gives editors completion, hover text and most type errors. The editor is a convenience: some mistakes (how a number or boolean is spelled, such as `010`, `True` or `1e3`) are invisible to it, so run `blokky validate` for the full check.

### The schema

The schema is published per spec version, at a raw GitHub URL of blokky-specs:

```text
https://raw.githubusercontent.com/gergodrazsdik/blokky-specs/<spec tag>/schema/blokky.schema.json
```

Use the tag of the spec version your plans name in `blokky:`, for example `v1.0.0-draft.5`:

```text
https://raw.githubusercontent.com/gergodrazsdik/blokky-specs/v1.0.0-draft.5/schema/blokky.schema.json
```

To use it offline, `blokky schema` prints the schema of the spec version the binary implements; save it to a file and point your editor at that file.

### Per file: the modeline

A comment on the first line of a plan ties it to the schema, whatever its name or directory:

```yaml
# yaml-language-server: $schema=https://raw.githubusercontent.com/gergodrazsdik/blokky-specs/v1.0.0-draft.5/schema/blokky.schema.json
blokky: 1.0
exec: [echo, hello]
```

It is a YAML comment, so Blokky ignores it.

### VS Code

With the Red Hat YAML extension, add this to your `settings.json`:

```json
{
  "yaml.yamlVersion": "1.2",
  "yaml.schemas": {
    "https://raw.githubusercontent.com/gergodrazsdik/blokky-specs/v1.0.0-draft.5/schema/blokky.schema.json": [
      "*.blokky.yaml",
      "**/.*/**/*.blokky.yaml"
    ]
  }
}
```

- Keep `yaml.yamlVersion` at `1.2` (the default). With `1.1`, the editor reads `yes` and `on` as booleans and misses or invents errors.
- Keep the second glob: the extension's globs skip dot directories, so without it a plan under `.github/` or another dot directory is silently not checked.

#!/usr/bin/env bash
# Runs the LSP probe over every case and example for yamlVersion 1.2 and 1.1.
# Files are copied to a directory without dot segments: the server's yaml.schemas
# globs (picomatch, dot: false) don't match paths such as .claude/ or .github/.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
specs=${SPECS:-$here/../../../blokky-specs}
work=${WORK:-$TMPDIR/schema-editor}
rm -rf "$work" && mkdir -p "$work/cases" "$work/examples"
cp "$here"/cases/*.yaml "$work/cases/"
cp "$specs"/examples/*/*.blokky.yaml "$work/examples/"
mkdir -p "$here/out"
c=$work/cases/90-completion.blokky.yaml
for v in 1.2 1.1; do
  uv run "$here/lsp_probe.py" "$specs/schema/blokky.schema.json" "$v" "$here/out/cases-$v.json" "$work"/cases/*.yaml \
    --complete "$c:3:12" "$c:4:8" "$c:8:12" "$c:9:1" "$c:7:5" \
    --hover "$c:1:2" "$c:3:3" "$c:4:2" "$c:6:5" "$c:7:6" "$c:8:6"
  uv run "$here/lsp_probe.py" "$specs/schema/blokky.schema.json" "$v" "$here/out/examples-$v.json" "$work"/examples/*.yaml
done

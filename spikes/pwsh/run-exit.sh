#!/bin/bash
# Run each exit/*.ps1 with and without the SH-17 prologue on each pwsh version.
set -u
here=$(cd "$(dirname "$0")" && pwd)
out=$here/out; mkdir -p "$out/gen"
declare -A PW=([7.2]="$here/tools/7.2/pwsh" [7.3]="$here/tools/7.3/pwsh" [7.4]="$here/tools/7.4/pwsh" [7.5]="$here/tools/7.5/pwsh" [7.6]="$(command -v pwsh)")
export DOTNET_ROLL_FORWARD=Major
printf '%-42s' case; for v in 7.2 7.3 7.4 7.5 7.6; do printf '%-10s' "$v" "$v+P"; done; echo
for f in "$here"/exit/*.ps1; do
  n=$(basename "$f" .ps1)
  { printf "\$ErrorActionPreference = 'Stop'; "; cat "$f"; } > "$out/gen/$n.P.ps1"
  printf '%-42s' "$n"
  for v in 7.2 7.3 7.4 7.5 7.6; do
    for mode in plain P; do
      file=$f; [ "$mode" = P ] && file=$out/gen/$n.P.ps1
      "${PW[$v]}" -NoProfile -NonInteractive -File "$file" >"$out/$n.$v.$mode.out" 2>"$out/$n.$v.$mode.err" </dev/null
      rc=$?
      printf '%-10s' "$rc"
    done
  done
  echo
done

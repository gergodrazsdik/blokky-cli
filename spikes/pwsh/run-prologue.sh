#!/bin/bash
# Spike 14: scanner verdict per case, then run on each pwsh: caught/uncaught throw line vs. source line.
set -u
here=$(cd "$(dirname "$0")" && pwd)
out=$here/out/prologue; mkdir -p "$out"
declare -A PW=([7.2]="$here/tools/7.2/pwsh" [7.3]="$here/tools/7.3/pwsh" [7.4]="$here/tools/7.4/pwsh" [7.5]="$here/tools/7.5/pwsh" [7.6]="$(command -v pwsh)")
export DOTNET_ROLL_FORWARD=Major NO_COLOR=1
for f in "$here"/prologue/*.ps1; do
  n=$(basename "$f" .ps1)
  want_x=$(grep -n "throw 'x'" "$f" | cut -d: -f1 | head -1)
  want_end=$(grep -n "throw 'end'" "$f" | cut -d: -f1 | head -1)
  if ! verdict=$("$here/scanner/bin/prologue" "$f" "$out/$n.ps1" 2>&1); then printf '%-28s %s\n' "$n" "$verdict"; continue; fi
  printf '%-28s %-20s want x=%s end=%s\n' "$n" "$verdict" "${want_x:--}" "${want_end:--}"
  for v in 7.2 7.3 7.4 7.5 7.6; do
    "${PW[$v]}" -NoProfile -NonInteractive -File "$out/$n.ps1" >"$out/$n.$v.out" 2>"$out/$n.$v.err" </dev/null
    rc=$?
    got_x=$(grep -o 'line=[0-9]*' "$out/$n.$v.out" | head -1)
    eap=$(grep -o 'eap=.*' "$out/$n.$v.out" | head -1)
    got_end=$(grep -o "$n\.ps1:[0-9]*" "$out/$n.$v.err" | head -1 | sed 's/.*://')
    printf '    %s rc=%s %s end=%s %s %s\n' "$v" "$rc" "${got_x:-line=-}" "${got_end:--}" "${eap:-eap=-}" \
      "$(grep -v '^\s*$' "$out/$n.$v.err" | head -1 | cut -c1-90)"
  done
done

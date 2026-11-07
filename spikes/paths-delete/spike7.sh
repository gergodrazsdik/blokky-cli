#!/bin/bash
# Spike 7: link-safe removal and creation of run-owned files (Linux).
# Usage: spike7.sh [runs] [rmtree|dotnetdelete]
set -u
here=$(cd "$(dirname "$0")" && pwd)
bk=$here/out/PathsDelete
runs=${1:-100}
mode=${2:-rmtree}
T=$(mktemp -d "${TMPDIR:-/tmp}/spike7.XXXXXX")
mkdir -p "$T/outside/dir/deep" "$T/root"
chmod 700 "$T/root"
for i in $(seq 1 50); do echo "keep $i" >"$T/outside/dir/f$i"; done
echo keep >"$T/outside/dir/deep/x"
echo keep >"$T/outside/file"
fingerprint() { find "$T/outside" -printf '%p %s\n' | sort | sha256sum; }
before=$(fingerprint)

mktree() { # $1: tree path
  local t=$1
  mkdir -p "$t/a/b/c" "$t/d/sub/s1/s2"
  for i in $(seq 1 50); do : >"$t/a/b/c/f$i"; : >"$t/d/sub/f$i"; : >"$t/d/sub/s1/s2/g$i"; done
  ln -s "$T/outside/file" "$t/link-file"
  ln -s "$T/outside/dir" "$t/link-dir"
  ln -s "$T/outside/dir" "$t/a/b/link-dir-deep"
  ln -s "$T/nowhere" "$t/dangling"
  ln -s "$T/outside/dir" "$t/d/lnk"     # the swapper exchanges d/sub and d/lnk
}

echo "== static tree (no race), $mode"
mktree "$T/root/static"
"$bk" "$mode" "$T/root/static"
[ -e "$T/root/static" ] && echo "FAIL: tree still there"
[ "$(fingerprint)" = "$before" ] && echo "outside intact" || echo "FAIL: outside changed"

echo "== TOCTOU: RENAME_EXCHANGE of d/sub <-> d/lnk (-> outside/dir) during removal, $runs runs, $mode"
fail=0; leftover=0
for r in $(seq 1 "$runs"); do
  t="$T/root/run$r"
  mktree "$t"
  "$here/c/swapper" "$t/d/sub" "$t/d/lnk" &
  sw=$!
  "$bk" "$mode" "$t" >"$T/last.out" 2>&1
  kill "$sw"; wait "$sw" 2>/dev/null
  [ -e "$t" ] && { leftover=$((leftover + 1)); cat "$T/last.out"; }
  if [ "$(fingerprint)" != "$before" ]; then
    fail=$((fail + 1)); echo "run $r: outside changed: $(cat "$T/last.out")"
    rm -rf "$T/outside"; mkdir -p "$T/outside/dir/deep"
    for i in $(seq 1 50); do echo "keep $i" >"$T/outside/dir/f$i"; done
    echo keep >"$T/outside/dir/deep/x"; echo keep >"$T/outside/file"
  fi
  cat "$T/last.out" >>"$T/all.out"
done
echo "runs=$runs outside_damaged=$fail tree_left_over=$leftover"
grep -o "retries=[0-9]*" "$T/all.out" | cut -d= -f2 | awk '{s+=$1; if ($1>0) n++} END {print "runs_with_retries=" n+0 " total_retries=" s+0}'

echo "== creation (EM-69)"
c="$T/root/create"
mkdir -m 700 "$c"
ln -s "$T/outside/dir" "$c/link-dir"
ln -s "$T/outside/file" "$c/link-file"
for n in dangling dangling2 dangling3 dangling4; do ln -s "$T/created-$n" "$c/$n"; done
"$bk" create "$c"
echo "targets created through dangling links:"; find "$T" -maxdepth 1 -name "created-*" | grep . || echo "(none)"
[ -e "$T/outside/dir/made-by-dotnet" ] && echo "Directory.CreateDirectory wrote through link-dir"
ln -s "$c" "$T/root/link-root"
"$bk" create "$T/root/link-root" | head -1
rm -rf "$T"

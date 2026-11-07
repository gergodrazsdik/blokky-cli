#!/usr/bin/env bash
# Exercises the spike launcher. Usage: test.sh [tree|lifeline|thread|net|drain|all] [runs]
set -u
here=$(cd "$(dirname "$0")" && pwd)
L=${L:-$here/out/launcher}
what=${1:-all}
runs=${2:-50}
W=$(mktemp -d)
trap 'pkill -f "sleep 60[0-9][0-9]" 2>/dev/null; rm -rf "$W"' EXIT

# tree.sh N TAG: one level of a process tree; level 1 ignores SIGTERM (forces SIGKILL).
cat >"$W/tree.sh" <<'EOF'
n=$1; tag=$2
[ "$n" -eq 1 ] && trap '' TERM
if [ "$n" -gt 0 ]; then sh "$(dirname "$0")/tree.sh" $((n - 1)) "$tag" & fi
sleep "$tag" & wait
EOF

alive() { pgrep -f "^sleep $1" | wc -l; }

leaf_tree() { # $1 = sleep tag
  printf '%s' "sh $W/tree.sh 3 $1 & setsid sleep $(($1 + 1)) & ( sleep $(($1 + 2)) & ) & ( setsid sleep $(($1 + 3)) & ) & ( env -i setsid sleep $(($1 + 4)) & ) & sleep 0.3; exec sleep $1"
}

t_tree() { # spike 1: timeout kills the group; escapees best-effort
  for mode in "--launch spawn" "--launch spawn --subreaper"; do
    fail=0
    for i in $(seq "$runs"); do
      # shellcheck disable=SC2086  # mode is a list of flags
      out=$("$L" run $mode --lifeline none --timeout 500 --grace 300 "$(leaf_tree 6010)")
      sleep 0.05
      n=0
      for k in 0 1 2 3 4; do n=$((n + $(alive $((6010 + k))))); done
      if [ "$n" -ne 0 ]; then
        fail=$((fail + 1))
        [ "$fail" -eq 1 ] && echo "  survivors: $(pgrep -af 'sleep 601[0-9]' | tr '\n' ';')"
      fi
      [ "$i" -eq 1 ] && echo "  sample: ${out%% stdout=*}"
      pkill -f 'sleep 601[0-9]'
    done
    echo "tree [$mode]: $runs runs, runs with survivors: $fail"
  done
}

# Nested chain of DEPTH hosts; the innermost leaf runs a tree. Then kill -9 the top host.
t_lifeline() {
  depth=10
  for mode in "--launch spawn --lifeline none" "--launch tramp --lifeline pdeathsig" \
    "--launch spawn --lifeline watchdog" "--launch tramp --lifeline watchdog"; do
    fail=0; maxleft=0
    for i in $(seq "$runs"); do
      inner="sh $W/tree.sh 2 6020"
      cmd=$inner
      for _ in $(seq $((depth - 1))); do cmd="$L run $mode --timeout 60000 '$(printf '%s' "$cmd" | sed "s/'/'\\\\''/g")'"; done
      # shellcheck disable=SC2086  # mode is a list of flags
      "$L" run $mode --timeout 60000 --pidfile "$W/top.pid" "$cmd" >/dev/null &
      for _ in $(seq 200); do [ "$(alive 6020)" -ge 3 ] && break; sleep 0.02; done
      before=$(pgrep -f "launcher run" | wc -l)
      kill -9 "$(cat "$W/top.pid")"
      sleep 1
      left=$(($(alive 6020) + $(pgrep -f "launcher run" | wc -l)))
      [ "$left" -gt 0 ] && fail=$((fail + 1))
      [ "$left" -gt "$maxleft" ] && maxleft=$left
      [ "$i" -eq 1 ] && echo "  sample: hosts before kill=$before, alive 1s after kill -9: $left"
      pkill -9 -f 'sleep 602[0-9]'; pkill -9 -f "launcher run" ; wait 2>/dev/null
    done
    echo "lifeline [$mode]: $runs runs, runs with survivors after 1s: $fail (max $maxleft)"
  done
}

t_thread() { # PDEATHSIG is per thread: spawn from a thread that exits
  for th in main ephemeral; do
    echo "thread=$th: $("$L" run --launch tramp --lifeline none --thread "$th" --timeout 3000 'sleep 0.5; echo survived')"
  done
}

t_net() { # .NET 11 Process: StartDetached / KillOnParentExit
  for combo in "0 0 main" "1 0 main" "0 1 main" "1 1 main" "1 1 ephemeral"; do
    # shellcheck disable=SC2086  # combo is three words on purpose
    "$L" probe-net $combo 'sleep 6031 & exec sleep 6030' >"$W/net.out" &
    hp=$!
    sleep 0.6
    pre=$(alive 6030)
    kill -9 "$hp"; wait "$hp" 2>/dev/null
    sleep 0.5
    echo "net [detached kill-on-parent-exit thread = $combo]: $(head -1 "$W/net.out"); child alive before kill=$pre; after kill -9: child alive=$(alive 6030) grandchild alive=$(alive 6031)"
    pkill -f 'sleep 603[0-9]'
  done
}

t_drain() { # spike 8L
  echo "late output within drain:"
  "$L" run --drain 1000 'echo early; (sleep 0.5; echo late) &'
  echo "held pipe, holder ignores SIGTERM (drain 1000 + grace 500):"
  s=$(date +%s%N)
  "$L" run --drain 1000 --grace 500 'echo x; (trap "" TERM; sleep 6040; echo never) &'
  echo "  wall ms: $((($(date +%s%N) - s) / 1000000)), holder alive: $(alive 6040)"
  echo "order: next sibling vs dying descendant ($runs runs):"
  bad=0
  for i in $(seq "$runs"); do
    "$L" run --drain 1000 --grace 2000 \
      "(trap 'sleep 0.3; date +%s%N >$W/a_end; exit 0' TERM; while :; do sleep 0.05; done) & echo a" \
      "date +%s%N >$W/b_start" >"$W/order.out"
    [ "$(cat "$W/a_end")" -lt "$(cat "$W/b_start")" ] || bad=$((bad + 1))
    [ "$i" -eq 1 ] && sed 's/^/  /; s/ start_ns=[0-9]* end_ns=[0-9]*//' "$W/order.out"
  done
  echo "  overlaps: $bad"
}

case $what in
  tree) t_tree ;; lifeline) t_lifeline ;; thread) t_thread ;; net) t_net ;; drain) t_drain ;;
  all) t_tree; t_lifeline; t_thread; t_net; t_drain ;;
esac

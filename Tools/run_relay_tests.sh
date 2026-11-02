#!/usr/bin/env bash
# Real Unity Relay multiplayer tests using headless player builds (no editor).
#
#   Tools/run_relay_tests.sh [pair|capacity|hostloss|hostquit|latency|combat|combatlag|combatnolag|net|all] [binary]
#
# Each test starts separate game processes, each signed in as its own anonymous Unity
# player (-profile), and communicating only through Unity Relay. Results land in
# AutoTest/<test>_<time>/ as JSON + logs. Exit code is 0 only if every test passed.
set -u
cd "$(dirname "$0")/.."
APP="${2:-$PWD/Builds/macOS/SALTLINE.app/Contents/MacOS/SALTLINE}"
[ -x "$APP" ] || { echo "player binary not found: $APP"; exit 2; }
WHICH="${1:-all}"
OVERALL=0

player() { # run a headless player in the foreground; the log file is what matters
  "$APP" -batchmode -nographics "$@" >/dev/null 2>&1
}
# Background launches use `( exec ... ) &` so that $! is the game process itself,
# which the host-loss test needs to kill it without a clean shutdown.
bgplayer() { ( exec "$APP" -batchmode -nographics "$@" >/dev/null 2>&1 ) & }

summary() { # dir
  for f in "$1"/*.log; do
    [ -f "$f" ] || continue
    grep -h "\[SALTLINE\]\[AUTOTEST\]" "$f" | sed 's/.*AUTOTEST\] //' | sed "s|^|  $(basename "$f" .log): |"
  done
}

result_of() { grep -h "\"result\"" "$1" 2>/dev/null | sed 's/.*: "\(.*\)".*/\1/'; }

record() { # name status
  printf "%-10s %s\n" "$1" "$2" | tee -a AutoTest/summary.txt
  [ "$2" = "PASS" ] || OVERALL=1
}

mkdir -p AutoTest
echo "== $(date '+%Y-%m-%d %H:%M:%S') $(basename "$APP") ==" >> AutoTest/summary.txt

test_pair() {
  local R="$PWD/AutoTest/pair_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest host -profile t_host -codefile "$R/code.txt" -clients 1 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  player -autotest client -profile t_c1 -codefile "$R/code.txt" -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record pair PASS || record pair FAIL
}

test_capacity() {
  local R="$PWD/AutoTest/capacity_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest host -profile t_host -codefile "$R/code.txt" -clients 7 -expectfull -holdopen 45 -testtimeout 150 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  local pids=()
  for i in 1 2 3 4 5 6 7; do
    player -autotest client -profile "t_c$i" -codefile "$R/code.txt" -holdopen 30 -testtimeout 150 -testout "$R/c$i.json" -logFile "$R/c$i.log" & pids+=($!)
    sleep 1
  done
  sleep 25 # the 9th player arrives while all 8 are connected
  player -autotest client -expectreject -profile t_c9 -codefile "$R/code.txt" -testout "$R/c9.json" -logFile "$R/c9.log"
  for p in "${pids[@]}"; do wait "$p"; done; wait $H
  summary "$R"
  local ok=PASS
  [ "$(result_of "$R/host.json")" = "PASS" ] || ok=FAIL
  for i in 1 2 3 4 5 6 7 9; do [ "$(result_of "$R/c$i.json")" = "PASS" ] || ok=FAIL; done
  record capacity $ok
}

test_hostloss() {
  local R="$PWD/AutoTest/hostloss_$(date +%H%M%S)"; mkdir -p "$R"
  bgplayer -autotest host -profile t_hostloss -codefile "$R/code.txt" -clients 1 -idleafter 120 -testout "$R/host.json" -logFile "$R/host.log"; local H=$!
  player -autotest client -profile t_c1 -codefile "$R/code.txt" -waitforhostloss 30 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  # wait for the client's PASS verdict, then kill the host without a clean shutdown
  for _ in $(seq 1 120); do grep -q "host verdict = PASS" "$R/c1.log" 2>/dev/null && break; sleep 1; done
  # kill the game process itself (not the wrapper shell) with no clean shutdown
  sleep 2; kill -9 "$H" 2>/dev/null; echo "  killed host game process $H at $(date +%T)"
  wait $C
  summary "$R"
  [ "$(result_of "$R/c1.json")" = "PASS" ] && record hostloss PASS || record hostloss FAIL
}

test_hostquit() { # host exits normally right after validating; the client must be told
  local R="$PWD/AutoTest/hostquit_$(date +%H%M%S)"; mkdir -p "$R"
  bgplayer -autotest host -profile t_hostquit -codefile "$R/code.txt" -clients 1 -testout "$R/host.json" -logFile "$R/host.log"; local H=$!
  player -autotest client -profile t_c1 -codefile "$R/code.txt" -waitforhostloss 30 -testout "$R/c1.json" -logFile "$R/c1.log"
  wait $H
  summary "$R"
  [ "$(result_of "$R/c1.json")" = "PASS" ] && record hostquit PASS || record hostquit FAIL
}

test_latency() { # 150ms delay, 30ms jitter, 3% loss injected on the client
  local R="$PWD/AutoTest/latency_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest host -profile t_host -codefile "$R/code.txt" -clients 1 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  player -autotest client -profile t_c1 -netsim 150,30,3 -codefile "$R/code.txt" -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record latency PASS || record latency FAIL
}

run_combat() { # name "host args" "client args"
  local R="$PWD/AutoTest/$1_$(date +%H%M%S)"; mkdir -p "$R"
  # shellcheck disable=SC2086
  player -autotest combat-host -profile t_host -codefile "$R/code.txt" -testtimeout 200 $2 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  # shellcheck disable=SC2086
  player -autotest combat-client -profile t_c1 -codefile "$R/code.txt" -testtimeout 200 $3 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record "$1" PASS || record "$1" FAIL
}
# Movement prediction + authoritative hits over Relay (bots on both sides).
test_combat() { run_combat combat "" ""; }
# Same with 120 ms delay, 20 ms jitter, 1% loss injected on the client: lag compensation must keep hits registering.
test_combatlag() { run_combat combatlag "" "-netsim 120,20,1"; }
# Control: lag compensation disabled on the host, same latency. The client must now mostly MISS,
# which shows the lag-compensated runs are not passing by accident.
test_combatnolag() { run_combat combatnolag "-lagcomp 0" "-netsim 120,20,1 -expectmiss"; }

case "$WHICH" in
  pair) test_pair ;;
  capacity) test_capacity ;;
  hostloss) test_hostloss ;;
  hostquit) test_hostquit ;;
  latency) test_latency ;;
  combat) test_combat ;;
  combatlag) test_combatlag ;;
  combatnolag) test_combatnolag ;;
  net) test_pair; test_capacity; test_hostloss; test_hostquit; test_latency ;;
  gameplay) test_combat; test_combatlag; test_combatnolag ;;
  all) test_pair; test_capacity; test_hostloss; test_hostquit; test_latency; test_combat; test_combatlag; test_combatnolag ;;
  *) echo "unknown test: $WHICH"; exit 2 ;;
esac
exit $OVERALL

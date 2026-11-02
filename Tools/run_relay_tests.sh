#!/usr/bin/env bash
# Real Unity Relay multiplayer tests using headless player builds (no editor).
#
#   Tools/run_relay_tests.sh [pair|capacity|hostloss|hostquit|latency|combat|combatlag|combatnolag|match|matchdraw|ui|net|gameplay|all] [binary]
#
# Each test starts separate game processes, each signed in as its own anonymous Unity
# player (-profile), and communicating only through Unity Relay. Results land in
# AutoTest/<test>_<time>/ as JSON + logs. Exit code is 0 only if every test passed.
set -u
cd "$(dirname "$0")/.."
APP="${2:-$PWD/Builds/macOS/SALTLINE.app/Contents/MacOS/SALTLINE}"
# Latency-simulation tests need a development build (the simulator is stripped from release builds).
DEVAPP="${DEVAPP:-$PWD/Builds/macOS-dev/SALTLINE.app/Contents/MacOS/SALTLINE}"
[ -x "$APP" ] || { echo "player binary not found: $APP"; exit 2; }
WHICH="${1:-all}"
OVERALL=0

player() { # run a headless player in the foreground; the log file is what matters
  "$APP" -batchmode -nographics "$@" >/dev/null 2>&1
}
needdev() { [ -x "$DEVAPP" ] || { echo "development build not found: $DEVAPP (build with -devbuild)"; record "$1" FAIL; return 1; }; APP="$DEVAPP"; }
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
  local SAVED="$APP"; needdev latency || return; local R="$PWD/AutoTest/latency_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest host -profile t_host -codefile "$R/code.txt" -clients 1 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  player -autotest client -profile t_c1 -netsim 150,30,3 -codefile "$R/code.txt" -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record latency PASS || record latency FAIL
  APP="$SAVED"
}

run_combat() { # name "host args" "client args"
  local R="$PWD/AutoTest/$1_$(date +%H%M%S)"; mkdir -p "$R"
  # shellcheck disable=SC2086
  # Shorter respawn (2 s) + spawn protection (1 s) than the 5 s / 2 s defaults so each phase
  # collects enough shots; hit registration itself is unaffected by these rules.
  player -autotest combat-host -profile t_host -codefile "$R/code.txt" -testtimeout 240 -respawndelay 2 -spawnprotect 1 $2 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  # shellcheck disable=SC2086
  player -autotest combat-client -profile t_c1 -codefile "$R/code.txt" -testtimeout 240 $3 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record "$1" PASS || record "$1" FAIL
}
# Movement prediction + authoritative hits over Relay (bots on both sides).
test_combat() { run_combat combat "" ""; }
# Same with +50 ms each way, 10 ms jitter, 1% loss on the client (about 170 ms round trip over Relay):
# a poor but common connection. Lag compensation must keep hits registering. The rewind needed is
# roughly round trip + interpolation, which stays inside the host's 350 ms rewind cap.
LAGSIM="${LAGSIM:-50,10,1}"
test_combatlag() { local SAVED="$APP"; needdev combatlag || return; run_combat combatlag "" "-netsim $LAGSIM"; APP="$SAVED"; }
# Control: lag compensation disabled on the host, same latency. The client must now mostly MISS,
# which shows the lag-compensated runs are not passing by accident.
test_combatnolag() { local SAVED="$APP"; needdev combatnolag || return; run_combat combatnolag "-lagcomp 0" "-netsim $LAGSIM -expectmiss"; APP="$SAVED"; }

# Team Deathmatch flow: lobby rules, warmup, live, score limit, results, back to the lobby.
# Short custom rules so a full match fits in about a minute. Kills are applied on the host via
# the normal damage path (aim and hit registration are what the combat tests cover).
test_match() {
  local R="$PWD/AutoTest/match_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest match-host -profile t_host -codefile "$R/code.txt" -testtimeout 200 -warmup 5 -scorelimit 3 -timelimit 120 -resultstime 5 -respawndelay 1 -spawnprotect 0.5 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  player -autotest match-client -profile t_c1 -codefile "$R/code.txt" -testtimeout 200 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record match PASS || record match FAIL
}

# Public match, time-limit draw, plus a third player who finds the match by browsing and joins
# mid-match (assist, team-kill rule, leaving mid-match).
test_matchdraw() {
  local R="$PWD/AutoTest/matchdraw_$(date +%H%M%S)"; mkdir -p "$R"
  player -autotest match-host -scenario draw -latefile "$R/late.txt" -profile t_host -codefile "$R/code.txt" -testtimeout 240 -warmup 5 -scorelimit 50 -timelimit 60 -resultstime 5 -respawndelay 1 -spawnprotect 0.5 -testout "$R/host.json" -logFile "$R/host.log" & local H=$!
  player -autotest match-client -expectwinner none -profile t_c1 -codefile "$R/code.txt" -testtimeout 240 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  player -autotest match-late -profile t_c2 -codefile "$R/late.txt" -testtimeout 240 -testout "$R/late.json" -logFile "$R/late.log" & local L=$!
  wait $L; wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && [ "$(result_of "$R/late.json")" = "PASS" ] && record matchdraw PASS || record matchdraw FAIL
}

# Menus and HUD: a rendering host (batch mode with Metal, no window) presses the real buttons and
# saves a composited picture of every screen; a headless client joins, readies and plays along.
test_ui() {
  local R="$PWD/AutoTest/ui_$(date +%H%M%S)"; mkdir -p "$R/shots"
  ( exec "$APP" -batchmode -uitest host -capture "$R/shots" -profile t_host -codefile "$R/code.txt" -testtimeout 240 -warmup 4 -scorelimit 3 -timelimit 120 -resultstime 6 -respawndelay 1 -spawnprotect 0.5 -testout "$R/host.json" -logFile "$R/host.log" >/dev/null 2>&1 ) & local H=$!
  player -autotest match-client -profile t_c1 -codefile "$R/code.txt" -testtimeout 240 -testout "$R/c1.json" -logFile "$R/c1.log" & local C=$!
  wait $C; wait $H
  summary "$R"
  [ "$(result_of "$R/host.json")" = "PASS" ] && [ "$(result_of "$R/c1.json")" = "PASS" ] && record ui PASS || record ui FAIL
}

case "$WHICH" in
  pair) test_pair ;;
  capacity) test_capacity ;;
  hostloss) test_hostloss ;;
  hostquit) test_hostquit ;;
  latency) test_latency ;;
  combat) test_combat ;;
  combatlag) test_combatlag ;;
  combatnolag) test_combatnolag ;;
  match) test_match ;;
  matchdraw) test_matchdraw ;;
  ui) test_ui ;;
  net) test_pair; test_capacity; test_hostloss; test_hostquit; test_latency ;;
  gameplay) test_combat; test_combatlag; test_combatnolag; test_match; test_matchdraw ;;
  all) test_pair; test_capacity; test_hostloss; test_hostquit; test_latency; test_combat; test_combatlag; test_combatnolag; test_match; test_matchdraw ;;
  *) echo "unknown test: $WHICH"; exit 2 ;;
esac
exit $OVERALL

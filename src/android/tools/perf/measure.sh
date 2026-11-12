#!/usr/bin/env bash
# Measures cold start, frame jank, resident memory and APK size on one connected device.
# Usage: tools/perf/measure.sh [-s SERIAL] [-a APK] [-t "X,Y"] [-n RUNS]
#   -s  adb serial (default: the only device)
#   -a  APK to install and size (default: app/build/outputs/apk/debug/app-debug.apk)
#   -t  tap point that opens the scrolled screen before the jank run (default: none)
#   -n  cold start runs (default: 10)
set -euo pipefail

PKG=top.mccampfire.app
ACTIVITY=$PKG/.MainActivity
APK=app/build/outputs/apk/debug/app-debug.apk
RUNS=10
TAP=""
SERIAL=""
while getopts "s:a:t:n:" opt; do
  case $opt in
    s) SERIAL=$OPTARG ;;
    a) APK=$OPTARG ;;
    t) TAP=$OPTARG ;;
    n) RUNS=$OPTARG ;;
    *) exit 2 ;;
  esac
done

ADB=(adb)
[ -n "$SERIAL" ] && ADB=(adb -s "$SERIAL")
sh() { "${ADB[@]}" shell "$@"; }

median() { sort -n | awk '{a[NR]=$1} END {print (NR % 2) ? a[(NR+1)/2] : (a[NR/2] + a[NR/2+1]) / 2}'; }

"${ADB[@]}" install -r "$APK" >/dev/null
echo "apk_bytes=$(wc -c < "$APK" | tr -d ' ')"

starts=()
for _ in $(seq "$RUNS"); do
  t=$(sh am start -W -S -n "$ACTIVITY" | tr -d '\r' | awk '/TotalTime/ {print $2}')
  starts+=("$t")
  sleep 1
done
echo "cold_start_ms_runs=${starts[*]}"
echo "cold_start_ms_median=$(printf '%s\n' "${starts[@]}" | median)"

sleep 2
[ -n "$TAP" ] && { sh input tap "${TAP%,*}" "${TAP#*,}"; sleep 2; }
sh dumpsys gfxinfo $PKG reset >/dev/null
for _ in $(seq 6); do
  sh input swipe 600 2000 600 700 250
  sh input swipe 600 700 600 2000 250
done
sleep 1
sh dumpsys gfxinfo $PKG | tr -d '\r' | grep -E "Total frames rendered|Janky frames|50th percentile|90th percentile|95th percentile|99th percentile" | sed 's/^ *//'

echo "pss_kb=$(sh dumpsys meminfo $PKG | tr -d '\r' | awk '/TOTAL PSS:/ {print $3}' | head -1)"

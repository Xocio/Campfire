#!/usr/bin/env bash
# Runs every merge gate in order; CI and local runs use the same script.
set -euo pipefail
cd "$(dirname "$0")/.."

gradlew() { bash ./gradlew "$@"; }

echo "== unit tests, lint, debug build"
gradlew testDebugUnitTest lintDebug assembleDebug

echo "== token boundary"
node tools/check/tokens.cjs check

echo "== single-style release builds"
APK=app/build/outputs/apk/release/app-release-unsigned.apk
mkdir -p build/size
gradlew assembleRelease -Pzeroui.style=miuix
cp "$APK" build/size/miuix.apk
gradlew assembleRelease -Pzeroui.style=material
cp "$APK" build/size/material.apk

echo "== default release build and size limits"
gradlew assembleRelease
cp "$APK" build/size/default.apk
node tools/size/check.cjs "$APK"
node tools/size/styles.cjs build/size/default.apk build/size/miuix.apk build/size/material.apk

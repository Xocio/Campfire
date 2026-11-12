#!/usr/bin/env bash
# Builds the EasyTier Android libraries for arm64-v8a and x86_64 and copies them into the easytier module.
# Runs on a Linux host with rustup, cargo-ndk, protoc and the Android NDK.
# Usage: tools/easytier/build-jni.sh [tag]
# Environment: EASYTIER_SRC (source checkout), ANDROID_NDK_HOME, CARGO_BUILD_JOBS and CARGO_PROFILE_RELEASE_* overrides.
set -euo pipefail

TAG="${1:-v2.6.4}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="${EASYTIER_SRC:-$HOME/work/easytier}"
OUT="$ROOT/easytier/src/main/jniLibs"
PLATFORM=24
export ANDROID_NDK_HOME="${ANDROID_NDK_HOME:-/opt/android-sdk/ndk/30.0.14904198}"
export RUSTFLAGS="${RUSTFLAGS:-} -C link-arg=-Wl,-z,max-page-size=16384"

# shellcheck disable=SC1091
source "$HOME/.cargo/env"

if [ ! -d "$SRC" ]; then
  git clone --depth 1 --branch "$TAG" https://github.com/EasyTier/EasyTier.git "$SRC"
fi
cd "$SRC"
if [ "$(git describe --tags --exact-match 2>/dev/null)" != "$TAG" ]; then
  echo "source at $SRC is not tag $TAG" >&2
  exit 1
fi
COMMIT="$(git rev-parse HEAD)"

(cd easytier-contrib/easytier-ffi && cargo ndk -t arm64-v8a -t x86_64 --platform "$PLATFORM" build --release)
for abi in arm64-v8a x86_64; do
  case "$abi" in
    arm64-v8a) triple=aarch64-linux-android ;;
    x86_64) triple=x86_64-linux-android ;;
  esac
  (cd easytier-contrib/easytier-android-jni && cargo ndk -t "$abi" --platform "$PLATFORM" rustc --release --lib -- \
    -L "native=$SRC/target/$triple/release" -l dylib=easytier_ffi)
done

READELF="$(ls "$ANDROID_NDK_HOME"/toolchains/llvm/prebuilt/*/bin/llvm-readelf | head -1)"
STRIP="$(ls "$ANDROID_NDK_HOME"/toolchains/llvm/prebuilt/*/bin/llvm-strip | head -1)"

INFO="$ROOT/easytier/prebuilt.txt"
{
  echo "easytier $TAG $COMMIT"
  echo "rustc $(rustc --version | cut -d' ' -f2)"
  echo "cargo-ndk $(cargo ndk --version | cut -d' ' -f2)"
  echo "ndk $(basename "$ANDROID_NDK_HOME")"
  echo "platform $PLATFORM"
} > "$INFO"

for abi in arm64-v8a x86_64; do
  case "$abi" in
    arm64-v8a) triple=aarch64-linux-android ;;
    x86_64) triple=x86_64-linux-android ;;
  esac
  mkdir -p "$OUT/$abi"
  for lib in libeasytier_android_jni.so libeasytier_ffi.so; do
    cp "$SRC/target/$triple/release/$lib" "$OUT/$abi/$lib"
    "$STRIP" --strip-unneeded "$OUT/$abi/$lib"
    if "$READELF" -lW "$OUT/$abi/$lib" | awk '/LOAD/ && $NF != "0x4000" { bad = 1 } END { exit !bad }'; then
      echo "$abi/$lib is not 16 KB aligned" >&2
      exit 1
    fi
    echo "sha256 $abi/$lib $(sha256sum "$OUT/$abi/$lib" | cut -d' ' -f1)" >> "$INFO"
  done
done

cat "$INFO"

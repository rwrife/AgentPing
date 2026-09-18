#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
BUILD_DIR=$(mktemp -d)
trap 'rm -rf "$BUILD_DIR"' EXIT
${CXX:-c++} -std=c++17 -Wall -Wextra -Werror -pedantic \
  -I"$ROOT/firmware/src" "$ROOT/firmware/src/protocol_core.cpp" \
  "$ROOT/firmware/tests/protocol_core_test.cpp" -o "$BUILD_DIR/protocol_core_test"
"$BUILD_DIR/protocol_core_test"
for BOARD in WAVESHARE_ESP32_C6_TOUCH_AMOLED_1_64 WAVESHARE_ESP32_S3_LCD_1_47B; do
  ${CXX:-c++} -std=c++17 -Wall -Wextra -Werror -pedantic \
    -D"AGENTPING_TARGET_$BOARD=1" -I"$ROOT/firmware/src" \
    "$ROOT/firmware/tests/display_profile_test.cpp" -o "$BUILD_DIR/display_profile_test"
  "$BUILD_DIR/display_profile_test"
done

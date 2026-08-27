#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
BUILD_DIR=$(mktemp -d)
trap 'rm -rf "$BUILD_DIR"' EXIT
${CXX:-c++} -std=c++17 -Wall -Wextra -Werror -pedantic \
  -I"$ROOT/firmware/src" "$ROOT/firmware/src/protocol_core.cpp" \
  "$ROOT/firmware/tests/protocol_core_test.cpp" -o "$BUILD_DIR/protocol_core_test"
"$BUILD_DIR/protocol_core_test"

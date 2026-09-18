#include "connection_wait_display.h"
#include <cassert>
#include <cstdio>

int main() {
  agentping::ConnectionWaitDisplay display;
  // Startup is excluded; timing begins when the waiting screen first appears.
  assert(!display.update(0, false));
  constexpr int64_t start = 5000000;
  assert(!display.update(start, true));
  assert(!display.update(start + 119999999, true));
  assert(display.update(start + 120000000, true));
  assert(display.update(start + 149999999, true));
  assert(!display.update(start + 150000000, true));
  assert(!display.update(start + 269999999, true));
  assert(display.update(start + 270000000, true));
  // Connecting during blackout wakes immediately and stays awake thereafter.
  assert(!display.update(start + 270000001, false));
  assert(!display.update(start + 900000000, false));
  // Re-entering a waiting screen starts a fresh visible interval.
  constexpr int64_t restart = 1000000000;
  assert(!display.update(restart, true));
  assert(display.update(restart + 120000000, true));
  // A delayed loop samples the right cycle; clocks beyond 32-bit us are safe.
  assert(display.update(restart + 150000000LL * 1000 + 120000000, true));
  assert(!display.update(restart + 150000000LL * 1001, true));
  std::puts("connection_wait_display: all tests passed");
}

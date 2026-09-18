#pragma once
#include <cstdint>

namespace agentping {
// Keep time independent of animation transitions and renderer pause/resume.
class ConnectionWaitDisplay {
 public:
  static constexpr int64_t kVisibleUs = 120000000;
  static constexpr int64_t kBlackUs = 30000000;

  bool update(int64_t now, bool waiting) {
    if (!waiting) {
      waiting_since_ = -1;
      return false;
    }
    if (waiting_since_ < 0) waiting_since_ = now;
    return (now - waiting_since_) % (kVisibleUs + kBlackUs) >= kVisibleUs;
  }

 private:
  int64_t waiting_since_ = -1;
};
}  // namespace agentping

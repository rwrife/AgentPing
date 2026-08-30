#pragma once
#include "protocol_core.h"

#include <string>

namespace agentping::ui {

struct PendingAction {
  std::string action;
  std::string text;
  bool confirmed = false;
};

void initialize();
void render(const ViewModel& model);
void task();
bool take_action(PendingAction& output);

}  // namespace agentping::ui

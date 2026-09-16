#pragma once
#include <cstdio>
#include <cstring>

namespace agentping {
struct NotificationCommand {
  char id[17]{};
  unsigned provider = 0;
  unsigned kind = 0;
};
inline bool parse_notification(const char* line, NotificationCommand& output) {
  char provider[16]{}, kind[16]{}, extra{};
  if (std::sscanf(line, "notify %16s %15s %15s %c", output.id, provider, kind, &extra) != 3)
    return false;
  if (std::strlen(output.id) != 16 || std::strspn(output.id,"0123456789abcdef") != 16)
    return false;
  const char* providers[] = {"codex", "claude", "copilot"};
  const char* kinds[] = {"attention", "completed", "error", "thinking"};
  bool valid_provider = false, valid_kind = false;
  for (unsigned i=0; i<sizeof(providers)/sizeof(providers[0]); ++i)
    if (!std::strcmp(provider,providers[i])) { output.provider=i; valid_provider=true; }
  for (unsigned i=0; i<sizeof(kinds)/sizeof(kinds[0]); ++i)
    if (!std::strcmp(kind,kinds[i])) { output.kind=i; valid_kind=true; }
  return valid_provider && valid_kind;
}
}

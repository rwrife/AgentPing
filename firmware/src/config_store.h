#pragma once

#include "protocol_core.h"

#include <cstdint>
#include <string>

namespace agentping {

struct DeviceConfig {
  std::string ssid;
  std::string wifi_password;
  std::string wss_url;
  std::string device_id;
  std::string device_token;
  std::string server_certificate_pem;
  std::uint64_t configuration_generation = 0;
  std::uint64_t resume_sequence = 0;
  ViewModel resume_view;

  bool complete() const;
};

bool load_config(DeviceConfig& output);
bool save_resume_state(std::uint64_t configuration_generation,
                       std::uint64_t sequence, const ViewModel& view);
bool save_time_checkpoint(std::uint64_t configuration_generation);
bool erase_config();
void provisioning_console();

}  // namespace agentping

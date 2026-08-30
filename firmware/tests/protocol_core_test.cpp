#include "protocol_core.h"

#include <cassert>
#include <cstdint>
#include <iostream>
#include <string>
#include <utility>

using agentping::Envelope;
using agentping::FrameAssemblyStatus;
using agentping::ParseError;
using agentping::ProtocolState;
using agentping::TextFrameAssembler;
using agentping::UiState;

namespace {

std::string message(const char* type, unsigned sequence, const std::string& payload,
                    const std::string& extra = {}, const char* connection = "bridge-1") {
  return std::string("{\"protocolVersion\":\"1.0\","
                     "\"messageId\":\"123e4567-e89b-12d3-a456-426614174000\","
                     "\"type\":\"")
      + type + "\",\"sentAt\":\"2026-08-26T00:00:00Z\",\"connectionId\":\""
      + connection + "\",\"sequence\":" + std::to_string(sequence) + extra
      + ",\"payload\":" + payload + "}";
}

std::string capability(bool reset = false, unsigned count = 0, unsigned checkpoint = 0,
                       unsigned resume = 0) {
  std::string result =
      "{\"deviceId\":\"agentping-bridge\",\"role\":\"bridge\","
      "\"supportedVersions\":[\"1.0\"],"
      "\"features\":[\"events\",\"sessions\",\"attention\",\"resume\"],"
      "\"maxMessageBytes\":16384,\"resumeFromSequence\":" + std::to_string(resume)
      + ",\"resetState\":";
  result += reset ? "true" : "false";
  if (reset) {
    result += ",\"snapshotItemCount\":" + std::to_string(count)
        + ",\"snapshotCheckpoint\":" + std::to_string(checkpoint);
  }
  result += ",\"softwareVersion\":\"1.0.0\"}";
  return result;
}

std::string session(const char* state = "running", unsigned revision = 1) {
  return std::string("{\"sessionId\":\"session-1\",\"provider\":\"codex\",\"state\":\"")
      + state + "\",\"displayName\":\"Build \\u2713\","
      "\"updatedAt\":\"2026-08-26T00:00:00+00:00\",\"revision\":"
      + std::to_string(revision) + ",\"unreadCount\":0}";
}

void parser_and_reducer_happy_path() {
  Envelope envelope;
  ProtocolState state;
  assert(agentping::parse_envelope(message("capability", 1, capability()), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.last_received_sequence() == 1);
  assert(state.view().state == UiState::idle);

  assert(agentping::parse_envelope(
             message("session", 2, session(), ",\"serverSequence\":1"), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.view().state == UiState::running);
  assert(state.view().title == "Build \xe2\x9c\x93");
  assert(state.view().revision == 1);
  assert(state.resume_sequence() == 1);
  assert(state.last_received_sequence() == 2);

  const auto before = state.last_received_sequence();
  assert(state.apply(envelope) == ParseError::ordering);
  assert(state.last_received_sequence() == before);

  state.disconnected();
  assert(state.view().state == UiState::disconnected);
  assert(agentping::parse_envelope(
             message("capability", 1, capability(false, 0, 0, 1), {}, "bridge-2"), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.view().state == UiState::running);
  assert(state.view().title == "Build \xe2\x9c\x93");
}

void every_inbound_payload_shape() {
  Envelope envelope;
  assert(agentping::parse_envelope(message(
      "event", 2,
      "{\"eventId\":\"event-1\",\"sessionId\":\"session-1\",\"provider\":\"codex\","
      "\"eventKind\":\"progress\",\"summary\":\"Compiling\",\"severity\":\"info\","
      "\"metadata\":{\"phase\":\"build\"}}"), envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "event", 2,
      "{\"eventId\":\"event-1\",\"sessionId\":\"session-1\",\"provider\":\"codex\","
      "\"eventKind\":\"progress\",\"summary\":\"Compiling\",\"severity\":\"info\","
      "\"metadata\":{\"phase..name\":\"build\"}}"), envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "attention", 2,
      "{\"attentionId\":\"attention-1\",\"sessionId\":\"session-1\",\"revision\":2,"
      "\"category\":\"approval\",\"title\":\"Run tests?\",\"body\":\"Execute unit tests\","
      "\"responseDeadlineAt\":\"2026-08-26T00:00:30Z\",\"destructive\":false,"
      "\"allowedActions\":[\"approve\",\"deny\"]}"), envelope) == ParseError::none);
  assert(envelope.title == "Run tests?");
  assert(agentping::parse_envelope(message(
      "error", 2,
      "{\"code\":\"STALE_REVISION\",\"message\":\"State changed\",\"retryable\":false}"),
      envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "heartbeat", 2,
      "{\"uptimeMs\":10,\"status\":\"ready\",\"lastReceivedSequence\":1,\"queueDepth\":0}"),
      envelope) == ParseError::none);

  std::string unicode_title;
  for (unsigned index = 0; index < 120; ++index) unicode_title += "\xe2\x9c\x93";
  const auto unicode_session = [&unicode_title]() {
    return std::string("{\"sessionId\":\"session-1\",\"provider\":\"codex\","
                       "\"state\":\"running\",\"displayName\":\"")
        + unicode_title
        + "\",\"updatedAt\":\"2026-08-26T00:00:00Z\",\"revision\":1,"
          "\"unreadCount\":0}";
  };
  assert(agentping::parse_envelope(message("session", 2, unicode_session()), envelope)
         == ParseError::none);
  unicode_title += "\xe2\x9c\x93";
  assert(agentping::parse_envelope(message("session", 2, unicode_session()), envelope)
         == ParseError::schema);
}

void fail_closed_cases() {
  Envelope envelope;
  assert(agentping::parse_envelope(std::string(16385, 'x'), envelope) == ParseError::too_large);
  assert(agentping::parse_envelope("{", envelope) == ParseError::malformed_json);
  assert(agentping::parse_envelope("{\v}", envelope) == ParseError::malformed_json);
  std::string invalid_timestamp = message(
      "heartbeat", 2,
      "{\"uptimeMs\":10,\"status\":\"ready\",\"lastReceivedSequence\":1,\"queueDepth\":0}");
  invalid_timestamp.replace(invalid_timestamp.find("2026-08-26"), 10, "2026-02-30");
  assert(agentping::parse_envelope(invalid_timestamp, envelope) == ParseError::schema);
  std::string invalid_utf8 = message("session", 2, session());
  invalid_utf8[invalid_utf8.find("Build")] = static_cast<char>(0xff);
  assert(agentping::parse_envelope(invalid_utf8, envelope) == ParseError::malformed_json);
  assert(agentping::parse_envelope(std::string(20, '[') + "0" + std::string(20, ']'), envelope)
         == ParseError::malformed_json);
  assert(agentping::parse_envelope(
      "{\"protocolVersion\":\"1.0\",\"protocolVersion\":\"1.0\"}", envelope)
      == ParseError::malformed_json);
  assert(agentping::parse_envelope(message(
      "session", 2,
      "{\"sessionId\":\"s\",\"provider\":\"codex\",\"state\":\"running\","
      "\"displayName\":\"Build\",\"updatedAt\":\"2026-08-26T00:00:00Z\","
      "\"revision\":1,\"unreadCount\":0,\"secret\":\"must reject\"}"), envelope)
      == ParseError::schema);
  for (const char* key : {"token", "build.api_key.value", "a space", "Uppercase"}) {
    const std::string payload =
        std::string("{\"eventId\":\"event-1\",\"sessionId\":\"session-1\","
                    "\"provider\":\"codex\",\"eventKind\":\"progress\","
                    "\"summary\":\"Compiling\",\"severity\":\"info\",\"metadata\":{\"")
        + key + "\":\"must reject\"}}";
    assert(agentping::parse_envelope(message("event", 2, payload), envelope)
           == ParseError::schema);
  }
  assert(agentping::parse_envelope(message("approval", 2, "{}"), envelope)
         == ParseError::schema);
  assert(agentping::parse_envelope(message("session", 1, session()), envelope)
         == ParseError::none);
  ProtocolState state;
  assert(state.apply(envelope) == ParseError::negotiation);
}

void websocket_frame_assembly() {
  TextFrameAssembler assembler;
  std::string_view complete;
  const std::string wire = message("capability", 1, capability());
  const std::size_t split = 17;
  assert(assembler.push(0x1, true, wire.size(), 0,
                        std::string_view(wire).substr(0, split), complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x1, true, wire.size(), split,
                        std::string_view(wire).substr(split), complete)
         == FrameAssemblyStatus::complete);
  assert(complete == wire);

  assert(assembler.push(0x1, false, 5, 0, "hello", complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x0, false, 1, 0, " ", complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x0, true, 5, 0, "world", complete)
         == FrameAssemblyStatus::complete);
  assert(complete == "hello world");

  assert(assembler.push(0x1, false, 5, 0, "hello", complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x0, true, 0, 0, {}, complete)
         == FrameAssemblyStatus::complete);
  assert(complete == "hello");

  assert(assembler.push(0x0, true, 1, 0, "x", complete)
         == FrameAssemblyStatus::rejected);
  assert(assembler.push(0x1, true, agentping::protocol_v1::kMaxMessageBytes + 1,
                        0, {}, complete)
         == FrameAssemblyStatus::rejected);
  assert(assembler.push(0x1, false, 1, 0, "x", complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x1, true, 1, 0, "y", complete)
         == FrameAssemblyStatus::rejected);
  const std::string oversized(agentping::protocol_v1::kMaxMessageBytes, 'x');
  assert(assembler.push(0x1, false, oversized.size(), 0, oversized, complete)
         == FrameAssemblyStatus::incomplete);
  assert(assembler.push(0x0, true, 1, 0, "x", complete)
         == FrameAssemblyStatus::rejected);
}

void durable_sequence_rules() {
  Envelope envelope;
  ProtocolState state;
  assert(agentping::parse_envelope(message("capability", 1, capability()), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);

  assert(agentping::parse_envelope(message(
      "heartbeat", 2,
      "{\"uptimeMs\":1,\"status\":\"ready\",\"lastReceivedSequence\":1,\"queueDepth\":0}",
      ",\"serverSequence\":1"), envelope) == ParseError::none);
  assert(state.apply(envelope) == ParseError::ordering);
  assert(state.last_received_sequence() == 1);
  assert(state.resume_sequence() == 0);

  assert(agentping::parse_envelope(
             message("session", 2, session(), ",\"serverSequence\":2"), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::ordering);
  assert(agentping::parse_envelope(
             message("session", 2, session(), ",\"serverSequence\":1"), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.resume_sequence() == 1);

  assert(agentping::parse_envelope(message(
      "attention", 3,
      "{\"attentionId\":\"attention-1\",\"sessionId\":\"session-1\",\"revision\":2,"
      "\"category\":\"approval\",\"title\":\"Run tests?\",\"body\":\"Execute unit tests\","
      "\"responseDeadlineAt\":\"2026-08-26T00:00:30Z\",\"destructive\":false,"
      "\"allowedActions\":[\"approve\",\"deny\"]}", ",\"serverSequence\":3"), envelope)
      == ParseError::none);
  assert(state.apply(envelope) == ParseError::ordering);
  assert(state.resume_sequence() == 1);
}

void snapshot_and_resume() {
  Envelope envelope;
  ProtocolState state;
  assert(agentping::parse_envelope(message("capability", 1, capability(true, 2, 10)), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.resume_sequence() == 0);
  assert(agentping::parse_envelope(message("session", 2, session("idle", 2)), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.resume_sequence() == 0);
  assert(agentping::parse_envelope(message(
      "attention", 3,
      "{\"attentionId\":\"a\",\"sessionId\":\"session-1\",\"revision\":3,"
      "\"category\":\"notification\",\"title\":\"Done\",\"body\":\"Snapshot item\","
      "\"responseDeadlineAt\":\"2026-08-26T00:00:30Z\",\"destructive\":false,"
      "\"allowedActions\":[\"deny\"]}"), envelope) == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.resume_sequence() == 10);

  ProtocolState restored;
  restored.restore_resume_state(4, {UiState::completed, "Previous build", "Completed", 3});
  assert(agentping::parse_envelope(message("capability", 1, capability()), envelope)
         == ParseError::none);
  assert(restored.apply(envelope) == ParseError::ordering);

  restored.disconnected();
  assert(agentping::parse_envelope(
             message("capability", 1, capability(false, 0, 0, 4), {}, "bridge-3"), envelope)
         == ParseError::none);
  assert(restored.apply(envelope) == ParseError::none);
  assert(restored.view().state == UiState::completed);
  assert(restored.view().title == "Previous build");

  ProtocolState wrong_snapshot_item;
  assert(agentping::parse_envelope(message("capability", 1, capability(true, 1, 7)), envelope)
         == ParseError::none);
  assert(wrong_snapshot_item.apply(envelope) == ParseError::none);
  assert(agentping::parse_envelope(
      message("heartbeat", 2,
              "{\"uptimeMs\":1,\"status\":\"ready\",\"lastReceivedSequence\":1,\"queueDepth\":0}"),
      envelope) == ParseError::none);
  assert(wrong_snapshot_item.apply(envelope) == ParseError::ordering);
}

void snapshot_attention_uses_its_session_provider() {
  Envelope envelope;
  ProtocolState state;
  assert(agentping::parse_envelope(message("capability", 1, capability(true, 3, 10)), envelope)
         == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "session", 2,
      "{\"sessionId\":\"session-codex\",\"provider\":\"codex\",\"state\":\"running\","
      "\"displayName\":\"Codex task\",\"updatedAt\":\"2026-08-26T00:00:00Z\","
      "\"revision\":1,\"unreadCount\":0}"), envelope) == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "session", 3,
      "{\"sessionId\":\"session-manual\",\"provider\":\"manual\",\"state\":\"running\","
      "\"displayName\":\"Manual task\",\"updatedAt\":\"2026-08-26T00:00:00Z\","
      "\"revision\":1,\"unreadCount\":0}"), envelope) == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(agentping::parse_envelope(message(
      "attention", 4,
      "{\"attentionId\":\"attention-codex\",\"sessionId\":\"session-codex\",\"revision\":2,"
      "\"category\":\"approval\",\"title\":\"Run tests?\",\"body\":\"Execute unit tests\","
      "\"responseDeadlineAt\":\"2026-08-26T00:00:30Z\",\"destructive\":false,"
      "\"allowedActions\":[\"approve\",\"deny\"]}"), envelope) == ParseError::none);
  assert(state.apply(envelope) == ParseError::none);
  assert(state.view().session_id == "session-codex");
  assert(state.view().provider == "codex");
}

void action_preparation_is_fail_closed() {
  agentping::ViewModel attention;
  attention.state = UiState::waiting;
  attention.title = "Delete cache?";
  attention.detail = "Remove generated cache";
  attention.revision = 4;
  attention.attention_id = "attention-1";
  attention.presented_message_id = "123e4567-e89b-12d3-a456-426614174000";
  attention.provider = "codex";
  attention.session_id = "session-1";
  attention.response_deadline_at = "2026-08-26T00:00:30Z";
  attention.response_deadline_epoch = 2000;
  attention.destructive = true;
  attention.allow_approve = true;
  attention.allow_deny = true;
  attention.allow_cancel = true;
  attention.allow_reply = true;
  attention.allow_acknowledge = true;

  assert(agentping::action_context(attention)
         == "codex · session-1\nScope: approve, deny, cancel, acknowledge, reply\n"
            "Deadline: 2026-08-26T00:00:30Z\nDESTRUCTIVE · confirm twice");

  auto approval = agentping::prepare_action(attention, "approve", {}, false, 1900);
  assert(approval.status == agentping::ActionPreparationStatus::confirmation_required);
  approval = agentping::prepare_action(attention, "approve", {}, true, 1900);
  assert(approval.status == agentping::ActionPreparationStatus::ready);
  assert(approval.message_type == "approval");
  assert(approval.canonical_prompt == "Delete cache?\nRemove generated cache");

  const auto cancelled = agentping::prepare_action(attention, "cancel", {}, false, 1900);
  assert(cancelled.status == agentping::ActionPreparationStatus::ready);
  assert(cancelled.message_type == "denial");
  assert(cancelled.reason == "user_cancelled");
  const auto acknowledged = agentping::prepare_action(attention, "acknowledge", {}, false, 1900);
  assert(acknowledged.reason == "acknowledged");

  std::string reply(512, 'x');
  assert(agentping::prepare_action(attention, "reply", reply, false, 1900).status
         == agentping::ActionPreparationStatus::ready);
  reply.push_back('x');
  assert(agentping::prepare_action(attention, "reply", reply, false, 1900).status
         == agentping::ActionPreparationStatus::invalid);
  assert(agentping::prepare_action(attention, "approve", {}, true, 2000).status
         == agentping::ActionPreparationStatus::expired);
  attention.allow_approve = false;
  assert(agentping::prepare_action(attention, "approve", {}, true, 1900).status
         == agentping::ActionPreparationStatus::not_allowed);
}

void backoff_bounds() {
  assert(agentping::backoff_delay_ms(0, 0) == 1000);
  for (unsigned attempt = 0; attempt < 20; ++attempt) {
    for (std::uint32_t random : {0U, 1U, 12345U, 0xffffffffU}) {
      const auto delay = agentping::backoff_delay_ms(attempt, random);
      const std::uint32_t cap = attempt >= 6 ? 60000U : 1000U << attempt;
      assert(delay >= cap);
      assert(delay <= std::min<std::uint32_t>(60000U, cap + cap / 4U));
    }
  }
}

void ui_state_mapping() {
  const std::pair<const char*, UiState> cases[] = {
      {"idle", UiState::idle},
      {"running", UiState::running},
      {"waiting_for_input", UiState::waiting},
      {"completed", UiState::completed},
      {"failed", UiState::failed},
  };
  for (const auto& entry : cases) {
    Envelope envelope;
    ProtocolState state;
    assert(agentping::parse_envelope(message("capability", 1, capability()), envelope)
           == ParseError::none);
    assert(state.apply(envelope) == ParseError::none);
    assert(agentping::parse_envelope(message("session", 2, session(entry.first)), envelope)
           == ParseError::none);
    assert(state.apply(envelope) == ParseError::ordering);
    assert(agentping::parse_envelope(
               message("session", 2, session(entry.first), ",\"serverSequence\":1"), envelope)
           == ParseError::none);
    assert(state.apply(envelope) == ParseError::none);
    assert(state.view().state == entry.second);
    state.disconnected();
    assert(state.view().state == UiState::disconnected);
  }
}

void endpoint_policy() {
  assert(agentping::is_private_wss_endpoint("wss://192.168.1.10:8742/ws"));
  assert(agentping::is_private_wss_endpoint("wss://10.0.0.4/ws"));
  assert(agentping::is_private_wss_endpoint("wss://172.31.255.254:443/ws"));
  assert(!agentping::is_private_wss_endpoint("ws://192.168.1.10/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://8.8.8.8/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://010.0.0.1/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://bridge.local/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://token@192.168.1.10/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://192.168.1.10/ws?token=secret"));
  assert(!agentping::is_private_wss_endpoint("wss://192.168.1.10:0/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://192.168.1.10:99999/ws"));
  assert(!agentping::is_private_wss_endpoint("wss://192.168.1.10/admin"));
}

}  // namespace

int main() {
  parser_and_reducer_happy_path();
  every_inbound_payload_shape();
  fail_closed_cases();
  websocket_frame_assembly();
  durable_sequence_rules();
  snapshot_and_resume();
  snapshot_attention_uses_its_session_provider();
  action_preparation_is_fail_closed();
  backoff_bounds();
  ui_state_mapping();
  endpoint_policy();
  std::cout << "protocol_core: all tests passed\n";
}

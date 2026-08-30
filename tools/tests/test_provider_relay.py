import io
import json
import sys
import unittest
from contextlib import redirect_stderr, redirect_stdout
from unittest import mock

from tools.agentping_provider_relay import load_payload, main, provider_decision, validated_endpoint


class ProviderRelayTests(unittest.TestCase):
    def test_accepts_literal_ipv4_and_ipv6_loopback(self) -> None:
        self.assertEqual(
            "http://127.0.0.1:8742/api/adapters/codex",
            validated_endpoint("http://127.0.0.1:8742", "codex"),
        )
        self.assertEqual(
            "http://[::1]:8742/api/adapters/claude_code",
            validated_endpoint("http://[::1]:8742", "claude_code"),
        )

    def test_rejects_non_loopback_credentials_and_paths(self) -> None:
        for value in (
            "https://127.0.0.1:8742",
            "http://localhost:8742",
            "http://192.168.1.5:8742",
            "http://user:pass@127.0.0.1:8742",
            "http://127.0.0.1:8742/path",
        ):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validated_endpoint(value, "codex")

    def test_loads_codex_argument_or_stdin_as_compact_object(self) -> None:
        argument = load_payload('{"type": "agent-turn-complete"}')
        stdin = load_payload(None, io.BytesIO(b'{"hook_event_name":"SessionStart"}'))

        self.assertEqual({"type": "agent-turn-complete"}, json.loads(argument))
        self.assertEqual({"hook_event_name": "SessionStart"}, json.loads(stdin))

    def test_rejects_arrays_invalid_json_and_oversize_input(self) -> None:
        for payload in ("[]", "not-json", '{"x":"' + "x" * 16_384 + '"}'):
            with self.subTest(length=len(payload)), self.assertRaises(ValueError):
                load_payload(payload)
    def test_renders_documented_provider_permission_decisions(self) -> None:
        allow = {"action": "approve", "status": "recorded"}
        deny = {"action": "cancel", "status": "recorded"}

        for provider in ("claude_code", "codex"):
            with self.subTest(provider=provider):
                self.assertEqual(
                    {
                        "hookSpecificOutput": {
                            "hookEventName": "PermissionRequest",
                            "decision": {"behavior": "allow"},
                        }
                    },
                    provider_decision(provider, allow),
                )
                self.assertEqual(
                    "deny",
                    provider_decision(provider, deny)["hookSpecificOutput"]["decision"]["behavior"],
                )

        self.assertEqual(
            {"permissionDecision": "allow"},
            provider_decision("copilot_cli", allow),
        )
        self.assertEqual(
            "deny",
            provider_decision("copilot_cli", deny)["permissionDecision"],
        )

    def test_manual_reply_is_explicit_and_reply_text_is_not_emitted_to_permission_hooks(self) -> None:
        outcome = {"action": "reply", "status": "recorded", "text": "Run focused tests"}

        self.assertEqual(
            {"action": "reply", "status": "recorded", "text": "Run focused tests"},
            provider_decision("manual", outcome),
        )
        with self.assertRaises(ValueError):
            provider_decision("claude_code", outcome)

    def test_copilot_wait_failure_emits_explicit_deny_before_provider_timeout(self) -> None:
        stdout = io.StringIO()
        stderr = io.StringIO()
        argv = [
            "agentping_provider_relay.py",
            "--provider",
            "copilot_cli",
            "--wait-for-action",
            "{}",
        ]
        with (
            mock.patch.object(sys, "argv", argv),
            mock.patch("urllib.request.urlopen", side_effect=TimeoutError("timed out")),
            redirect_stdout(stdout),
            redirect_stderr(stderr),
        ):
            result = main()

        self.assertEqual(0, result)
        self.assertEqual(
            {
                "permissionDecision": "deny",
                "permissionDecisionReason": "Denied by AgentPing device.",
            },
            json.loads(stdout.getvalue()),
        )
        self.assertIn("payload was not logged", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()

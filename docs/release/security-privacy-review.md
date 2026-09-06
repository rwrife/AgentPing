# AgentPing MVP security and privacy review

## Threat model summary

AgentPing is a desk-side approval surface where provider credentials stay on the PC bridge and are never placed on the ESP32 device. The protocol is authenticated, bounded, and fail-closed.

## Security controls in scope

| Area | Implemented control | Evidence source |
|---|---|---|
| Network exposure | Bridge defaults to loopback (`127.0.0.1:8742`) and requires explicit private-interface TLS pairing for LAN use | `bridge/README.md`, `SECURITY.md` |
| Device auth | Per-device bearer token with revocation and rotation; bounded enrollment attempts/window | `bridge/README.md`, companion pairing tests |
| Protocol integrity | Draft 2020-12 schema validation with fail-closed fixtures and message bounds | `protocol/validate.py`, fixture tests |
| Action safety | Approve/deny/reply/cancel/acknowledge are revision/deadline/prompt-bound, idempotent, and fail-closed | bridge integration tests, firmware host tests |
| Secret minimization | Provider credentials remain PC-side; logs/fixtures redact secrets; firmware avoids secret-bearing logs | `SECURITY.md`, `firmware/README.md`, adapter tests |
| Persistence safety | Atomic write-through persistence with rollback on failure and stale-session handling | bridge persistence tests |

## Privacy posture

- The device shows operational state but should not be treated as a trusted secret store.
- Development firmware stores enrollment material in NVS without production flash-encryption guarantees.
- Reply text is bounded and intentionally omitted from logs.
- Metadata keys resembling secret-bearing tokens are schema-rejected.

## Residual risks and required operator controls

1. **LAN TLS setup is operator-owned**: certificate provisioning and private-interface binding are not auto-generated.
2. **Unsigned artifacts by default**: companion MSI/app binaries are unsigned until release operators perform signing.
3. **Physical validation gap**: CI cannot validate RF/Wi-Fi quality, touch usability, or on-device side-channel behavior.
4. **Provider fail-open externality**: third-party hook mechanisms may fail-open outside AgentPing's process boundary; AgentPing still returns explicit deny for expected adapter failures.

## Release gate assertions

Before tagging MVP releases:
- all canonical CI checks and simulator smoke checks are green,
- release bundle checksums are generated and archived,
- this security/privacy review has no unresolved critical exceptions,
- physical evidence (if claimed) is attached separately and explicitly labeled as physical validation.

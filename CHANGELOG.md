# Changelog

All notable changes to AgentPing are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project tracks semantic version tags (`v*`).

## [Unreleased]

### Added
- End-to-end simulator coverage for provider-event ingestion, actionable attention fanout, reconnect replay, and device action fail-closed behavior (`BridgeEndToEndSimulationTests`).
- MVP release workflow scaffolding that gates publication on canonical CI checks and assembles bridge, companion, firmware, hardware, and protocol artifacts.
- Generated protocol reference (`docs/generated/protocol-v1-reference.md`) driven directly from the v1 schema.
- Release operations docs covering reproducible demo/bring-up commands, rollback/recovery, security/privacy review, and dependency/license inventory generation.

### Changed
- Canonical verification now runs the dedicated end-to-end simulation smoke target.

## [0.0.0-bootstrap] - 2026-08-26

### Added
- Buildable baseline for bridge, firmware scaffolding, protocol contract, and repository verification conventions.

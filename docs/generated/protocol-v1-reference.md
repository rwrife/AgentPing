# AgentPing protocol v1 reference (generated)

This file is generated from `protocol/v1/agentping.schema.json` by `python3 protocol/generate_reference.py`. Do not edit by hand.

- Protocol version: `1.0`
- Schema: Draft 2020-12

## Wire limits

| Limit | Value |
|---|---|
| `maxMessageBytes` | `16384` |
| `maxReplyCharacters` | `512` |
| `maxClockSkewSeconds` | `300` |
| `maxReplayWindowMessages` | `256` |
| `heartbeatIntervalSeconds` | `15` |
| `approvalTimeoutSeconds` | `30` |

## Envelope

| Field | Required | Rules |
|---|---|---|
| `connectionId` | yes | type `string`; pattern `^[A-Za-z0-9][A-Za-z0-9._:-]*$`; length `1..64` |
| `messageId` | yes | ref `#/$defs/uuid` |
| `payload` | yes | type `object` |
| `protocolVersion` | yes | const `1.0` |
| `sentAt` | yes | type `string`; format `date-time` |
| `sequence` | yes | type `integer`; range `0..9007199254740991` |
| `serverSequence` | no | type `integer`; range `0..9007199254740991`; Bridge-only durable state checkpoint. Devices acknowledge the highest fully applied value in capability.payload.resumeFromSequence when reconnecting. |
| `type` | yes | enum: `event`, `session`, `attention`, `approval`, `denial`, `reply`, `heartbeat`, `error`, `capability` |

## Message payloads

Message kinds:
- `event`
- `session`
- `attention`
- `approval`
- `denial`
- `reply`
- `heartbeat`
- `error`
- `capability`

### `event` payload

Schema definition: `#/$defs/eventPayload`

| Field | Required | Rules |
|---|---|---|
| `detail` | no | type `string`; length `-..2048` |
| `eventId` | yes | ref `#/$defs/identifier` |
| `eventKind` | yes | enum: `started`, `progress`, `completed`, `failed`, `message` |
| `metadata` | no | type `object` |
| `provider` | yes | ref `#/$defs/provider` |
| `sessionId` | yes | ref `#/$defs/identifier` |
| `severity` | yes | enum: `info`, `success`, `warning`, `error` |
| `summary` | yes | type `string`; length `1..512` |

### `session` payload

Schema definition: `#/$defs/sessionPayload`

| Field | Required | Rules |
|---|---|---|
| `displayName` | yes | type `string`; length `1..120` |
| `provider` | yes | ref `#/$defs/provider` |
| `revision` | yes | type `integer`; range `0..9007199254740991` |
| `sessionId` | yes | ref `#/$defs/identifier` |
| `state` | yes | enum: `idle`, `running`, `waiting_for_input`, `completed`, `failed` |
| `unreadCount` | yes | type `integer`; range `0..999` |
| `updatedAt` | yes | type `string`; format `date-time` |

### `attention` payload

Schema definition: `#/$defs/attentionPayload`

| Field | Required | Rules |
|---|---|---|
| `allowedActions` | yes | type `array`; items `1..5`; unique items |
| `attentionId` | yes | ref `#/$defs/identifier` |
| `body` | yes | type `string`; length `1..1024` |
| `category` | yes | enum: `approval`, `reply`, `notification` |
| `destructive` | yes | type `boolean` |
| `responseDeadlineAt` | yes | type `string`; format `date-time` |
| `revision` | yes | type `integer`; range `0..9007199254740991` |
| `sessionId` | yes | ref `#/$defs/identifier` |
| `title` | yes | type `string`; length `1..120` |

### `approval` payload

Schema definition: `#/$defs/approvalPayload`

| Field | Required | Rules |
|---|---|---|
| `actionId` | yes | ref `#/$defs/uuid` |
| `attentionId` | yes | ref `#/$defs/identifier` |
| `confirmation` | no | ref `#/$defs/confirmation` |
| `destructive` | yes | type `boolean` |
| `expectedRevision` | yes | type `integer`; range `0..9007199254740991` |

Conditional rules:
- if `{"properties":{"destructive":{"const":true}},"required":["destructive"]}` then `{"required":["confirmation"]}`

### `denial` payload

Schema definition: `#/$defs/denialPayload`

| Field | Required | Rules |
|---|---|---|
| `actionId` | yes | ref `#/$defs/uuid` |
| `attentionId` | yes | ref `#/$defs/identifier` |
| `expectedRevision` | yes | type `integer`; range `0..9007199254740991` |
| `note` | no | type `string`; length `-..256` |
| `reason` | yes | enum: `user_denied`, `user_cancelled`, `acknowledged`, `expired`, `stale`, `policy_blocked` |

### `reply` payload

Schema definition: `#/$defs/replyPayload`

| Field | Required | Rules |
|---|---|---|
| `actionId` | yes | ref `#/$defs/uuid` |
| `attentionId` | yes | ref `#/$defs/identifier` |
| `expectedRevision` | yes | type `integer`; range `0..9007199254740991` |
| `text` | yes | type `string`; length `1..512` |

### `heartbeat` payload

Schema definition: `#/$defs/heartbeatPayload`

| Field | Required | Rules |
|---|---|---|
| `lastReceivedSequence` | yes | type `integer`; range `0..9007199254740991` |
| `queueDepth` | yes | type `integer`; range `0..256` |
| `status` | yes | enum: `ready`, `busy`, `degraded` |
| `uptimeMs` | yes | type `integer`; range `0..9007199254740991` |

### `error` payload

Schema definition: `#/$defs/errorPayload`

| Field | Required | Rules |
|---|---|---|
| `code` | yes | type `string`; pattern `^[A-Z][A-Z0-9_]{1,63}$` |
| `message` | yes | type `string`; length `1..256` |
| `relatedMessageId` | no | ref `#/$defs/uuid` |
| `retryable` | yes | type `boolean` |

### `capability` payload

Schema definition: `#/$defs/capabilityPayload`

| Field | Required | Rules |
|---|---|---|
| `deviceId` | yes | ref `#/$defs/identifier` |
| `features` | yes | type `array`; items `-..16`; unique items |
| `maxMessageBytes` | yes | const `16384` |
| `resetState` | no | type `boolean`; Bridge response only. Clear prior state before the following snapshot. |
| `resumeFromSequence` | yes | type `integer`; range `0..9007199254740991` |
| `role` | yes | enum: `bridge`, `display` |
| `snapshotCheckpoint` | no | type `integer`; range `0..9007199254740991`; Bridge response only. Persist after all snapshot items, immediately if the count is zero. |
| `snapshotItemCount` | no | type `integer`; range `0..-`; Bridge response only. Number of snapshot items following this frame. |
| `softwareVersion` | no | type `string`; length `1..64` |
| `supportedVersions` | yes | type `array`; items `1..8`; unique items |

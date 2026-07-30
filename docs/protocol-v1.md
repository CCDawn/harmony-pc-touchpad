# Protocol v1

Status: **development contract**

Wire version: **1.0**

Protocol v1 transports semantic pointer, button, scroll, and gesture events
from a paired HarmonyOS device to one Windows agent on a private local network.
It does not transport screen content, arbitrary keyboard input, files, or
remote-shell commands.

## Transport and authentication

- The transport is WSS. One JSON text message carries one control message; one
  WebSocket binary message carries one input frame.
- The HarmonyOS client trusts the agent's paired certificate through
  request-level CA configuration. Certificate verification must never be
  skipped.
- Pairing authorization uses a one-time token during the WebSocket upgrade.
  Later `/input` upgrades use an HMAC proof derived from the persistent
  per-device secret. Tokens and secrets are never placed in the URL, mDNS
  record, or log output.
- mDNS service records only advertise the protocol major version, agent ID,
  display name, port, and whether pairing is currently allowed.
- Only one authenticated device can own the control lease.

The exact QR, pairing, certificate pinning, and HMAC upgrade contract is frozen
in [Security contract v1](security-v1.md). It is not part of the binary input
frame format.

## Versioning

- An unsupported major version is rejected before payload processing.
- A higher minor version may connect only when both peers can negotiate a
  common capability set.
- Unknown input frame types, flags, enum values, non-zero reserved bytes, or
  non-finite floating-point values are protocol violations.

## Control messages

Control messages use this envelope:

```json
{
  "protocol": { "major": 1, "minor": 0 },
  "kind": "HELLO",
  "messageId": "msg-hello-001",
  "sessionId": null,
  "sentAtUs": "123456789",
  "payload": {}
}
```

`sentAtUs` is a decimal string containing a sender-local monotonic timestamp.
It is used for ordering and diagnostics, not for cross-device wall-clock
comparison.

| Kind | Direction | Purpose |
|---|---|---|
| `HELLO` | Client → agent | Device identity and capabilities |
| `HELLO_ACK` | Agent → client | Session, timers, rate, negotiated capabilities |
| `PAIRING_ACCEPTED` | Agent → client | Deliver one persistent device secret over the certificate-pinned pairing socket |
| `CONTROL_REQUEST` | Client → agent | Request the single control lease |
| `CONTROL_GRANTED` | Agent → client | Confirm the active controller |
| `CONTROL_DENIED` | Agent → client | Reject a lease request with a reason |
| `PING` / `PONG` | Both | Liveness without an input side effect |
| `ERROR` | Both | Bounded protocol/session error information |

No control-message kind accepts keyboard keys or executable commands.
`PAIRING_ACCEPTED` is never written to logs and its socket closes immediately
after delivery; the client reconnects to `/input` using HMAC authentication.

## Binary input frame

All integers and IEEE-754 `float32` values use little-endian byte order.

| Offset | Size | Field |
|---:|---:|---|
| 0 | 1 | Major version |
| 1 | 1 | Frame type |
| 2 | 2 | Flags |
| 4 | 4 | Per-session sequence |
| 8 | 8 | Sender monotonic timestamp in microseconds |
| 16 | variable | Type-specific payload |

### Flags

| Bit | Name | Meaning |
|---:|---|---|
| `0x0001` | `COALESCIBLE` | A newer event of the same stream may replace it |
| `0x0002` | `FINAL` | Gesture/input state boundary; never discard |

### Frame types

| Value | Name | Payload size |
|---:|---|---:|
| 1 | `POINTER_DELTA` | 12 bytes |
| 2 | `BUTTON` | 4 bytes |
| 3 | `SCROLL` | 12 bytes |
| 4 | `GESTURE` | 12 bytes |
| 5 | `RELEASE_ALL` | 0 bytes |

#### `POINTER_DELTA`

```text
dx:f32, dy:f32, velocity:f32
```

`dx` and `dy` are the displacement since the previous transmitted sample in
ArkUI virtual pixels (`vp`). Positive `dx` points right and positive `dy`
points down. `velocity` is the non-negative displacement magnitude in `vp/s`.
The Windows agent applies the configured sensitivity and acceleration curve;
the client does not transmit desktop pixels.

Every `POINTER_DELTA` frame carries exactly the `COALESCIBLE` flag.

#### `BUTTON`

```text
button:u8, action:u8, reserved:u16=0
```

Buttons are `LEFT=1`, `RIGHT=2`, and `MIDDLE=3`. Actions are `DOWN=1` and
`UP=2`. Clicks and double-clicks are expressed as bounded down/up sequences.

#### `SCROLL`

```text
dx:f32, dy:f32, phase:u8, reserved:u24=0
```

Phases are `BEGIN=1`, `UPDATE=2`, `END=3`, and `CANCEL=4`.

`dx` and `dy` are two-finger centroid displacement since the previous sample
in `vp`, with positive values pointing right and down. The Windows agent owns
natural-scroll inversion, accumulation, and conversion to wheel units.

`BEGIN` carries no flags, `UPDATE` carries exactly `COALESCIBLE`, and `END` or
`CANCEL` carries exactly `FINAL`.

#### `GESTURE`

```text
gesture:u8, phase:u8, direction:u8, reserved:u8=0,
value1:f32, value2:f32
```

Gestures are `PINCH=1`, `ROTATE=2`, `THREE_FINGER_SWIPE=3`, and
`FOUR_FINGER_SWIPE=4`. Directions are `NONE=0`, `UP=1`, `DOWN=2`, `LEFT=3`,
and `RIGHT=4`.

| Gesture | Direction | Phases and flags | `value1` | `value2` |
|---|---|---|---|---|
| `PINCH` | `NONE` | `BEGIN`: none; `UPDATE`: `COALESCIBLE`; `END`/`CANCEL`: `FINAL` | Incremental scale ratio; `1.0` means no change and the value must be positive | Signed scale velocity in ratio units per second; positive means spreading |
| `ROTATE` | `NONE` | `BEGIN`: none; `UPDATE`: `COALESCIBLE`; `END`/`CANCEL`: `FINAL` | Incremental rotation in radians; clockwise is positive in screen coordinates | Signed angular velocity in radians per second |
| `THREE_FINGER_SWIPE` | Required cardinal direction | `END` with exactly `FINAL` | Non-negative total dominant-axis distance in `vp` | Non-negative final speed in `vp/s` |
| `FOUR_FINGER_SWIPE` | Required cardinal direction | `END` with exactly `FINAL` | Non-negative total dominant-axis distance in `vp` | Non-negative final speed in `vp/s` |

#### `RELEASE_ALL`

The frame has no payload. The agent releases every held button and clears all
in-progress gesture state. It is idempotent and carries exactly the `FINAL`
flag.

## Flow control and failure safety

- The negotiated input rate cannot exceed 120 frames per second.
- The default heartbeat interval is 500ms and the default idle release timeout
  is 1000ms.
- Pointer, scroll-update, pinch-update, and rotate-update frames may be
  coalesced before transmission.
- Button transitions, gesture boundaries, and `RELEASE_ALL` cannot be
  coalesced.
- Sequence starts at zero for every authenticated session and increments once
  per transmitted binary frame. Coalescing happens before sequence assignment.
  A duplicate, skipped, or out-of-order sequence is a protocol violation.
- The receiver may discard stale coalescible events but must process later
  final boundaries.
- A timeout, disconnect, authentication failure, protocol violation, app
  background event, or agent shutdown invokes `RELEASE_ALL`.

## Conformance assets

- [`input-frames.json`](../protocol/v1/test-vectors/input-frames.json) contains
  exact hexadecimal wire frames.
- [`control-messages.json`](../protocol/v1/test-vectors/control-messages.json)
  contains valid and invalid control-plane examples.
- [`codec.mjs`](../protocol/reference/codec.mjs) and
  [`control.mjs`](../protocol/reference/control.mjs) are dependency-free
  reference validators, not production runtime implementations.

# Architecture and trust boundaries

## MVP components

### HarmonyOS application

- Captures raw multi-contact touch events.
- Resolves gesture conflicts in a deterministic state machine.
- Coalesces high-frequency pointer and scroll updates.
- Discovers the Windows agent through mDNS.
- Pins the agent certificate and stores the paired-device secret in HUKS.
- Stops transmitting and clears local gesture state when backgrounded.

### Windows agent

- Runs as a non-elevated .NET 10 tray application with no console window.
- Advertises a private-network service through mDNS.
- Hosts the authenticated WSS endpoint.
- Allows one active controller at a time.
- Validates and rate-limits every message before dispatch.
- Maps semantic gestures to an allowlisted action set.
- Wraps Win32 `SendInput` behind an `IInputSink` test boundary.
- Stores paired-device secrets with Windows DPAPI.

### Protocol contract

- JSON control messages for negotiation and session lifecycle.
- One little-endian binary event per WebSocket binary message.
- Shared golden vectors consumed by the reference validator, ArkTS, and C#.
- Exact v1.0 acceptance plus capability-intersection negotiation.

## Trust model

The local network is not trusted. mDNS is discovery-only and never carries a
credential. A discovered endpoint cannot inject input until certificate
validation and paired-device authentication both succeed.

The HarmonyOS application is trusted to recognize gestures, but it is not
trusted to choose arbitrary Windows key sequences. It can only emit protocol
events and gesture identifiers. The Windows action mapper owns the final
allowlist.

The Windows agent deliberately does not run as administrator. Win32 UIPI
therefore prevents it from controlling elevated applications and the UAC secure
desktop. That limitation is safer than keeping a privileged remote-input agent
resident.

## Session lifecycle

```text
DISCONNECTED
  -> DISCOVERING
  -> CONNECTING
  -> AUTHENTICATING
  -> READY
  -> CONTROLLING
  -> RECONNECTING or DISCONNECTED
```

Any transition out of `CONTROLLING` invokes `RELEASE_ALL`. A missing heartbeat,
certificate error, authentication error, protocol violation, application
background event, or process shutdown also clears all held buttons and gesture
state.

## Deferred architecture

Bluetooth HID is a separate adapter, not another transport mode inside
Protocol v1. Native Windows Precision Touchpad emulation would require a
different raw-contact contract and a virtual HID driver; neither is part of the
MVP.

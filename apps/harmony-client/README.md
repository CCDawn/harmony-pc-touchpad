# HarmonyOS client

HarmonyOS phone/tablet client for Harmony PC Touchpad.

The current milestone provides:

- a Stage model HarmonyOS application targeting API 12 or newer;
- LAN agent discovery through the official `@kit.NetworkKit` mDNS API;
- strict validation of discovery protocol v1 metadata;
- lifecycle-safe start, stop, resolve, deduplication, and service-loss handling;
- Windows pairing-QR scanning through the official Scan Kit;
- SPKI SHA-256 certificate pinning before the authenticated WebSocket upgrade;
- a WebSocket trust store limited to the exact pinned certificate; and
- HUKS-backed storage of the 32-byte device HMAC key. The secret is never
  persisted in Preferences or logs;
- authenticated `/input` WebSocket upgrades using HMAC generated inside HUKS;
- protocol-v1 `HELLO`/control negotiation, heartbeats, release-all shutdown,
  and binary input frames; and
- an immersive landscape touch surface with a guarded edge, one-finger
  pointer movement, tap-to-left-click, and double-tap-hold dragging;
- ID-stable two-finger scrolling with axis lock, adjustable speed and natural
  direction, plus two-finger tap-to-right-click; and
- two-finger pinch zoom plus three/four-finger semantic swipe actions; and
- persistent controls for drag sensitivity, scroll speed/direction, and
  click/drag haptic feedback.

## Build

Open this directory in DevEco Studio, or run Hvigor with a locally installed
HarmonyOS SDK:

```powershell
hvigorw assembleHap --mode module -p product=default -p module=entry@default -p buildMode=debug --no-daemon
```

Secure QR pairing, HUKS authentication, pointer movement, and left-click have
passed an end-to-end LAN run on HarmonyOS hardware. The landscape UI has also
been visually verified on hardware. Automatic mDNS discovery and final
two-finger gesture feel still require device acceptance.

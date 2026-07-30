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
  persisted in Preferences or logs.

## Build

Open this directory in DevEco Studio, or run Hvigor with a locally installed
HarmonyOS SDK:

```powershell
hvigorw assembleHap --mode module -p product=default -p module=entry@default -p buildMode=debug --no-daemon
```

Device acceptance is still required for end-to-end discovery, TLS trust-anchor
behavior, Scan Kit, and HUKS import on HarmonyOS hardware.

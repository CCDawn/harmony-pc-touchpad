# HarmonyOS client

HarmonyOS phone/tablet client for Harmony PC Touchpad.

The current milestone provides:

- a Stage model HarmonyOS application targeting API 12 or newer;
- LAN agent discovery through the official `@kit.NetworkKit` mDNS API;
- strict validation of discovery protocol v1 metadata;
- lifecycle-safe start, stop, resolve, deduplication, and service-loss handling;
- a small discovery screen that intentionally does not persist credentials.

## Build

Open this directory in DevEco Studio, or run Hvigor with a locally installed
HarmonyOS SDK:

```powershell
hvigorw assembleHap --mode module -p product=default -p module=entry@default -p buildMode=debug --no-daemon
```

Device or emulator acceptance is still required for end-to-end discovery on
HarmonyOS hardware.

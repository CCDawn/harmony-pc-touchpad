# Windows agent vertical slice

This directory contains the first executable boundary of the Windows agent.
It is deliberately offline: there is no WebSocket listener, pairing flow,
device-secret storage, mDNS advertisement, or startup registration yet.

## Implemented

- strict Protocol v1 binary decoding against the repository's shared vectors;
- sequence validation with fail-closed release on gaps and dispatch failures;
- idempotent release on disconnect and timeout;
- pointer, button, and two-axis wheel injection through Win32 `SendInput`;
- fractional-delta accumulation before integer Windows input commands;
- a `WinExe` tray shell, so launching the agent does not create a console
  window; and
- isolated tests that replace the native input API and never move the test
  machine's pointer.

Semantic gestures are currently rejected explicitly at the Windows sink. They
will be added through a separate allowlisted action mapper; no arbitrary
keyboard input is accepted.

## Projects

- `HarmonyPcTouchpad.Agent.Protocol`: binary models, decoding, and validation.
- `HarmonyPcTouchpad.Agent.Core`: transport-independent input-session safety.
- `HarmonyPcTouchpad.Agent.Windows`: `IInputSink` and Win32 `SendInput` adapter.
- `HarmonyPcTouchpad.Agent.App`: console-free tray-process shell.

## Local verification

```powershell
dotnet test HarmonyPcTouchpad.WindowsAgent.slnx --configuration Release

dotnet publish `
  src/HarmonyPcTouchpad.Agent.App/HarmonyPcTouchpad.Agent.App.csproj `
  --configuration Release `
  --self-contained false
```

The framework-dependent publish requires the .NET 10 Desktop Runtime on the
target PC. Packaging and signing are intentionally deferred until the
authenticated transport is present.

Do not expose a listener around `InputSession` until authentication,
single-controller ownership, heartbeat timeout, message-size/rate limits, and
private-network binding are implemented together.

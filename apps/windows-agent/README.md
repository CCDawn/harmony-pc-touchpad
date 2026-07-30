# Windows agent

This directory contains the console-free Windows tray agent and its
authenticated local-network transport. It is still a development build:
mDNS advertisement, startup registration, installer/signing, and the
HarmonyOS client are not present yet.

## Implemented

- strict Protocol v1 binary decoding against the repository's shared vectors;
- sequence validation with fail-closed release on gaps and dispatch failures;
- idempotent release on disconnect and timeout;
- pointer, button, and two-axis wheel injection through Win32 `SendInput`;
- fractional-delta accumulation before integer Windows input commands;
- a `WinExe` tray shell, so launching the agent does not create a console
  window; and
- isolated tests that replace the native input API and never move the test
  machine's pointer;
- two-minute, single-use pairing tickets that retain only a SHA-256 token hash;
- a canonical HMAC-SHA256 upgrade authenticator with clock-skew and replay
  protection; and
- per-device secret persistence encrypted with Windows DPAPI `CurrentUser`.
- a stable self-signed TLS identity whose PFX is encrypted with DPAPI
  `CurrentUser`;
- Kestrel WSS bound only to discovered RFC1918 or IPv6 ULA interface
  addresses on port `47431`;
- `/pair` and HMAC-authenticated `/input` validation before WebSocket upgrade,
  with compression disabled;
- one active controller, a 409 rejection for a busy lease, a 4096-byte
  message cap, a sliding 120-frame/s input limit, and a separate bounded
  heartbeat-control rate; and
- 500ms heartbeat negotiation plus fail-closed release after 1000ms without
  traffic.

The tray menu can copy a two-minute pairing JSON payload. Its
`<agent-id>.local` endpoint is intentionally aligned with the future mDNS
advertisement, so automatic hostname discovery/resolution is not complete
until the mDNS increment lands. Semantic gestures are still rejected
explicitly at the Windows sink and are not advertised during capability
negotiation; they will be added through a separate allowlisted action mapper.
No arbitrary keyboard input is accepted.

## Projects

- `HarmonyPcTouchpad.Agent.Protocol`: binary models, decoding, and validation.
- `HarmonyPcTouchpad.Agent.Security`: pairing, QR, certificate fingerprint,
  signing, and replay-protection contract.
- `HarmonyPcTouchpad.Agent.Transport`: Kestrel/WSS endpoints, upgrade gates,
  control handshake, rate/size limits, heartbeat timeout, and controller lease.
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

The framework-dependent publish requires the .NET 10 Desktop and ASP.NET Core
runtimes on the target PC. Packaging and signing are intentionally deferred.
Launching the development build creates or loads the DPAPI-protected identity
under `%LOCALAPPDATA%\HarmonyPcTouchpad` and opens port `47431` only when at
least one allowed private address is available. The process imports the TLS
certificate into the current user's key set because Windows SChannel cannot
serve TLS from an ephemeral private key.

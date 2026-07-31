# Windows agent

This directory contains the console-free Windows tray agent and its
authenticated local-network transport. A per-user Windows installer is
available; distribution signing and final beta hardening are not complete.

## Implemented

- strict Protocol v1 binary decoding against the repository's shared vectors;
- sequence validation with fail-closed release on gaps and dispatch failures;
- idempotent release on disconnect and timeout;
- pointer, button, two-axis wheel, allowlisted keyboard-chord, and Ctrl+wheel
  zoom injection through Win32 `SendInput`;
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
  traffic; and
- credential-free `_hptouchpad._tcp` DNS-SD advertisement through the native
  Windows DNS API on the same private interfaces as the WSS listener; and
- a tray-owned QR window with a two-minute countdown, explicit refresh, and
  copy fallback. Refreshing invalidates the previous single-use ticket; and
- single-instance activation, so opening the desktop or Start menu shortcut
  again shows the existing pairing window instead of starting a second
  listener.

The tray menu displays a scannable two-minute pairing JSON payload. Its
`<agent-id>.local` endpoint matches the advertised DNS-SD host. The Windows
sink accepts only the protocol's allowlisted semantic gestures: pinch zoom,
three-finger task/application actions, and four-finger virtual-desktop
switching. Rotation and four-finger vertical swipes remain disabled. No
arbitrary keyboard input is accepted.

## Projects

- `HarmonyPcTouchpad.Agent.Protocol`: binary models, decoding, and validation.
- `HarmonyPcTouchpad.Agent.Security`: pairing, QR, certificate fingerprint,
  signing, and replay-protection contract.
- `HarmonyPcTouchpad.Agent.Transport`: Kestrel/WSS endpoints, upgrade gates,
  control handshake, rate/size limits, heartbeat timeout, and controller lease.
- `HarmonyPcTouchpad.Agent.Core`: transport-independent input-session safety.
- `HarmonyPcTouchpad.Agent.Windows`: `IInputSink` and Win32 `SendInput` adapter.
- `HarmonyPcTouchpad.Agent.App`: console-free tray-process shell.

## Install and run

Builds created by `scripts/build-windows-installer.ps1` produce
`HarmonyPcTouchpad-Setup-<version>.exe`. The installer:

- installs for the current Windows user without requiring administrator
  privileges;
- creates Start menu and, by default, desktop shortcuts;
- keeps optional sign-in autostart disabled unless it is selected during
  setup; and
- launches the tray agent without a console window.

Double-click **Harmony PC Touchpad** on the desktop or Start menu to open the
pairing QR window. Closing that window leaves the agent running in the system
tray. Use the tray icon to reopen pairing or exit the agent.

The development installer is not code-signed, so Windows SmartScreen may show
an unknown-publisher warning. A trusted code-signing certificate is required
before public beta distribution.

## Local verification and packaging

```powershell
dotnet test HarmonyPcTouchpad.WindowsAgent.slnx --configuration Release

.\..\..\scripts\build-windows-installer.ps1
```

The packaging script requires .NET SDK 10 and Inno Setup 6. It publishes a
self-contained, single-file `win-x64` application, so target PCs do not need a
separate .NET installation. The application creates or loads the
DPAPI-protected identity under `%LOCALAPPDATA%\HarmonyPcTouchpad` and opens port
`47431` only when at least one allowed private address is available. The
process imports the TLS certificate into the current user's key set because
Windows SChannel cannot serve TLS from an ephemeral private key.

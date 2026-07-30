# Harmony PC Touchpad

[![Protocol contract](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/protocol-contract.yml/badge.svg)](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/protocol-contract.yml)
[![Windows agent](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/windows-agent.yml/badge.svg)](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/windows-agent.yml)

Turn a HarmonyOS phone into an Apple-style touchpad for Windows 10/11 PCs.

> [!IMPORTANT]
> The Windows agent is an early, offline vertical slice. Its authenticated
> network transport and the HarmonyOS application are not implemented yet.

## Product direction

The first release will use a secure local-network architecture:

```text
HarmonyOS raw touch input
  -> deterministic gesture engine
  -> versioned semantic protocol over WSS
  -> Windows tray agent
  -> allowlisted Win32 SendInput actions
```

The MVP targets HarmonyOS NEXT API 12+ and Windows 10/11 x64. Bluetooth HID,
native Windows Precision Touchpad emulation, macOS, Linux, screen mirroring,
and internet remote control are outside the MVP.

## Current deliverables

- [Protocol v1](docs/protocol-v1.md)
- [Windows gesture map v1](docs/gesture-map-v1.md)
- [Architecture and trust boundaries](docs/architecture.md)
- Machine-readable [gesture bindings](protocol/v1/gesture-map.json)
- Cross-language [binary and control-message test vectors](protocol/v1/test-vectors)
- Dependency-free Node.js reference validators used only to prove the wire contract
- C# Protocol v1 decoder, fail-closed input session, and testable Win32
  `SendInput` boundary
- Cross-runtime [Security contract v1](docs/security-v1.md), replay-safe HMAC
  authentication core, and Windows DPAPI device-secret storage
- Console-free Windows tray shell with no listener enabled

## Run the contract tests

Node.js 22 or newer is required. The project currently has no runtime
dependencies.

```bash
npm test
```

The same vectors will later be consumed by ArkTS and C# tests. A protocol
change is not complete unless all implementations continue to pass the same
golden vectors.

## Test the Windows agent

.NET SDK 10.0.302 or a compatible 10.0 feature band is required.

```powershell
dotnet test apps/windows-agent/HarmonyPcTouchpad.WindowsAgent.slnx -c Release
```

See [the Windows agent notes](apps/windows-agent/README.md) for the current
capability and safety boundaries. Running the tray shell does not open a
network port.

## Roadmap

- [x] Freeze Protocol v1, safety rules, gesture mapping, and golden vectors
- [x] Build the Windows agent protocol/session/`SendInput` vertical slice
- [x] Freeze QR pairing, certificate pinning, and HMAC authentication
- [ ] Connect the security core to WSS and the single-controller transport
- [ ] Build secure HarmonyOS pairing and one-finger control
- [ ] Implement deterministic one- to four-finger gesture recognition
- [ ] Package and validate the Windows/HarmonyOS beta
- [ ] Evaluate Bluetooth HID on HarmonyOS 26/API 23 hardware

## Security and licensing

Unpaired clients must never be able to inject input. The protocol does not
carry arbitrary keyboard shortcuts; the Windows agent maps semantic gestures
to an allowlisted action set. See [SECURITY.md](SECURITY.md) for reporting
security problems.

The project is licensed under the [MIT License](LICENSE). See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for the current reuse boundary.

Apple, HarmonyOS, Huawei, Microsoft, and Windows are trademarks of their
respective owners. This project is not affiliated with or endorsed by them.

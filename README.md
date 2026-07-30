# Harmony PC Touchpad

[![Protocol contract](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/protocol-contract.yml/badge.svg)](https://github.com/CCDawn/harmony-pc-touchpad/actions/workflows/protocol-contract.yml)

Turn a HarmonyOS phone into an Apple-style touchpad for Windows 10/11 PCs.

> [!IMPORTANT]
> This repository is in the protocol-contract stage. It does not yet contain a
> usable HarmonyOS application or Windows agent.

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

## Run the contract tests

Node.js 22 or newer is required. The project currently has no runtime
dependencies.

```bash
npm test
```

The same vectors will later be consumed by ArkTS and C# tests. A protocol
change is not complete unless all implementations continue to pass the same
golden vectors.

## Roadmap

- [x] Freeze Protocol v1, safety rules, gesture mapping, and golden vectors
- [ ] Build the Windows agent vertical slice with a fake input sink
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

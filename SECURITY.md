# Security policy

Harmony PC Touchpad accepts commands that can control a computer. Authentication,
session cleanup, message validation, and input allowlisting are therefore
security boundaries rather than optional enhancements.

## Reporting a vulnerability

Please use GitHub's **Report a vulnerability** flow in the repository Security
tab to open a private security advisory. Do not publish pairing bypasses,
credential exposure, certificate-validation failures, or arbitrary-input
injection issues in a public issue before a fix is available.

Include the affected commit, operating-system and HarmonyOS versions,
reproduction steps, and the observed security impact. No response-time or
bounty commitment is currently offered.

## Supported versions

There is no released application yet. Security fixes currently target the
default branch and the active development branch only.

## Security invariants

- Unpaired devices cannot obtain a control session.
- Server certificate validation must not be disabled.
- Pairing tokens and persistent device secrets never appear in logs or mDNS.
- The network protocol cannot carry arbitrary keyboard commands.
- Disconnect, timeout, app backgrounding, and process shutdown release all
  held input state.
- The Windows agent runs without administrator privileges and only exposes its
  listener on private networks.

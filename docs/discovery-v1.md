# Discovery contract v1

Harmony PC Touchpad uses DNS-SD over mDNS only to locate a Windows agent on
the current local network. Discovery never grants control and never replaces
certificate pinning or authenticated WebSocket upgrade.

## Service

- Service type: `_hptouchpad._tcp`
- Qualified service type: `_hptouchpad._tcp.local`
- Transport port: `47431`
- Service instance: `<agentId>._hptouchpad._tcp.local`
- Host name: `<agentId>.local`

The Windows agent registers only on interfaces that already have an allowed
RFC1918 IPv4 or IPv6 ULA address. It explicitly deregisters on normal shutdown.

## TXT records

| Key | Value |
|---|---|
| `v` | Discovery contract version; exactly `1` |
| `id` | Stable Windows agent identifier |
| `name` | User-facing PC display name |
| `pairing` | `1` only while a new pairing is currently allowed; otherwise `0` |

TXT records must not contain a pairing token, certificate fingerprint,
per-device secret, IP-derived identifier, username, or telemetry value. The
machine-readable contract is
[`protocol/v1/discovery-service.json`](../protocol/v1/discovery-service.json).

## HarmonyOS lifecycle

The HarmonyOS application discovers `_hptouchpad._tcp`, resolves each service
before connecting, deduplicates by `id`, and removes entries on `serviceLost`.
Discovery stops when its owning page or ability is no longer active. A resolved
host and port remain untrusted until the certificate fingerprint from an
explicit pairing flow has been verified.

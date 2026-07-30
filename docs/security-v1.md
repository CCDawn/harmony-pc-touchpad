# Security contract v1

Status: **development contract**

This document freezes the Harmony PC Touchpad MVP pairing and authenticated
WebSocket-upgrade format. The local network is untrusted. Discovery never
grants control.

## Certificate identity

The Windows agent owns one TLS server certificate. The QR code carries:

```text
base64url(SHA-256(DER SubjectPublicKeyInfo))
```

as `spkiSha256`. The HarmonyOS client must pin this value before opening the
pairing WebSocket. A changed fingerprint requires explicit re-pairing; it must
never be accepted silently. Certificate verification is not disabled.

## Pairing QR

The QR code contains compact UTF-8 JSON with this exact property order:

```json
{
  "v": 1,
  "agentId": "agent-001",
  "endpoint": "wss://touchpad.local:47431/pair",
  "spkiSha256": "<32-byte unpadded base64url>",
  "pairingToken": "<32-byte unpadded base64url>",
  "expiresAtUnixMs": 1775000000000
}
```

- `agentId` matches `[A-Za-z0-9._-]{1,128}`.
- `endpoint` is an absolute `wss://` URL whose path is exactly `/pair`. It has
  no user information, query, or fragment.
- The pairing token is cryptographically random, expires after two minutes,
  and is single-use. The agent retains only its SHA-256 hash.
- Issuing a new QR invalidates the previous pairing token.
- The token never appears in the network URL, mDNS, telemetry, or logs.

## Pairing exchange

After pinning the certificate, the phone opens the QR endpoint with:

```text
X-HPT-Version: 1
X-HPT-Device-Id: <stable device identifier>
X-HPT-Pairing-Token: <one-time token>
```

The agent validates these fields before accepting the WebSocket. A successful
pairing generates a new 32-byte device secret and persists it under the device
identifier. The pairing socket sends exactly one `PAIRING_ACCEPTED` control
message:

```json
{
  "protocol": { "major": 1, "minor": 0 },
  "kind": "PAIRING_ACCEPTED",
  "messageId": "msg-pairing-accepted-001",
  "sessionId": null,
  "sentAtUs": "123456801",
  "payload": {
    "deviceId": "harmony-phone-001",
    "secretVersion": 1,
    "deviceSecret": "<32-byte unpadded base64url>"
  }
}
```

The message is protected by the pinned WSS channel and must never be logged.
The agent then closes the pairing socket. The phone stores the secret in HUKS
and reconnects to `/input`.

The Windows agent encrypts persistent device secrets using DPAPI
`CurrentUser`. Revoking a device deletes its record. MVP secret rotation is an
explicit re-pair: no background rotation or overlapping secret is supported.

## Authenticated `/input` upgrade

The HarmonyOS WebSocket client sends:

```text
X-HPT-Version: 1
X-HPT-Device-Id: phone-001
X-HPT-Timestamp-Unix-Ms: 1775000000000
X-HPT-Nonce: <16-byte unpadded base64url>
X-HPT-Signature: <32-byte unpadded base64url>
```

The signature is HMAC-SHA256 over this UTF-8 canonical string with no trailing
newline:

```text
HPT1
GET
/input
<agentId>
<deviceId>
<timestampUnixMs>
<nonce>
```

The server:

1. accepts only `GET /input`;
2. accepts timestamps within 30 seconds of server UTC;
3. accepts a nonce only once per device and retains replay entries for at
   least two minutes;
4. verifies the 32-byte signature with a fixed-time comparison;
5. registers the nonce only after a valid signature, so an attacker cannot
   burn a legitimate nonce; and
6. returns a generic authentication failure without revealing whether a
   device identifier exists.

Authentication completes before `AcceptWebSocketAsync`. Compression remains
disabled. The later transport layer must also enforce one upgraded input
connection, bounded message sizes, an input-rate limit, and the idle-release
timeout.

## Cross-runtime vectors

[`security-auth.json`](../protocol/v1/test-vectors/security-auth.json) freezes
the compact QR JSON, canonical request, nonce encoding, and HMAC signature for
Node.js, C#, and the future ArkTS implementation.

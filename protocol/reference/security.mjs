import {
  createHmac,
  timingSafeEqual,
} from 'node:crypto';

const identifierPattern = /^[A-Za-z0-9._-]{1,128}$/;

function decodeExactBase64Url(value, bytes, field) {
  if (typeof value !== 'string' || !/^[A-Za-z0-9_-]+$/.test(value)) {
    throw new TypeError(`${field} must be unpadded base64url`);
  }

  const decoded = Buffer.from(value, 'base64url');
  if (decoded.length !== bytes || decoded.toString('base64url') !== value) {
    throw new RangeError(`${field} must contain exactly ${bytes} bytes`);
  }

  return decoded;
}

function requireIdentifier(value, field) {
  if (typeof value !== 'string' || !identifierPattern.test(value)) {
    throw new TypeError(`${field} is invalid`);
  }
}

export function validatePairingQr(payload) {
  if (payload === null || typeof payload !== 'object' || Array.isArray(payload)) {
    throw new TypeError('pairing QR payload must be an object');
  }
  if (payload.v !== 1) {
    throw new RangeError('pairing QR version must be 1');
  }

  requireIdentifier(payload.agentId, 'agentId');
  const endpoint = new URL(payload.endpoint);
  if (
    endpoint.protocol !== 'wss:' ||
    endpoint.pathname !== '/pair' ||
    endpoint.search !== '' ||
    endpoint.hash !== '' ||
    endpoint.username !== '' ||
    endpoint.password !== ''
  ) {
    throw new RangeError('pairing endpoint must be a clean wss:// URL ending in /pair');
  }

  decodeExactBase64Url(payload.spkiSha256, 32, 'spkiSha256');
  decodeExactBase64Url(payload.pairingToken, 32, 'pairingToken');
  if (!Number.isSafeInteger(payload.expiresAtUnixMs) || payload.expiresAtUnixMs <= 0) {
    throw new RangeError('expiresAtUnixMs must be a positive safe integer');
  }

  return payload;
}

export function canonicalizeAuthRequest(request) {
  if (request.method !== 'GET' || request.path !== '/input') {
    throw new RangeError('authenticated request must be GET /input');
  }
  requireIdentifier(request.agentId, 'agentId');
  requireIdentifier(request.deviceId, 'deviceId');
  if (!Number.isSafeInteger(request.timestampUnixMs) || request.timestampUnixMs <= 0) {
    throw new RangeError('timestampUnixMs must be a positive safe integer');
  }
  decodeExactBase64Url(request.nonce, 16, 'nonce');

  return [
    'HPT1',
    request.method,
    request.path,
    request.agentId,
    request.deviceId,
    String(request.timestampUnixMs),
    request.nonce,
  ].join('\n');
}

export function signAuthRequest(secret, request) {
  if (!Buffer.isBuffer(secret) || secret.length !== 32) {
    throw new RangeError('device secret must contain exactly 32 bytes');
  }

  return createHmac('sha256', secret)
    .update(canonicalizeAuthRequest(request), 'utf8')
    .digest('base64url');
}

export function verifyAuthRequest(secret, request, signature) {
  const supplied = decodeExactBase64Url(signature, 32, 'signature');
  const expected = Buffer.from(signAuthRequest(secret, request), 'base64url');
  return timingSafeEqual(supplied, expected);
}

import { PROTOCOL_MAJOR } from './codec.mjs';

const MAX_UINT64 = (1n << 64n) - 1n;
const identifierPattern = /^[A-Za-z0-9._-]{1,128}$/;
const supportedKinds = new Set([
  'HELLO',
  'HELLO_ACK',
  'PAIRING_ACCEPTED',
  'CONTROL_REQUEST',
  'CONTROL_GRANTED',
  'CONTROL_DENIED',
  'PING',
  'PONG',
  'ERROR',
]);

function requireRecord(value, field) {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    throw new TypeError(`${field} must be an object`);
  }
  return value;
}

function requireString(value, field, maxLength = 128) {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxLength) {
    throw new TypeError(`${field} must be a non-empty string`);
  }
  return value;
}

function requireIdentifier(value, field) {
  requireString(value, field);
  if (!identifierPattern.test(value)) {
    throw new TypeError(`${field} is invalid`);
  }
}

function requireBase64UrlBytes(value, bytes, field) {
  requireString(value, field, 64);
  if (!/^[A-Za-z0-9_-]+$/.test(value)) {
    throw new TypeError(`${field} must be unpadded base64url`);
  }
  const decoded = Buffer.from(value, 'base64url');
  if (decoded.length !== bytes || decoded.toString('base64url') !== value) {
    throw new RangeError(`${field} must contain exactly ${bytes} bytes`);
  }
}

function requireUnsigned(value, max, field) {
  if (!Number.isInteger(value) || value < 0 || value > max) {
    throw new RangeError(`${field} must be an unsigned integer <= ${max}`);
  }
  return value;
}

function requireTimestamp(value) {
  let timestamp;
  try {
    timestamp = BigInt(value);
  } catch {
    throw new TypeError('sentAtUs must be an unsigned 64-bit integer string');
  }

  if (typeof value !== 'string' || timestamp < 0n || timestamp > MAX_UINT64) {
    throw new TypeError('sentAtUs must be an unsigned 64-bit integer string');
  }
}

function requireStringArray(value, field) {
  if (!Array.isArray(value) || value.some((entry) => typeof entry !== 'string')) {
    throw new TypeError(`${field} must be an array of strings`);
  }
  if (new Set(value).size !== value.length) {
    throw new RangeError(`${field} must not contain duplicates`);
  }
}

function requireSession(message) {
  requireString(message.sessionId, 'sessionId');
}

function validatePayload(message) {
  const payload = requireRecord(message.payload, 'payload');

  switch (message.kind) {
    case 'HELLO':
      if (message.sessionId !== null) {
        throw new RangeError('HELLO sessionId must be null');
      }
      requireIdentifier(payload.deviceId, 'payload.deviceId');
      requireString(payload.deviceName, 'payload.deviceName');
      requireStringArray(payload.capabilities, 'payload.capabilities');
      break;
    case 'HELLO_ACK':
      requireSession(message);
      requireUnsigned(payload.heartbeatMs, 5000, 'payload.heartbeatMs');
      if (payload.heartbeatMs === 0) {
        throw new RangeError('payload.heartbeatMs must be greater than zero');
      }
      requireUnsigned(payload.idleReleaseMs, 10000, 'payload.idleReleaseMs');
      if (payload.idleReleaseMs < payload.heartbeatMs) {
        throw new RangeError('payload.idleReleaseMs must be >= payload.heartbeatMs');
      }
      requireUnsigned(payload.maxInputRateHz, 120, 'payload.maxInputRateHz');
      if (payload.maxInputRateHz === 0) {
        throw new RangeError('payload.maxInputRateHz must be greater than zero');
      }
      requireStringArray(payload.capabilities, 'payload.capabilities');
      break;
    case 'PAIRING_ACCEPTED':
      if (message.sessionId !== null) {
        throw new RangeError('PAIRING_ACCEPTED sessionId must be null');
      }
      requireIdentifier(payload.deviceId, 'payload.deviceId');
      requireUnsigned(payload.secretVersion, 1, 'payload.secretVersion');
      if (payload.secretVersion !== 1) {
        throw new RangeError('payload.secretVersion must be 1');
      }
      requireBase64UrlBytes(payload.deviceSecret, 32, 'payload.deviceSecret');
      break;
    case 'CONTROL_REQUEST':
      requireSession(message);
      break;
    case 'CONTROL_GRANTED':
      requireSession(message);
      requireIdentifier(payload.controllerDeviceId, 'payload.controllerDeviceId');
      break;
    case 'CONTROL_DENIED':
      requireString(payload.reason, 'payload.reason');
      break;
    case 'PING':
    case 'PONG':
      requireSession(message);
      requireString(payload.nonce, 'payload.nonce', 64);
      break;
    case 'ERROR':
      requireString(payload.code, 'payload.code', 64);
      requireString(payload.message, 'payload.message', 512);
      break;
    default:
      throw new RangeError(`Unknown control message kind: ${message.kind}`);
  }
}

export function validateControlMessage(input) {
  const message = requireRecord(input, 'message');
  const protocol = requireRecord(message.protocol, 'protocol');

  if (protocol.major !== PROTOCOL_MAJOR) {
    throw new RangeError(`Unsupported protocol major version: ${protocol.major}`);
  }
  requireUnsigned(protocol.minor, 0xffff, 'protocol.minor');

  if (!supportedKinds.has(message.kind)) {
    throw new RangeError(`Unknown control message kind: ${String(message.kind)}`);
  }

  requireString(message.messageId, 'messageId', 64);
  if (message.sessionId !== null && typeof message.sessionId !== 'string') {
    throw new TypeError('sessionId must be a string or null');
  }
  requireTimestamp(message.sentAtUs);
  validatePayload(message);

  return message;
}

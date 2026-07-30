export const PROTOCOL_MAJOR = 1;
export const HEADER_BYTES = 16;

export const FrameType = Object.freeze({
  POINTER_DELTA: 1,
  BUTTON: 2,
  SCROLL: 3,
  GESTURE: 4,
  RELEASE_ALL: 5,
});

export const FrameFlag = Object.freeze({
  COALESCIBLE: 0x0001,
  FINAL: 0x0002,
});

export const Button = Object.freeze({
  LEFT: 1,
  RIGHT: 2,
  MIDDLE: 3,
});

export const ButtonAction = Object.freeze({
  DOWN: 1,
  UP: 2,
});

export const Phase = Object.freeze({
  BEGIN: 1,
  UPDATE: 2,
  END: 3,
  CANCEL: 4,
});

export const Gesture = Object.freeze({
  PINCH: 1,
  ROTATE: 2,
  THREE_FINGER_SWIPE: 3,
  FOUR_FINGER_SWIPE: 4,
});

export const Direction = Object.freeze({
  NONE: 0,
  UP: 1,
  DOWN: 2,
  LEFT: 3,
  RIGHT: 4,
});

const MAX_UINT64 = (1n << 64n) - 1n;
const KNOWN_FLAG_MASK = Object.values(FrameFlag).reduce((mask, flag) => mask | flag, 0);

const frameTypeByCode = invert(FrameType);
const buttonByCode = invert(Button);
const buttonActionByCode = invert(ButtonAction);
const phaseByCode = invert(Phase);
const gestureByCode = invert(Gesture);
const directionByCode = invert(Direction);

const payloadBytesByType = Object.freeze({
  POINTER_DELTA: 12,
  BUTTON: 4,
  SCROLL: 12,
  GESTURE: 12,
  RELEASE_ALL: 0,
});

function invert(values) {
  return Object.fromEntries(Object.entries(values).map(([name, code]) => [code, name]));
}

function requireEnum(values, name, field) {
  const code = values[name];
  if (code === undefined) {
    throw new RangeError(`Unknown ${field}: ${String(name)}`);
  }
  return code;
}

function requireFinite(value, field) {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new TypeError(`${field} must be a finite number`);
  }
  return value;
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
    throw new TypeError('timestampUs must be an unsigned 64-bit integer');
  }

  if (timestamp < 0n || timestamp > MAX_UINT64) {
    throw new RangeError('timestampUs must be an unsigned 64-bit integer');
  }
  return timestamp;
}

function encodeFlags(names = []) {
  let flags = 0;
  for (const name of names) {
    flags |= requireEnum(FrameFlag, name, 'frame flag');
  }
  return flags;
}

function decodeFlags(flags) {
  if ((flags & ~KNOWN_FLAG_MASK) !== 0) {
    throw new RangeError(`Unknown frame flags: 0x${flags.toString(16)}`);
  }

  return Object.entries(FrameFlag)
    .filter(([, value]) => (flags & value) !== 0)
    .map(([name]) => name);
}

function assertReservedZero(buffer, offset, length) {
  for (let index = offset; index < offset + length; index += 1) {
    if (buffer[index] !== 0) {
      throw new RangeError('Reserved payload bytes must be zero');
    }
  }
}

function encodePayload(type, payload = {}) {
  const buffer = Buffer.alloc(payloadBytesByType[type]);

  switch (type) {
    case 'POINTER_DELTA':
      buffer.writeFloatLE(requireFinite(payload.dx, 'payload.dx'), 0);
      buffer.writeFloatLE(requireFinite(payload.dy, 'payload.dy'), 4);
      buffer.writeFloatLE(requireFinite(payload.velocity, 'payload.velocity'), 8);
      return buffer;
    case 'BUTTON':
      buffer.writeUInt8(requireEnum(Button, payload.button, 'button'), 0);
      buffer.writeUInt8(requireEnum(ButtonAction, payload.action, 'button action'), 1);
      return buffer;
    case 'SCROLL':
      buffer.writeFloatLE(requireFinite(payload.dx, 'payload.dx'), 0);
      buffer.writeFloatLE(requireFinite(payload.dy, 'payload.dy'), 4);
      buffer.writeUInt8(requireEnum(Phase, payload.phase, 'phase'), 8);
      return buffer;
    case 'GESTURE':
      buffer.writeUInt8(requireEnum(Gesture, payload.gesture, 'gesture'), 0);
      buffer.writeUInt8(requireEnum(Phase, payload.phase, 'phase'), 1);
      buffer.writeUInt8(requireEnum(Direction, payload.direction, 'direction'), 2);
      buffer.writeFloatLE(requireFinite(payload.value1, 'payload.value1'), 4);
      buffer.writeFloatLE(requireFinite(payload.value2, 'payload.value2'), 8);
      return buffer;
    case 'RELEASE_ALL':
      if (Object.keys(payload).length !== 0) {
        throw new RangeError('RELEASE_ALL must not carry a payload');
      }
      return buffer;
    default:
      throw new RangeError(`Unknown frame type: ${String(type)}`);
  }
}

function decodePayload(type, buffer) {
  switch (type) {
    case 'POINTER_DELTA':
      return {
        dx: requireFinite(buffer.readFloatLE(0), 'payload.dx'),
        dy: requireFinite(buffer.readFloatLE(4), 'payload.dy'),
        velocity: requireFinite(buffer.readFloatLE(8), 'payload.velocity'),
      };
    case 'BUTTON':
      assertReservedZero(buffer, 2, 2);
      return {
        button: requireDecodedEnum(buttonByCode, buffer.readUInt8(0), 'button'),
        action: requireDecodedEnum(buttonActionByCode, buffer.readUInt8(1), 'button action'),
      };
    case 'SCROLL':
      assertReservedZero(buffer, 9, 3);
      return {
        dx: requireFinite(buffer.readFloatLE(0), 'payload.dx'),
        dy: requireFinite(buffer.readFloatLE(4), 'payload.dy'),
        phase: requireDecodedEnum(phaseByCode, buffer.readUInt8(8), 'phase'),
      };
    case 'GESTURE':
      assertReservedZero(buffer, 3, 1);
      return {
        gesture: requireDecodedEnum(gestureByCode, buffer.readUInt8(0), 'gesture'),
        phase: requireDecodedEnum(phaseByCode, buffer.readUInt8(1), 'phase'),
        direction: requireDecodedEnum(directionByCode, buffer.readUInt8(2), 'direction'),
        value1: requireFinite(buffer.readFloatLE(4), 'payload.value1'),
        value2: requireFinite(buffer.readFloatLE(8), 'payload.value2'),
      };
    case 'RELEASE_ALL':
      return {};
    default:
      throw new RangeError(`Unknown frame type: ${String(type)}`);
  }
}

function requireDecodedEnum(values, code, field) {
  const name = values[code];
  if (name === undefined) {
    throw new RangeError(`Unknown ${field} code: ${code}`);
  }
  return name;
}

export function encodeFrame(frame) {
  if (frame.version !== PROTOCOL_MAJOR) {
    throw new RangeError(`Unsupported protocol major version: ${frame.version}`);
  }

  const typeCode = requireEnum(FrameType, frame.type, 'frame type');
  const flags = encodeFlags(frame.flags);
  const sequence = requireUnsigned(frame.sequence, 0xffffffff, 'sequence');
  const timestamp = requireTimestamp(frame.timestampUs);
  const payload = encodePayload(frame.type, frame.payload);
  const buffer = Buffer.alloc(HEADER_BYTES + payload.length);

  buffer.writeUInt8(PROTOCOL_MAJOR, 0);
  buffer.writeUInt8(typeCode, 1);
  buffer.writeUInt16LE(flags, 2);
  buffer.writeUInt32LE(sequence, 4);
  buffer.writeBigUInt64LE(timestamp, 8);
  payload.copy(buffer, HEADER_BYTES);

  return buffer;
}

export function decodeFrame(input) {
  const buffer = Buffer.from(input);
  if (buffer.length < HEADER_BYTES) {
    throw new RangeError(`Frame is shorter than the ${HEADER_BYTES}-byte header`);
  }

  const version = buffer.readUInt8(0);
  if (version !== PROTOCOL_MAJOR) {
    throw new RangeError(`Unsupported protocol major version: ${version}`);
  }

  const typeCode = buffer.readUInt8(1);
  const type = frameTypeByCode[typeCode];
  if (type === undefined) {
    throw new RangeError(`Unknown frame type: ${typeCode}`);
  }

  const expectedPayloadBytes = payloadBytesByType[type];
  const actualPayloadBytes = buffer.length - HEADER_BYTES;
  if (actualPayloadBytes !== expectedPayloadBytes) {
    throw new RangeError(
      `Invalid payload size for ${type}: expected ${expectedPayloadBytes}, received ${actualPayloadBytes}`,
    );
  }

  const payloadBuffer = buffer.subarray(HEADER_BYTES);
  return {
    version,
    type,
    flags: decodeFlags(buffer.readUInt16LE(2)),
    sequence: buffer.readUInt32LE(4),
    timestampUs: buffer.readBigUInt64LE(8).toString(),
    payload: decodePayload(type, payloadBuffer),
  };
}

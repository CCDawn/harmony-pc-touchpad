import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import {
  PROTOCOL_MAJOR,
  decodeFrame,
  encodeFrame,
} from '../reference/codec.mjs';

const vectorsUrl = new URL('../v1/test-vectors/input-frames.json', import.meta.url);

async function loadVectors() {
  return JSON.parse(await readFile(vectorsUrl, 'utf8'));
}

test('golden input frames encode byte-for-byte and round-trip', async () => {
  const fixture = await loadVectors();

  assert.equal(fixture.protocol.major, PROTOCOL_MAJOR);
  assert.equal(fixture.byteOrder, 'little-endian');

  for (const vector of fixture.vectors) {
    const encoded = encodeFrame(vector.frame);
    assert.equal(encoded.toString('hex'), vector.hex, vector.name);

    const decoded = decodeFrame(Buffer.from(vector.hex, 'hex'));
    assert.deepEqual(decoded, vector.frame, vector.name);
  }
});

test('unsupported major protocol versions are rejected', () => {
  const frame = Buffer.alloc(16);
  frame.writeUInt8(PROTOCOL_MAJOR + 1, 0);
  frame.writeUInt8(5, 1);

  assert.throws(
    () => decodeFrame(frame),
    /Unsupported protocol major version/,
  );
});

test('frames with the wrong payload size are rejected', () => {
  const pointerHeaderWithoutPayload = Buffer.alloc(16);
  pointerHeaderWithoutPayload.writeUInt8(PROTOCOL_MAJOR, 0);
  pointerHeaderWithoutPayload.writeUInt8(1, 1);

  assert.throws(
    () => decodeFrame(pointerHeaderWithoutPayload),
    /Invalid payload size/,
  );
});

test('unknown event types are rejected instead of becoming arbitrary input', () => {
  const frame = Buffer.alloc(16);
  frame.writeUInt8(PROTOCOL_MAJOR, 0);
  frame.writeUInt8(255, 1);

  assert.throws(
    () => decodeFrame(frame),
    /Unknown frame type/,
  );
});

test('non-finite pointer values are rejected at the network boundary', () => {
  const frame = Buffer.alloc(28);
  frame.writeUInt8(PROTOCOL_MAJOR, 0);
  frame.writeUInt8(1, 1);
  frame.writeFloatLE(Number.NaN, 16);
  frame.writeFloatLE(1, 20);
  frame.writeFloatLE(1, 24);

  assert.throws(
    () => decodeFrame(frame),
    /must be a finite number/,
  );
});

test('non-zero reserved payload bytes are rejected', () => {
  const frame = Buffer.alloc(20);
  frame.writeUInt8(PROTOCOL_MAJOR, 0);
  frame.writeUInt8(2, 1);
  frame.writeUInt8(1, 16);
  frame.writeUInt8(1, 17);
  frame.writeUInt8(1, 18);

  assert.throws(
    () => decodeFrame(frame),
    /Reserved payload bytes must be zero/,
  );
});

test('release and end boundaries cannot be marked as coalescible', () => {
  assert.throws(
    () => encodeFrame({
      version: 1,
      type: 'RELEASE_ALL',
      flags: [],
      sequence: 1,
      timestampUs: '1',
      payload: {},
    }),
    /RELEASE_ALL requires exactly the FINAL flag/,
  );

  assert.throws(
    () => encodeFrame({
      version: 1,
      type: 'SCROLL',
      flags: ['COALESCIBLE'],
      sequence: 2,
      timestampUs: '2',
      payload: {
        dx: 0,
        dy: 0,
        phase: 'END',
      },
    }),
    /SCROLL END requires exactly the FINAL flag/,
  );

  const malformedNetworkFrame = encodeFrame({
    version: 1,
    type: 'RELEASE_ALL',
    flags: ['FINAL'],
    sequence: 3,
    timestampUs: '3',
    payload: {},
  });
  malformedNetworkFrame.writeUInt16LE(0, 2);

  assert.throws(
    () => decodeFrame(malformedNetworkFrame),
    /RELEASE_ALL requires exactly the FINAL flag/,
  );
});

test('gesture direction and numeric units obey their semantic contract', () => {
  assert.throws(
    () => encodeFrame({
      version: 1,
      type: 'GESTURE',
      flags: ['COALESCIBLE'],
      sequence: 3,
      timestampUs: '3',
      payload: {
        gesture: 'PINCH',
        phase: 'UPDATE',
        direction: 'UP',
        value1: 1.1,
        value2: 0.2,
      },
    }),
    /PINCH direction must be NONE/,
  );

  assert.throws(
    () => encodeFrame({
      version: 1,
      type: 'POINTER_DELTA',
      flags: ['COALESCIBLE'],
      sequence: 4,
      timestampUs: '4',
      payload: {
        dx: 1,
        dy: 1,
        velocity: -1,
      },
    }),
    /POINTER_DELTA velocity must be non-negative/,
  );
});

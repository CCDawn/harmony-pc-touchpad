import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildAuthCanonical,
  encodeButtonFrame,
  encodeGestureFrame,
  encodePointerDeltaFrame,
  encodeReleaseAllFrame,
  encodeScrollFrame
} from '../entry/src/main/ets/input/InputContract.ts';

function toHex(buffer) {
  return Buffer.from(buffer).toString('hex');
}

test('authentication canonical string matches protocol v1', () => {
  assert.equal(
    buildAuthCanonical(
      'agent-001',
      'harmony-phone-001',
      1775000000000,
      'ABEiM0RVZneImaq7zN3u_w'
    ),
    [
      'HPT1',
      'GET',
      '/input',
      'agent-001',
      'harmony-phone-001',
      '1775000000000',
      'ABEiM0RVZneImaq7zN3u_w'
    ].join('\n')
  );
});

test('pointer delta frame matches the frozen golden vector', () => {
  assert.equal(
    toHex(encodePointerDeltaFrame(42, 123456789, 1.5, -2.25, 3.5)),
    '010101002a00000015cd5b07000000000000c03f000010c000006040'
  );
});

test('button and release-all frames match the frozen golden vectors', () => {
  assert.equal(
    toHex(encodeButtonFrame(43, 123456790, 1, true)),
    '010200002b00000016cd5b070000000001010000'
  );
  assert.equal(
    toHex(encodeButtonFrame(43, 123456790, 2, true)),
    '010200002b00000016cd5b070000000002010000'
  );
  assert.equal(
    toHex(encodeReleaseAllFrame(46, 123456793)),
    '010502002e00000019cd5b0700000000'
  );
});

test('scroll update frame matches the frozen golden vector', () => {
  assert.equal(
    toHex(encodeScrollFrame(44, 123456791, -0.5, 120, 2)),
    '010301002c00000017cd5b0700000000000000bf0000f04202000000'
  );
});

test('pinch update frame carries gesture phase and scale ratio', () => {
  const bytes = new Uint8Array(
    encodeGestureFrame(47, 123456794, 1, 2, 0, 1.05, 0.5)
  );
  const view = new DataView(bytes.buffer);
  assert.equal(bytes.length, 28);
  assert.deepEqual(Array.from(bytes.slice(0, 20)), [
    1, 4, 1, 0,
    47, 0, 0, 0,
    26, 205, 91, 7,
    0, 0, 0, 0,
    1, 2, 0, 0
  ]);
  assert.ok(Math.abs(view.getFloat32(20, true) - 1.05) < 0.000001);
  assert.ok(Math.abs(view.getFloat32(24, true) - 0.5) < 0.000001);
});

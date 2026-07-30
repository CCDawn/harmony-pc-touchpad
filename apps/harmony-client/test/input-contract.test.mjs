import assert from 'node:assert/strict';
import test from 'node:test';
import {
  buildAuthCanonical,
  encodeButtonFrame,
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

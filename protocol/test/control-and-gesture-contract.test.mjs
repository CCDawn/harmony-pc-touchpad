import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import { validateControlMessage } from '../reference/control.mjs';

const controlVectorsUrl = new URL(
  '../v1/test-vectors/control-messages.json',
  import.meta.url,
);
const gestureMapUrl = new URL('../v1/gesture-map.json', import.meta.url);

const requiredGestures = new Set([
  'ONE_FINGER_MOVE',
  'ONE_FINGER_TAP',
  'ONE_FINGER_DOUBLE_TAP',
  'ONE_FINGER_LONG_PRESS_DRAG',
  'TWO_FINGER_TAP',
  'TWO_FINGER_SCROLL_VERTICAL',
  'TWO_FINGER_SCROLL_HORIZONTAL',
  'TWO_FINGER_PINCH',
  'TWO_FINGER_ROTATE',
  'THREE_FINGER_SWIPE_UP',
  'THREE_FINGER_SWIPE_DOWN',
  'THREE_FINGER_SWIPE_LEFT',
  'THREE_FINGER_SWIPE_RIGHT',
  'FOUR_FINGER_SWIPE_UP',
  'FOUR_FINGER_SWIPE_DOWN',
  'FOUR_FINGER_SWIPE_LEFT',
  'FOUR_FINGER_SWIPE_RIGHT',
]);

const allowedWindowsActions = new Set([
  'LEFT_CLICK',
  'DOUBLE_LEFT_CLICK',
  'DRAG',
  'RIGHT_CLICK',
  'POINTER_MOVE',
  'VERTICAL_SCROLL',
  'HORIZONTAL_SCROLL',
  'ZOOM_CTRL_WHEEL',
  'TASK_VIEW',
  'SHOW_DESKTOP',
  'APP_PREVIOUS',
  'APP_NEXT',
  'DESKTOP_PREVIOUS',
  'DESKTOP_NEXT',
  'DISABLED',
]);

async function readJson(url) {
  return JSON.parse(await readFile(url, 'utf8'));
}

test('control-message golden vectors accept only the documented control plane', async () => {
  const fixture = await readJson(controlVectorsUrl);

  for (const vector of fixture.valid) {
    assert.doesNotThrow(
      () => validateControlMessage(vector.message),
      vector.name,
    );
  }

  for (const vector of fixture.invalid) {
    assert.throws(
      () => validateControlMessage(vector.message),
      new RegExp(vector.error),
      vector.name,
    );
  }
});

test('the Windows gesture map is complete, unique, and action-allowlisted', async () => {
  const gestureMap = await readJson(gestureMapUrl);
  const names = gestureMap.bindings.map((binding) => binding.gesture);

  assert.equal(gestureMap.version, 1);
  assert.equal(gestureMap.platform, 'windows');
  assert.equal(new Set(names).size, names.length, 'gesture bindings must be unique');
  assert.deepEqual(new Set(names), requiredGestures);

  for (const binding of gestureMap.bindings) {
    assert.ok(
      allowedWindowsActions.has(binding.action),
      `${binding.gesture} uses a non-allowlisted action`,
    );
    assert.equal('keys' in binding, false, `${binding.gesture} exposes raw keys`);
    assert.equal('shortcut' in binding, false, `${binding.gesture} exposes a shortcut`);
  }
});

test('unsupported Windows gestures remain explicitly disabled in v1', async () => {
  const gestureMap = await readJson(gestureMapUrl);
  const byGesture = new Map(
    gestureMap.bindings.map((binding) => [binding.gesture, binding.action]),
  );

  assert.equal(byGesture.get('TWO_FINGER_ROTATE'), 'DISABLED');
  assert.equal(byGesture.get('FOUR_FINGER_SWIPE_UP'), 'DISABLED');
  assert.equal(byGesture.get('FOUR_FINGER_SWIPE_DOWN'), 'DISABLED');
});

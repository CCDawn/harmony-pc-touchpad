import assert from 'node:assert/strict';
import test from 'node:test';
import {
  DEFAULT_TOUCHPAD_SETTINGS,
  normalizeTouchpadSettings
} from '../entry/src/main/ets/settings/TouchpadSettings.ts';

test('touchpad settings preserve usable defaults', () => {
  assert.deepEqual(
    normalizeTouchpadSettings({}),
    DEFAULT_TOUCHPAD_SETTINGS
  );
});

test('touchpad settings clamp persisted values to safe ranges', () => {
  assert.deepEqual(
    normalizeTouchpadSettings({
      dragSensitivity: 9,
      scrollSpeed: 0,
      naturalScroll: false,
      hapticEnabled: false,
      hapticStrength: 2.7
    }),
    {
      dragSensitivity: 2,
      scrollSpeed: 0.5,
      naturalScroll: false,
      hapticEnabled: false,
      hapticStrength: 3
    }
  );
});

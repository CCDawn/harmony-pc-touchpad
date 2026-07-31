import { common } from '@kit.AbilityKit';
import { preferences } from '@kit.ArkData';
import {
  DEFAULT_TOUCHPAD_SETTINGS,
  normalizeTouchpadSettings,
  TouchpadSettings
} from './TouchpadSettings';

const SETTINGS_FILE: string = 'touchpad_settings';

export class TouchpadSettingsStore {
  private readonly context: common.Context;

  constructor(context: common.Context) {
    this.context = context;
  }

  async load(): Promise<TouchpadSettings> {
    const store: preferences.Preferences =
      await preferences.getPreferences(this.context, SETTINGS_FILE);
    return normalizeTouchpadSettings({
      dragSensitivity: await store.get(
        'dragSensitivity',
        DEFAULT_TOUCHPAD_SETTINGS.dragSensitivity
      ) as number,
      scrollSpeed: await store.get(
        'scrollSpeed',
        DEFAULT_TOUCHPAD_SETTINGS.scrollSpeed
      ) as number,
      naturalScroll: await store.get(
        'naturalScroll',
        DEFAULT_TOUCHPAD_SETTINGS.naturalScroll
      ) as boolean,
      hapticEnabled: await store.get(
        'hapticEnabled',
        DEFAULT_TOUCHPAD_SETTINGS.hapticEnabled
      ) as boolean,
      hapticStrength: await store.get(
        'hapticStrength',
        DEFAULT_TOUCHPAD_SETTINGS.hapticStrength
      ) as number
    });
  }

  async save(settings: TouchpadSettings): Promise<void> {
    const normalized: TouchpadSettings =
      normalizeTouchpadSettings(settings);
    const store: preferences.Preferences =
      await preferences.getPreferences(this.context, SETTINGS_FILE);
    await store.put('dragSensitivity', normalized.dragSensitivity);
    await store.put('scrollSpeed', normalized.scrollSpeed);
    await store.put('naturalScroll', normalized.naturalScroll);
    await store.put('hapticEnabled', normalized.hapticEnabled);
    await store.put('hapticStrength', normalized.hapticStrength);
    await store.flush();
  }
}

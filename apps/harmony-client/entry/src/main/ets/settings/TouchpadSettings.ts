export interface TouchpadSettings {
  dragSensitivity: number;
  scrollSpeed: number;
  naturalScroll: boolean;
  hapticEnabled: boolean;
  hapticStrength: number;
}

export const DEFAULT_TOUCHPAD_SETTINGS: TouchpadSettings = {
  dragSensitivity: 1.35,
  scrollSpeed: 2.2,
  naturalScroll: true,
  hapticEnabled: true,
  hapticStrength: 2
};

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

export function normalizeTouchpadSettings(
  settings: Partial<TouchpadSettings>
): TouchpadSettings {
  return {
    dragSensitivity: clamp(
      settings.dragSensitivity ?? DEFAULT_TOUCHPAD_SETTINGS.dragSensitivity,
      0.75,
      2
    ),
    scrollSpeed: clamp(
      settings.scrollSpeed ?? DEFAULT_TOUCHPAD_SETTINGS.scrollSpeed,
      0.5,
      4
    ),
    naturalScroll:
      settings.naturalScroll ?? DEFAULT_TOUCHPAD_SETTINGS.naturalScroll,
    hapticEnabled:
      settings.hapticEnabled ?? DEFAULT_TOUCHPAD_SETTINGS.hapticEnabled,
    hapticStrength: Math.round(clamp(
      settings.hapticStrength ?? DEFAULT_TOUCHPAD_SETTINGS.hapticStrength,
      1,
      3
    ))
  };
}

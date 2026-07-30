# Windows gesture map v1

The phone recognizes gestures. The Windows agent maps the resulting semantic
identifier to an allowlisted Windows action. Raw virtual-key codes and arbitrary
shortcut arrays are intentionally absent from the network contract.

The machine-readable source of truth is
[`protocol/v1/gesture-map.json`](../protocol/v1/gesture-map.json).

| Gesture | Default action | Windows realization |
|---|---|---|
| One-finger move | `POINTER_MOVE` | Relative mouse movement |
| One-finger tap | `LEFT_CLICK` | Left button down/up |
| One-finger double tap | `DOUBLE_LEFT_CLICK` | Two left clicks |
| One-finger long-press drag | `DRAG` | Held left button until release/cancel |
| Two-finger tap | `RIGHT_CLICK` | Right button down/up |
| Two-finger vertical scroll | `VERTICAL_SCROLL` | Vertical wheel |
| Two-finger horizontal scroll | `HORIZONTAL_SCROLL` | Horizontal wheel |
| Two-finger pinch | `ZOOM_CTRL_WHEEL` | Allowlisted Ctrl+wheel synthesis |
| Two-finger rotate | `DISABLED` | No universal Windows action |
| Three-finger swipe up | `TASK_VIEW` | Allowlisted Task View action |
| Three-finger swipe down | `SHOW_DESKTOP` | Allowlisted Show Desktop action |
| Three-finger swipe left | `APP_PREVIOUS` | Previous application |
| Three-finger swipe right | `APP_NEXT` | Next application |
| Four-finger swipe left | `DESKTOP_PREVIOUS` | Previous virtual desktop |
| Four-finger swipe right | `DESKTOP_NEXT` | Next virtual desktop |
| Four-finger swipe up/down | `DISABLED` | Reserved for later configuration |

## Recognition rules

- One contact starts pointer movement only after the movement threshold is
  crossed; a contact released below the movement and duration thresholds is a
  tap.
- Two contacts begin in an undecided state. The recognizer locks to tap,
  scroll, pinch, or rotate after one candidate crosses its threshold.
- A locked gesture cannot change type until all contacts are released or the
  gesture is cancelled.
- Three- and four-finger swipes emit one final direction after the distance and
  velocity gate is satisfied.
- Local haptic feedback confirms click and completed swipe recognition; it is
  not transmitted as an input event.
- Three-finger drag is excluded from v1 because it conflicts with the global
  three-finger swipe family.

Threshold numbers belong to the gesture-engine implementation and its trace
fixtures. Changing thresholds does not change the wire protocol unless the
observable semantic event changes.

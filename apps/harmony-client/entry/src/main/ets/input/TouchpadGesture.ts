const pointerGain: number = 1.35;
const pointerDeadZonePx: number = 4;
const tapDistancePx: number = 6;
const tapDurationMs: number = 250;
const baseDoubleTapIntervalMs: number = 420;
const baseDoubleTapDistancePx: number = 36;
const scrollDeadZonePx: number = 3;

export type TouchpadEventType = 'down' | 'move' | 'up' | 'cancel';
export type TouchpadButton = 'left' | 'right';
export type ScrollPhase = 'begin' | 'update' | 'end' | 'cancel';

export interface TouchPoint {
  id: number;
  x: number;
  y: number;
}

export interface TouchpadGestureSettings {
  dragSensitivity: number;
  scrollSpeed: number;
  naturalScroll: boolean;
}

export interface PointerMoveAction {
  kind: 'move';
  dx: number;
  dy: number;
  velocity: number;
}

export interface PointerClickAction {
  kind: 'click';
  button: TouchpadButton;
}

export interface PointerButtonAction {
  kind: 'button';
  button: TouchpadButton;
  isDown: boolean;
}

export interface ScrollAction {
  kind: 'scroll';
  phase: ScrollPhase;
  dx: number;
  dy: number;
}

export interface ReleaseAction {
  kind: 'release';
}

export type TouchpadAction =
  PointerMoveAction |
  PointerClickAction |
  PointerButtonAction |
  ScrollAction |
  ReleaseAction;

type GestureMode = 'idle' | 'pointer' | 'scroll' | 'dragging';
type ScrollAxis = 'none' | 'horizontal' | 'vertical';

function distance(
  leftX: number,
  leftY: number,
  rightX: number,
  rightY: number
): number {
  return Math.hypot(leftX - rightX, leftY - rightY);
}

function centroid(points: Array<TouchPoint>): TouchPoint {
  return {
    id: -1,
    x: (points[0].x + points[1].x) / 2,
    y: (points[0].y + points[1].y) / 2
  };
}

export class TouchpadGesture {
  private settings: TouchpadGestureSettings = {
    dragSensitivity: 1.35,
    scrollSpeed: 2.2,
    naturalScroll: true
  };
  private mode: GestureMode = 'idle';
  private suppressUntilAllReleased: boolean = false;
  private startPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private lastPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private firstScrollPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private secondScrollPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private startTimeMs: number = 0;
  private lastTimeMs: number = 0;
  private moved: boolean = false;
  private doubleTapCandidate: boolean = false;
  private lastTapTimeMs: number = -10000;
  private lastTapX: number = 0;
  private lastTapY: number = 0;
  private scrollStarted: boolean = false;
  private scrollAxis: ScrollAxis = 'none';

  constructor(settings?: Partial<TouchpadGestureSettings>) {
    this.updateSettings(settings ?? {});
  }

  updateSettings(settings: Partial<TouchpadGestureSettings>): void {
    this.settings = {
      dragSensitivity: Math.min(
        2,
        Math.max(0.75, settings.dragSensitivity ??
          this.settings.dragSensitivity)
      ),
      scrollSpeed: Math.min(
        4,
        Math.max(0.5, settings.scrollSpeed ?? this.settings.scrollSpeed)
      ),
      naturalScroll:
        settings.naturalScroll ?? this.settings.naturalScroll
    };
  }

  private doubleTapWindowMs(): number {
    return baseDoubleTapIntervalMs * this.settings.dragSensitivity;
  }

  handleTouches(
    type: TouchpadEventType,
    touches: Array<TouchPoint>,
    changedTouches: Array<TouchPoint>,
    timeMs: number
  ): Array<TouchpadAction> {
    if (type === 'cancel') {
      return this.cancelAndSuppress(touches.length > 0);
    }

    if (this.suppressUntilAllReleased) {
      if (touches.length === 0) {
        this.suppressUntilAllReleased = false;
        this.mode = 'idle';
      }
      return [];
    }

    if (touches.length >= 3) {
      return this.cancelAndSuppress(true);
    }

    if (type === 'down' && touches.length === 1) {
      return this.beginPointer(touches[0], timeMs);
    }

    if (type === 'down' && touches.length === 2) {
      return this.beginScroll(touches, timeMs);
    }

    if (touches.length === 2 && this.mode !== 'scroll') {
      return this.beginScroll(touches, timeMs);
    }

    if (this.mode === 'scroll') {
      return this.handleScroll(type, touches, timeMs);
    }

    if (this.mode === 'pointer' || this.mode === 'dragging') {
      if (type === 'move' && touches.length === 1) {
        return this.movePointer(touches[0], timeMs);
      }
      if (type === 'up') {
        const released: TouchPoint =
          changedTouches.length > 0 ?
            changedTouches[0] :
            this.lastPoint;
        return this.endPointer(released, timeMs);
      }
    }

    return [];
  }

  private beginPointer(
    point: TouchPoint,
    timeMs: number
  ): Array<TouchpadAction> {
    const isDoubleTap: boolean = this.lastTapTimeMs > -10000 &&
      timeMs - this.lastTapTimeMs <= this.doubleTapWindowMs() &&
      distance(
        point.x,
        point.y,
        this.lastTapX,
        this.lastTapY
      ) <= baseDoubleTapDistancePx * this.settings.dragSensitivity;
    this.mode = 'pointer';
    this.startPoint = point;
    this.lastPoint = point;
    this.startTimeMs = timeMs;
    this.lastTimeMs = timeMs;
    this.moved = false;
    this.doubleTapCandidate = isDoubleTap;
    return [];
  }

  private movePointer(
    point: TouchPoint,
    timeMs: number
  ): Array<TouchpadAction> {
    const rawDx: number = point.x - this.lastPoint.x;
    const rawDy: number = point.y - this.lastPoint.y;
    const elapsedMs: number = Math.max(1, timeMs - this.lastTimeMs);
    const totalDistance: number = distance(
      point.x,
      point.y,
      this.startPoint.x,
      this.startPoint.y
    );
    this.lastPoint = point;
    this.lastTimeMs = timeMs;
    const activationDistance: number = this.doubleTapCandidate ?
      Math.max(2, pointerDeadZonePx / this.settings.dragSensitivity) :
      pointerDeadZonePx;
    if (totalDistance <= activationDistance) {
      return [];
    }

    this.moved = true;
    const actions: Array<TouchpadAction> = [];
    if (this.doubleTapCandidate && this.mode !== 'dragging') {
      this.mode = 'dragging';
      actions.push({
        kind: 'button',
        button: 'left',
        isDown: true
      });
    }
    if (rawDx !== 0 || rawDy !== 0) {
      actions.push({
        kind: 'move',
        dx: rawDx * pointerGain,
        dy: rawDy * pointerGain,
        velocity: Math.hypot(rawDx, rawDy) * 1000 / elapsedMs
      });
    }
    return actions;
  }

  private endPointer(
    point: TouchPoint,
    timeMs: number
  ): Array<TouchpadAction> {
    if (this.mode === 'dragging') {
      this.mode = 'idle';
      this.doubleTapCandidate = false;
      this.lastTapTimeMs = -10000;
      return [{
        kind: 'button',
        button: 'left',
        isDown: false
      }];
    }

    const durationMs: number = timeMs - this.startTimeMs;
    const totalDistance: number = distance(
      point.x,
      point.y,
      this.startPoint.x,
      this.startPoint.y
    );
    const wasDoubleTap: boolean = this.doubleTapCandidate;
    this.mode = 'idle';
    if (wasDoubleTap && !this.moved &&
      durationMs <= tapDurationMs &&
      totalDistance <= tapDistancePx) {
      this.doubleTapCandidate = false;
      this.lastTapTimeMs = -10000;
      return [{ kind: 'click', button: 'left' }];
    }
    if (!wasDoubleTap && !this.moved &&
      durationMs <= tapDurationMs &&
      totalDistance <= tapDistancePx) {
      this.lastTapTimeMs = timeMs;
      this.lastTapX = point.x;
      this.lastTapY = point.y;
      return [{ kind: 'click', button: 'left' }];
    }
    this.doubleTapCandidate = false;
    return [];
  }

  private beginScroll(
    points: Array<TouchPoint>,
    timeMs: number
  ): Array<TouchpadAction> {
    const actions: Array<TouchpadAction> = [];
    if (this.mode === 'dragging') {
      actions.push({
        kind: 'button',
        button: 'left',
        isDown: false
      });
    }
    this.mode = 'scroll';
    this.startTimeMs = timeMs;
    this.lastTimeMs = timeMs;
    this.firstScrollPoint = points[0];
    this.secondScrollPoint = points[1];
    this.lastPoint = centroid(points);
    this.scrollStarted = false;
    this.scrollAxis = 'none';
    this.doubleTapCandidate = false;
    this.lastTapTimeMs = -10000;
    return actions;
  }

  private handleScroll(
    type: TouchpadEventType,
    touches: Array<TouchPoint>,
    timeMs: number
  ): Array<TouchpadAction> {
    if (type === 'move' && touches.length === 2) {
      const orderedTouches: Array<TouchPoint> =
        this.orderScrollTouches(touches);
      const currentCentroid: TouchPoint = centroid(orderedTouches);
      const rawDx: number = currentCentroid.x - this.lastPoint.x;
      const rawDy: number = currentCentroid.y - this.lastPoint.y;
      const maximumTravel: number = Math.max(
        distance(
          orderedTouches[0].x,
          orderedTouches[0].y,
          this.firstScrollPoint.x,
          this.firstScrollPoint.y
        ),
        distance(
          orderedTouches[1].x,
          orderedTouches[1].y,
          this.secondScrollPoint.x,
          this.secondScrollPoint.y
        )
      );
      this.lastPoint = currentCentroid;
      this.lastTimeMs = timeMs;
      if (maximumTravel <= scrollDeadZonePx) {
        return [];
      }

      const actions: Array<TouchpadAction> = [];
      if (!this.scrollStarted) {
        this.scrollStarted = true;
        this.scrollAxis =
          Math.abs(rawDy) >= Math.abs(rawDx) ?
            'vertical' :
            'horizontal';
        actions.push({
          kind: 'scroll',
          phase: 'begin',
          dx: 0,
          dy: 0
        });
      }
      const direction: number = this.settings.naturalScroll ? 1 : -1;
      const dx: number = this.scrollAxis === 'vertical' ?
        0 :
        rawDx * this.settings.scrollSpeed * direction;
      const dy: number = this.scrollAxis === 'horizontal' ?
        0 :
        rawDy * this.settings.scrollSpeed * direction;
      actions.push({
        kind: 'scroll',
        phase: 'update',
        dx,
        dy
      });
      return actions;
    }

    if (type === 'up') {
      const actions: Array<TouchpadAction> = [];
      if (this.scrollStarted) {
        actions.push({
          kind: 'scroll',
          phase: 'end',
          dx: 0,
          dy: 0
        });
      } else if (timeMs - this.startTimeMs <= tapDurationMs) {
        actions.push({
          kind: 'click',
          button: 'right'
        });
      }
      this.mode = 'idle';
      this.suppressUntilAllReleased = touches.length > 0;
      this.scrollAxis = 'none';
      return actions;
    }
    return [];
  }

  private cancelAndSuppress(
    shouldSuppress: boolean
  ): Array<TouchpadAction> {
    this.mode = 'idle';
    this.scrollStarted = false;
    this.scrollAxis = 'none';
    this.doubleTapCandidate = false;
    this.suppressUntilAllReleased = shouldSuppress;
    return [{ kind: 'release' }];
  }

  private orderScrollTouches(
    touches: Array<TouchPoint>
  ): Array<TouchPoint> {
    const first: TouchPoint | undefined = touches.find(
      (point: TouchPoint): boolean =>
        point.id === this.firstScrollPoint.id
    );
    const second: TouchPoint | undefined = touches.find(
      (point: TouchPoint): boolean =>
        point.id === this.secondScrollPoint.id
    );
    if (first !== undefined && second !== undefined) {
      return [first, second];
    }
    return touches;
  }
}

const pointerGain: number = 1.35;
const pointerDeadZonePx: number = 4;
const dragActivationDistancePx: number = 8;
const tapDistancePx: number = 6;
const tapDurationMs: number = 250;
const baseDoubleTapIntervalMs: number = 420;
const baseDoubleTapDistancePx: number = 36;
const scrollDeadZonePx: number = 3;
const pinchDeadZoneRatio: number = 0.04;
const swipeDeadZonePx: number = 72;
const swipeAxisBias: number = 1.2;
const swipeMaxDurationMs: number = 800;

export type TouchpadEventType = 'down' | 'move' | 'up' | 'cancel';
export type TouchpadButton = 'left' | 'right';
export type GesturePhase = 'begin' | 'update' | 'end' | 'cancel';
export type ScrollPhase = GesturePhase;
export type SwipeDirection = 'up' | 'down' | 'left' | 'right';

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

export interface PinchAction {
  kind: 'pinch';
  phase: GesturePhase;
  scaleRatio: number;
  scaleVelocity: number;
}

export interface SwipeAction {
  kind: 'swipe';
  fingers: 3 | 4;
  direction: SwipeDirection;
  distance: number;
  velocity: number;
}

export interface ReleaseAction {
  kind: 'release';
}

export type TouchpadAction =
  PointerMoveAction |
  PointerClickAction |
  PointerButtonAction |
  ScrollAction |
  PinchAction |
  SwipeAction |
  ReleaseAction;

type GestureMode =
  'idle' |
  'pointer' |
  'two-finger' |
  'scroll' |
  'pinch' |
  'dragging' |
  'swipe';
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
  if (points.length === 0) {
    return { id: -1, x: 0, y: 0 };
  }
  let x: number = 0;
  let y: number = 0;
  points.forEach((point: TouchPoint): void => {
    x += point.x;
    y += point.y;
  });
  return {
    id: -1,
    x: x / points.length,
    y: y / points.length
  };
}

function pointDistance(left: TouchPoint, right: TouchPoint): number {
  return distance(left.x, left.y, right.x, right.y);
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
  private pinchStarted: boolean = false;
  private initialPinchDistance: number = 0;
  private lastPinchDistance: number = 0;
  private lastPinchTimeMs: number = 0;
  private scrollAxis: ScrollAxis = 'none';
  private swipeFingerCount: 3 | 4 = 3;
  private swipePoints: Array<TouchPoint> = [];
  private swipeStartPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private swipeLastPoint: TouchPoint = { id: -1, x: 0, y: 0 };
  private swipeStartTimeMs: number = 0;
  private swipeMaxDistance: number = 0;

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

    if (this.mode === 'swipe') {
      if (touches.length > 4) {
        return this.cancelAndSuppress(true);
      }
      return this.handleSwipe(type, touches, changedTouches, timeMs);
    }

    if (touches.length > 4) {
      return this.cancelAndSuppress(true);
    }

    if (touches.length >= 3) {
      if (type === 'down' &&
        (this.mode === 'two-finger' || this.mode === 'idle')) {
        return this.beginSwipe(touches, timeMs);
      }
      return this.cancelAndSuppress(true);
    }

    if (type === 'down' && touches.length === 1) {
      return this.beginPointer(touches[0], timeMs);
    }

    if (type === 'down' && touches.length === 2) {
      return this.beginTwoFinger(touches, timeMs);
    }

    if (touches.length === 2 &&
      this.mode !== 'two-finger' &&
      this.mode !== 'scroll' &&
      this.mode !== 'pinch') {
      return this.beginTwoFinger(touches, timeMs);
    }

    if (this.mode === 'two-finger' ||
      this.mode === 'scroll' ||
      this.mode === 'pinch') {
      return this.handleTwoFinger(type, touches, timeMs);
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
      Math.max(
        dragActivationDistancePx,
        pointerDeadZonePx / this.settings.dragSensitivity
      ) :
      pointerDeadZonePx;
    if (totalDistance <= activationDistance) {
      return [];
    }

    this.moved = true;
    const actions: Array<TouchpadAction> = [];
    if (this.doubleTapCandidate && this.mode !== 'dragging') {
      this.mode = 'dragging';
      const dragDx: number =
        (point.x - this.startPoint.x) * pointerGain;
      const dragDy: number =
        (point.y - this.startPoint.y) * pointerGain;
      if (dragDx !== 0 || dragDy !== 0) {
        actions.push({
          kind: 'move',
          dx: dragDx,
          dy: dragDy,
          velocity: Math.hypot(dragDx, dragDy) * 1000 /
            Math.max(1, timeMs - this.startTimeMs)
        });
      }
      actions.push({
        kind: 'button',
        button: 'left',
        isDown: true
      });
    } else if (rawDx !== 0 || rawDy !== 0) {
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

  private beginTwoFinger(
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
    this.mode = 'two-finger';
    this.startTimeMs = timeMs;
    this.lastTimeMs = timeMs;
    this.firstScrollPoint = points[0];
    this.secondScrollPoint = points[1];
    this.lastPoint = centroid(points);
    this.scrollStarted = false;
    this.pinchStarted = false;
    this.initialPinchDistance = pointDistance(points[0], points[1]);
    this.lastPinchDistance = this.initialPinchDistance;
    this.lastPinchTimeMs = timeMs;
    this.scrollAxis = 'none';
    this.doubleTapCandidate = false;
    this.lastTapTimeMs = -10000;
    return actions;
  }

  private handleTwoFinger(
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
      const currentDistance: number = pointDistance(
        orderedTouches[0],
        orderedTouches[1]
      );
      const elapsedMs: number = Math.max(1, timeMs - this.lastTimeMs);
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

      if (!this.scrollStarted && !this.pinchStarted) {
        const safeInitialDistance: number =
          Math.max(1, this.initialPinchDistance);
        const scaleFromStart: number =
          currentDistance / safeInitialDistance;
        if (Math.abs(scaleFromStart - 1) >= pinchDeadZoneRatio) {
          this.mode = 'pinch';
          this.pinchStarted = true;
          this.lastPinchDistance = currentDistance;
          this.lastPinchTimeMs = timeMs;
          const firstScaleRatio: number =
            currentDistance / Math.max(1, this.initialPinchDistance);
          return [
            {
              kind: 'pinch',
              phase: 'begin',
              scaleRatio: 1,
              scaleVelocity: 0
            },
            {
              kind: 'pinch',
              phase: 'update',
              scaleRatio: firstScaleRatio,
              scaleVelocity: (firstScaleRatio - 1) * 1000 / elapsedMs
            }
          ];
        }
        if (maximumTravel <= scrollDeadZonePx) {
          return [];
        }
      }

      const actions: Array<TouchpadAction> = [];
      if (this.pinchStarted || this.mode === 'pinch') {
        const scaleRatio: number =
          currentDistance / Math.max(1, this.lastPinchDistance);
        const pinchElapsedMs: number = Math.max(
          1,
          timeMs - this.lastPinchTimeMs
        );
        this.lastPinchDistance = currentDistance;
        this.lastPinchTimeMs = timeMs;
        if (scaleRatio === 1) {
          return [];
        }
        return [{
          kind: 'pinch',
          phase: 'update',
          scaleRatio: scaleRatio,
          scaleVelocity: (scaleRatio - 1) * 1000 /
            pinchElapsedMs
        }];
      }
      if (!this.scrollStarted) {
        this.scrollStarted = true;
        this.mode = 'scroll';
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
      if (this.pinchStarted || this.mode === 'pinch') {
        actions.push({
          kind: 'pinch',
          phase: 'end',
          scaleRatio: 1,
          scaleVelocity: 0
        });
      } else if (this.scrollStarted) {
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
      this.pinchStarted = false;
      this.initialPinchDistance = 0;
      this.lastPinchDistance = 0;
      this.scrollAxis = 'none';
      return actions;
    }
    return [];
  }

  private beginSwipe(
    points: Array<TouchPoint>,
    timeMs: number
  ): Array<TouchpadAction> {
    if (points.length !== 3 && points.length !== 4) {
      return this.cancelAndSuppress(true);
    }
    this.mode = 'swipe';
    this.swipeFingerCount = points.length === 4 ? 4 : 3;
    this.swipePoints = points.slice();
    this.swipeStartPoint = centroid(this.swipePoints);
    this.swipeLastPoint = this.swipeStartPoint;
    this.swipeStartTimeMs = timeMs;
    this.swipeMaxDistance = 0;
    this.suppressUntilAllReleased = false;
    return [];
  }

  private handleSwipe(
    type: TouchpadEventType,
    touches: Array<TouchPoint>,
    changedTouches: Array<TouchPoint>,
    timeMs: number
  ): Array<TouchpadAction> {
    if (type === 'down' &&
      this.swipeFingerCount === 3 &&
      touches.length === 4 &&
      this.swipeMaxDistance === 0) {
      return this.beginSwipe(touches, timeMs);
    }

    if (type === 'move' &&
      touches.length === this.swipeFingerCount) {
      this.swipePoints = touches.slice();
      this.swipeLastPoint = centroid(this.swipePoints);
      this.swipeMaxDistance = Math.max(
        this.swipeMaxDistance,
        pointDistance(this.swipeStartPoint, this.swipeLastPoint)
      );
      return [];
    }

    if (type !== 'up') {
      return [];
    }

    this.updateSwipePoints(touches, changedTouches);
    this.swipeLastPoint = centroid(this.swipePoints);
    this.swipeMaxDistance = Math.max(
      this.swipeMaxDistance,
      pointDistance(this.swipeStartPoint, this.swipeLastPoint)
    );
    const dx: number = this.swipeLastPoint.x - this.swipeStartPoint.x;
    const dy: number = this.swipeLastPoint.y - this.swipeStartPoint.y;
    const durationMs: number = Math.max(
      1,
      timeMs - this.swipeStartTimeMs
    );
    const travel: number = Math.max(Math.abs(dx), Math.abs(dy));
    const direction: SwipeDirection | undefined =
      this.swipeDirection(dx, dy);
    const fingers: 3 | 4 = this.swipeFingerCount;
    this.mode = 'idle';
    this.suppressUntilAllReleased = touches.length > 0;
    this.swipePoints = [];
    this.swipeMaxDistance = 0;
    if (direction === undefined ||
      travel < swipeDeadZonePx ||
      durationMs > swipeMaxDurationMs ||
      (fingers === 4 && (direction === 'up' || direction === 'down'))) {
      return [];
    }
    return [{
      kind: 'swipe',
      fingers: fingers,
      direction: direction,
      distance: travel,
      velocity: travel * 1000 / durationMs
    }];
  }

  private updateSwipePoints(
    touches: Array<TouchPoint>,
    changedTouches: Array<TouchPoint>
  ): void {
    const updated: Array<TouchPoint> = this.swipePoints.map(
      (point: TouchPoint): TouchPoint => {
        const replacement: TouchPoint | undefined =
          touches.find((candidate: TouchPoint): boolean =>
            candidate.id === point.id) ??
          changedTouches.find((candidate: TouchPoint): boolean =>
            candidate.id === point.id);
        return replacement ?? point;
      }
    );
    changedTouches.forEach((point: TouchPoint): void => {
      if (!updated.some((candidate: TouchPoint): boolean =>
        candidate.id === point.id)) {
        updated.push(point);
      }
    });
    this.swipePoints = updated.slice(0, this.swipeFingerCount);
  }

  private swipeDirection(
    dx: number,
    dy: number
  ): SwipeDirection | undefined {
    const absoluteX: number = Math.abs(dx);
    const absoluteY: number = Math.abs(dy);
    if (absoluteY >= absoluteX * swipeAxisBias) {
      return dy < 0 ? 'up' : 'down';
    }
    if (absoluteX >= absoluteY * swipeAxisBias) {
      return dx < 0 ? 'left' : 'right';
    }
    return undefined;
  }

  private cancelAndSuppress(
    shouldSuppress: boolean
  ): Array<TouchpadAction> {
    this.mode = 'idle';
    this.scrollStarted = false;
    this.pinchStarted = false;
    this.initialPinchDistance = 0;
    this.lastPinchDistance = 0;
    this.scrollAxis = 'none';
    this.doubleTapCandidate = false;
    this.swipePoints = [];
    this.swipeMaxDistance = 0;
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

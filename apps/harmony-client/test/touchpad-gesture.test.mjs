import assert from 'node:assert/strict';
import test from 'node:test';
import { TouchpadGesture } from '../entry/src/main/ets/input/TouchpadGesture.ts';

const point = (id, x, y) => ({ id, x, y });

test('one-finger dead zone prevents landing jitter and movement suppresses click', () => {
  const gesture = new TouchpadGesture();
  const start = point(1, 100, 100);

  assert.deepEqual(gesture.handleTouches('down', [start], [start], 1000), []);
  assert.deepEqual(
    gesture.handleTouches(
      'move',
      [point(1, 102, 102)],
      [point(1, 102, 102)],
      1010
    ),
    []
  );
  const actions = gesture.handleTouches(
    'move',
    [point(1, 110, 100)],
    [point(1, 110, 100)],
    1020
  );
  assert.equal(actions.length, 1);
  assert.equal(actions[0].kind, 'move');
  assert.equal(actions[0].dx, 10.8);
  assert.equal(actions[0].dy, -2.7);
  assert.deepEqual(
    gesture.handleTouches('up', [], [point(1, 110, 100)], 1030),
    []
  );
});

test('short stationary one-finger touch emits one left click', () => {
  const gesture = new TouchpadGesture();
  const start = point(1, 100, 200);

  gesture.handleTouches('down', [start], [start], 2000);
  assert.deepEqual(
    gesture.handleTouches('up', [], [point(1, 103, 204)], 2140),
    [{ kind: 'click', button: 'left' }]
  );
});

test('double tap then hold transitions into bounded left-button drag', () => {
  const gesture = new TouchpadGesture();
  const first = point(1, 100, 200);

  gesture.handleTouches('down', [first], [first], 3000);
  assert.deepEqual(
    gesture.handleTouches('up', [], [first], 3080),
    [{ kind: 'click', button: 'left' }]
  );

  const second = point(2, 102, 201);
  gesture.handleTouches('down', [second], [second], 3200);
  const dragActions = gesture.handleTouches(
    'move',
    [point(2, 112, 201)],
    [point(2, 112, 201)],
    3220
  );
  assert.deepEqual(
    dragActions[0],
    {
      kind: 'move',
      dx: 13.5,
      dy: 0,
      velocity: 675
    }
  );
  assert.deepEqual(
    dragActions[1],
    { kind: 'button', button: 'left', isDown: true }
  );
  assert.deepEqual(
    gesture.handleTouches('up', [], [point(2, 112, 201)], 3300),
    [{ kind: 'button', button: 'left', isDown: false }]
  );
});

test('double tap without movement emits the second click after release', () => {
  const gesture = new TouchpadGesture();
  const first = point(1, 100, 200);

  gesture.handleTouches('down', [first], [first], 3400);
  assert.deepEqual(
    gesture.handleTouches('up', [], [first], 3480),
    [{ kind: 'click', button: 'left' }]
  );
  const second = point(2, 102, 201);
  assert.deepEqual(
    gesture.handleTouches('down', [second], [second], 3600),
    []
  );
  assert.deepEqual(
    gesture.handleTouches('up', [], [second], 3680),
    [{ kind: 'click', button: 'left' }]
  );
});

test('double tap stays eligible after the immediate first click', () => {
  const gesture = new TouchpadGesture();
  const first = point(1, 100, 200);

  gesture.handleTouches('down', [first], [first], 3800);
  assert.deepEqual(
    gesture.handleTouches('up', [], [first], 3880),
    [{ kind: 'click', button: 'left' }]
  );
  const second = point(2, 102, 201);
  gesture.handleTouches('down', [second], [second], 4100);
  assert.deepEqual(
    gesture.handleTouches('up', [], [second], 4180),
    [{ kind: 'click', button: 'left' }]
  );
});

test('drag sensitivity widens the double-tap window and activation distance', () => {
  const gesture = new TouchpadGesture({
    dragSensitivity: 1.6,
    scrollSpeed: 2.2,
    naturalScroll: true
  });
  const first = point(1, 100, 200);

  gesture.handleTouches('down', [first], [first], 3000);
  gesture.handleTouches('up', [], [first], 3080);
  const second = point(2, 132, 200);
  gesture.handleTouches('down', [second], [second], 3540);
  const actions = gesture.handleTouches(
    'move',
    [point(2, 142, 200)],
    [point(2, 142, 200)],
    3560
  );

  assert.deepEqual(actions[1], {
    kind: 'button',
    button: 'left',
    isDown: true
  });
});

test('default double-tap hold accepts normal timing and spacing', () => {
  const gesture = new TouchpadGesture();
  const first = point(1, 100, 200);

  gesture.handleTouches('down', [first], [first], 5000);
  gesture.handleTouches('up', [], [first], 5080);
  const second = point(2, 130, 200);
  gesture.handleTouches('down', [second], [second], 5540);
  const actions = gesture.handleTouches(
    'move',
    [point(2, 140, 200)],
    [point(2, 140, 200)],
    5560
  );

  assert.deepEqual(actions[1], {
    kind: 'button',
    button: 'left',
    isDown: true
  });
});

test('two-finger movement tracks touch ids and applies configured speed', () => {
  const gesture = new TouchpadGesture({
    dragSensitivity: 1.25,
    scrollSpeed: 2.5,
    naturalScroll: true
  });
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);

  gesture.handleTouches('down', [one], [one], 4000);
  gesture.handleTouches('down', [one, two], [two], 4010);
  const updates = gesture.handleTouches(
    'move',
    [point(2, 200, 112), point(1, 100, 112)],
    [point(2, 200, 112), point(1, 100, 112)],
    4030
  );
  assert.deepEqual(updates, [
    { kind: 'scroll', phase: 'begin', dx: 0, dy: 0 },
    { kind: 'scroll', phase: 'update', dx: 0, dy: 30 }
  ]);
  assert.deepEqual(
    gesture.handleTouches(
      'up',
      [point(2, 200, 112)],
      [point(1, 100, 112)],
      4050
    ),
    [{ kind: 'scroll', phase: 'end', dx: 0, dy: 0 }]
  );
});

test('natural scroll can be inverted without changing gesture detection', () => {
  const gesture = new TouchpadGesture({
    dragSensitivity: 1.25,
    scrollSpeed: 2,
    naturalScroll: false
  });
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);

  gesture.handleTouches('down', [one], [one], 4500);
  gesture.handleTouches('down', [one, two], [two], 4510);
  const updates = gesture.handleTouches(
    'move',
    [point(1, 100, 110), point(2, 200, 110)],
    [point(1, 100, 110), point(2, 200, 110)],
    4530
  );
  assert.deepEqual(updates[1], {
    kind: 'scroll',
    phase: 'update',
    dx: 0,
    dy: -20
  });
});

test('short stationary two-finger touch emits one right click', () => {
  const gesture = new TouchpadGesture();
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);

  gesture.handleTouches('down', [one], [one], 5000);
  gesture.handleTouches('down', [one, two], [two], 5010);
  assert.deepEqual(
    gesture.handleTouches('up', [two], [one], 5120),
    [{ kind: 'click', button: 'right' }]
  );
  assert.deepEqual(
    gesture.handleTouches('up', [], [two], 5130),
    []
  );
});

test('two-finger movement never falls through to a right click', () => {
  const gesture = new TouchpadGesture();
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);

  gesture.handleTouches('down', [one], [one], 5500);
  gesture.handleTouches('down', [one, two], [two], 5510);
  gesture.handleTouches(
    'move',
    [point(1, 100, 120), point(2, 200, 120)],
    [point(1, 100, 120), point(2, 200, 120)],
    5540
  );
  assert.deepEqual(
    gesture.handleTouches(
      'up',
      [point(2, 200, 120)],
      [point(1, 100, 120)],
      5570
    ),
    [{ kind: 'scroll', phase: 'end', dx: 0, dy: 0 }]
  );
});

test('two-finger distance changes lock to pinch and preserve incremental scale', () => {
  const gesture = new TouchpadGesture();
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);

  gesture.handleTouches('down', [one], [one], 7000);
  gesture.handleTouches('down', [one, two], [two], 7010);
  const actions = gesture.handleTouches(
    'move',
    [point(1, 95, 100), point(2, 205, 100)],
    [point(1, 95, 100), point(2, 205, 100)],
    7030
  );
  assert.equal(actions[0].kind, 'pinch');
  assert.equal(actions[0].phase, 'begin');
  assert.equal(actions[1].kind, 'pinch');
  assert.equal(actions[1].phase, 'update');
  assert.ok(Math.abs(actions[1].scaleRatio - 1.1) < 0.000001);
  assert.deepEqual(
    gesture.handleTouches(
      'up',
      [point(2, 205, 100)],
      [point(1, 95, 100)],
      7050
    ),
    [{ kind: 'pinch', phase: 'end', scaleRatio: 1, scaleVelocity: 0 }]
  );
});

test('three-finger upward swipe emits a semantic gesture at release', () => {
  const gesture = new TouchpadGesture();
  const one = point(1, 100, 200);
  const two = point(2, 200, 200);
  const three = point(3, 300, 200);

  gesture.handleTouches('down', [one], [one], 7200);
  gesture.handleTouches('down', [one, two], [two], 7210);
  gesture.handleTouches('down', [one, two, three], [three], 7220);
  gesture.handleTouches(
    'move',
    [point(1, 100, 100), point(2, 200, 100), point(3, 300, 100)],
    [point(1, 100, 100), point(2, 200, 100), point(3, 300, 100)],
    7280
  );
  assert.deepEqual(
    gesture.handleTouches(
      'up',
      [],
      [point(1, 100, 100), point(2, 200, 100), point(3, 300, 100)],
      7300
    ),
    [{
      kind: 'swipe',
      fingers: 3,
      direction: 'up',
      distance: 100,
      velocity: 1250
    }]
  );
});

test('four-finger horizontal swipe emits a desktop gesture', () => {
  const gesture = new TouchpadGesture();
  const start = [
    point(1, 100, 200),
    point(2, 200, 200),
    point(3, 300, 200),
    point(4, 400, 200)
  ];
  const end = start.map((item) => point(item.id, item.x + 100, item.y));

  gesture.handleTouches('down', [start[0]], [start[0]], 7400);
  gesture.handleTouches('down', start.slice(0, 2), [start[1]], 7410);
  gesture.handleTouches('down', start.slice(0, 3), [start[2]], 7420);
  gesture.handleTouches('down', start, [start[3]], 7430);
  gesture.handleTouches('move', end, end, 7490);
  assert.deepEqual(
    gesture.handleTouches('up', [], end, 7510),
    [{
      kind: 'swipe',
      fingers: 4,
      direction: 'right',
      distance: 100,
      velocity: 1250
    }]
  );
});

test('third finger cancels an already active two-finger scroll', () => {
  const gesture = new TouchpadGesture();
  const one = point(1, 100, 100);
  const two = point(2, 200, 100);
  const three = point(3, 300, 100);

  gesture.handleTouches('down', [one], [one], 6000);
  gesture.handleTouches('down', [one, two], [two], 6010);
  gesture.handleTouches(
    'move',
    [point(1, 100, 120), point(2, 200, 120)],
    [point(1, 100, 120), point(2, 200, 120)],
    6020
  );
  assert.deepEqual(
    gesture.handleTouches('down', [one, two, three], [three], 6020),
    [{ kind: 'release' }]
  );
  assert.deepEqual(
    gesture.handleTouches('move', [one, two], [one, two], 6030),
    []
  );
  gesture.handleTouches('up', [], [one, two, three], 6040);
  assert.deepEqual(
    gesture.handleTouches('down', [one], [one], 6100),
    []
  );
});

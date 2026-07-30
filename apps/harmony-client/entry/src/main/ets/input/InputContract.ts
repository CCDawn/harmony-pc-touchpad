const protocolVersion: number = 1;
const headerBytes: number = 16;
const flagCoalescible: number = 1;
const flagFinal: number = 2;
const pointerDeltaType: number = 1;
const buttonType: number = 2;
const scrollType: number = 3;
const releaseAllType: number = 5;
const buttonDown: number = 1;
const buttonUp: number = 2;
const uint32Range: number = 4294967296;

function createFrame(
  type: number,
  flags: number,
  sequence: number,
  timestampUs: number,
  payloadBytes: number
): DataView {
  const view: DataView = new DataView(
    new ArrayBuffer(headerBytes + payloadBytes)
  );
  view.setUint8(0, protocolVersion);
  view.setUint8(1, type);
  view.setUint16(2, flags, true);
  view.setUint32(4, sequence, true);
  view.setUint32(8, timestampUs % uint32Range, true);
  view.setUint32(12, Math.floor(timestampUs / uint32Range), true);
  return view;
}

export function buildAuthCanonical(
  agentId: string,
  deviceId: string,
  timestampUnixMs: number,
  nonce: string
): string {
  return [
    'HPT1',
    'GET',
    '/input',
    agentId,
    deviceId,
    timestampUnixMs.toString(),
    nonce
  ].join('\n');
}

export function encodePointerDeltaFrame(
  sequence: number,
  timestampUs: number,
  dx: number,
  dy: number,
  velocity: number
): ArrayBuffer {
  const view: DataView = createFrame(
    pointerDeltaType,
    flagCoalescible,
    sequence,
    timestampUs,
    12
  );
  view.setFloat32(16, dx, true);
  view.setFloat32(20, dy, true);
  view.setFloat32(24, velocity, true);
  return view.buffer;
}

export function encodeButtonFrame(
  sequence: number,
  timestampUs: number,
  button: number,
  isDown: boolean
): ArrayBuffer {
  const view: DataView = createFrame(
    buttonType,
    0,
    sequence,
    timestampUs,
    4
  );
  view.setUint8(16, button);
  view.setUint8(17, isDown ? buttonDown : buttonUp);
  view.setUint16(18, 0, true);
  return view.buffer;
}

export function encodeScrollFrame(
  sequence: number,
  timestampUs: number,
  dx: number,
  dy: number,
  phase: number
): ArrayBuffer {
  let flags: number = 0;
  if (phase === 2) {
    flags = flagCoalescible;
  } else if (phase === 3 || phase === 4) {
    flags = flagFinal;
  }
  const view: DataView = createFrame(
    scrollType,
    flags,
    sequence,
    timestampUs,
    12
  );
  view.setFloat32(16, dx, true);
  view.setFloat32(20, dy, true);
  view.setUint8(24, phase);
  view.setUint8(25, 0);
  view.setUint16(26, 0, true);
  return view.buffer;
}

export function encodeReleaseAllFrame(
  sequence: number,
  timestampUs: number
): ArrayBuffer {
  return createFrame(
    releaseAllType,
    flagFinal,
    sequence,
    timestampUs,
    0
  ).buffer;
}

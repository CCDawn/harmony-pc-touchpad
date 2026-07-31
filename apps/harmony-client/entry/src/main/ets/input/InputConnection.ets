import { systemDateTime } from '@kit.BasicServicesKit';
import { util } from '@kit.ArkTS';
import { webSocket } from '@kit.NetworkKit';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import {
  CredentialStore,
  PairedAgentMetadata
} from '../pairing/CredentialStore';
import { encodeBase64Url } from '../pairing/Encoding';
import {
  buildAuthCanonical,
  encodeButtonFrame,
  encodePointerDeltaFrame,
  encodeReleaseAllFrame,
  encodeScrollFrame
} from './InputContract';
import {
  ScrollPhase,
  TouchpadButton
} from './TouchpadGesture';

const responseTimeoutMs: number = 10000;
const inputPath: string = '/input';
const minimumPointerIntervalMs: number = 8;

interface ProtocolVersionJson {
  major: number;
  minor: number;
}

interface HelloAckPayloadJson {
  heartbeatMs: number;
  idleReleaseMs: number;
  maxInputRateHz: number;
  capabilities: Array<string>;
}

interface ControlGrantedPayloadJson {
  controllerDeviceId: string;
}

interface HeartbeatPayloadJson {
  nonce: string;
}

interface HelloPayload {
  deviceId: string;
  deviceName: string;
  capabilities: Array<string>;
}

interface EmptyPayload {
}

interface HeartbeatPayload {
  nonce: string;
}

type OutgoingPayload = HelloPayload | EmptyPayload | HeartbeatPayload;

interface OutgoingControlMessage {
  protocol: ProtocolVersionJson;
  kind: string;
  messageId: string;
  sessionId: string | null;
  sentAtUs: string;
  payload: OutgoingPayload;
}

interface ControlMessageJson {
  protocol: ProtocolVersionJson;
  kind: string;
  messageId: string;
  sessionId: string | null;
  sentAtUs: string;
  payload: HelloAckPayloadJson |
    ControlGrantedPayloadJson |
    HeartbeatPayloadJson;
}

export interface InputConnectionStatus {
  connected: boolean;
  detail: string;
}

type InputConnectionStatusListener =
  (status: InputConnectionStatus) => void;

function messageId(): string {
  return `message-${util.generateRandomUUID(false).replace(/-/g, '')}`;
}

function monotonicMicroseconds(): number {
  return systemDateTime.getUptime(
    systemDateTime.TimeType.STARTUP,
    false
  ) * 1000;
}

function controlMessage(
  kind: string,
  sessionId: string | null,
  payload: OutgoingPayload
): string {
  const protocol: ProtocolVersionJson = {
    major: 1,
    minor: 0
  };
  const message: OutgoingControlMessage = {
    protocol: protocol,
    kind: kind,
    messageId: messageId(),
    sessionId: sessionId,
    sentAtUs: Math.floor(monotonicMicroseconds()).toString(),
    payload: payload
  };
  return JSON.stringify(message);
}

function parseControlMessage(raw: string): ControlMessageJson {
  let value: ControlMessageJson;
  try {
    value = JSON.parse(raw) as ControlMessageJson;
  } catch (_error) {
    throw new Error('电脑返回了无效的控制响应。');
  }
  if (value === null ||
    value.protocol === undefined ||
    value.protocol.major !== 1 ||
    value.protocol.minor !== 0 ||
    typeof value.kind !== 'string' ||
    typeof value.messageId !== 'string' ||
    value.messageId.length === 0 ||
    typeof value.sentAtUs !== 'string' ||
    !/^[0-9]+$/.test(value.sentAtUs) ||
    value.payload === undefined) {
    throw new Error('电脑控制响应不符合协议。');
  }
  return value;
}

class ControlResponseWaiter {
  readonly response: Promise<ControlMessageJson>;
  private settled: boolean = false;
  private timeoutId: number = -1;
  private readonly client: webSocket.WebSocket;
  private readonly expectedKind: string;

  constructor(client: webSocket.WebSocket, expectedKind: string) {
    this.client = client;
    this.expectedKind = expectedKind;
    this.response =
      new Promise<ControlMessageJson>((resolve, reject): void => {
        this.timeoutId = setTimeout((): void => {
          if (!this.settled) {
            this.settled = true;
            this.client.off('message', this.onMessage);
            reject(new Error(`等待 ${this.expectedKind} 响应超时。`));
          }
        }, responseTimeoutMs);
        this.client.on('message', this.onMessage);
        this.resolve = resolve;
        this.reject = reject;
      });
  }

  private resolve: (value: ControlMessageJson) => void =
    (_value: ControlMessageJson): void => {};
  private reject: (reason?: Object) => void =
    (_reason?: Object): void => {};

  private readonly onMessage =
    (error: Error | undefined, data: string | ArrayBuffer): void => {
      if (this.settled) {
        return;
      }
      if (error !== undefined) {
        this.finish();
        this.reject(new Error('读取电脑控制响应失败。'));
        return;
      }
      if (typeof data !== 'string') {
        this.finish();
        this.reject(new Error('电脑返回了非文本控制响应。'));
        return;
      }
      try {
        const message: ControlMessageJson = parseControlMessage(data);
        if (message.kind !== this.expectedKind) {
          this.finish();
          this.reject(
            new Error(`预期 ${this.expectedKind}，实际为 ${message.kind}。`)
          );
          return;
        }
        this.finish();
        this.resolve(message);
      } catch (parseError) {
        this.finish();
        this.reject(parseError as Error);
      }
    };

  cancel(): void {
    if (!this.settled) {
      this.finish();
    }
  }

  private finish(): void {
    this.settled = true;
    clearTimeout(this.timeoutId);
    this.client.off('message', this.onMessage);
  }
}

class WebSocketOpenWaiter {
  readonly response: Promise<void>;
  private settled: boolean = false;
  private timeoutId: number = -1;
  private readonly client: webSocket.WebSocket;

  constructor(client: webSocket.WebSocket) {
    this.client = client;
    this.response = new Promise<void>((resolve, reject): void => {
      this.timeoutId = setTimeout((): void => {
        if (!this.settled) {
          this.finish();
          reject(new Error('等待安全控制通道打开超时。'));
        }
      }, responseTimeoutMs);
      this.client.on('open',
        (error: Error | undefined, _value: Object): void => {
          if (this.settled) {
            return;
          }
          this.finish();
          if (error !== undefined) {
            reject(new Error('安全控制通道打开失败。'));
          } else {
            resolve();
          }
        });
    });
  }

  cancel(): void {
    if (!this.settled) {
      this.finish();
    }
  }

  private finish(): void {
    this.settled = true;
    clearTimeout(this.timeoutId);
    this.client.off('open');
  }
}

export class InputConnection {
  private readonly credentialStore: CredentialStore;
  private readonly statusListener: InputConnectionStatusListener;
  private client?: webSocket.WebSocket;
  private sessionId: string = '';
  private deviceId: string = '';
  private sequence: number = 0;
  private heartbeatId: number = -1;
  private isConnected: boolean = false;
  private lastPointerAtMs: number = 0;
  private pendingPointerDx: number = 0;
  private pendingPointerDy: number = 0;
  private pendingPointerVelocity: number = 0;
  private sendTail: Promise<void> = Promise.resolve();

  constructor(
    credentialStore: CredentialStore,
    statusListener: InputConnectionStatusListener
  ) {
    this.credentialStore = credentialStore;
    this.statusListener = statusListener;
  }

  async connect(): Promise<string> {
    await this.disconnect();
    const metadata: PairedAgentMetadata | undefined =
      await this.credentialStore.loadLastPairing();
    if (metadata === undefined) {
      throw new Error('尚未找到已配对的电脑，请先扫描配对二维码。');
    }
    this.deviceId = await this.credentialStore.getOrCreateDeviceId();

    const nonceBytes: Uint8Array =
      cryptoFramework.createRandom().generateRandomSync(16).data;
    const nonce: string = encodeBase64Url(nonceBytes);
    nonceBytes.fill(0);
    const timestampUnixMs: number = Date.now();
    const canonical: string = buildAuthCanonical(
      metadata.agentId,
      this.deviceId,
      timestampUnixMs,
      nonce
    );
    const signatureBytes: Uint8Array =
      await this.credentialStore.sign(metadata, canonical);
    const signature: string = encodeBase64Url(signatureBytes);
    signatureBytes.fill(0);

    const endpoint: string =
      `${metadata.endpoint.substring(0, metadata.endpoint.length - 5)}${inputPath}`;
    const client: webSocket.WebSocket = webSocket.createWebSocket();
    this.client = client;
    client.on('close', (_error, _value): void => {
      this.markDisconnected('电脑已断开控制连接。');
    });
    client.on('error', (_error): void => {
      this.markDisconnected('控制连接发生网络错误。');
    });

    let helloWaiter: ControlResponseWaiter | undefined;
    let grantWaiter: ControlResponseWaiter | undefined;
    const openWaiter: WebSocketOpenWaiter =
      new WebSocketOpenWaiter(client);
    try {
      const connected: boolean = await client.connect(endpoint, {
        header: {
          'X-HPT-Version': '1',
          'X-HPT-Device-Id': this.deviceId,
          'X-HPT-Timestamp-Unix-Ms': timestampUnixMs.toString(),
          'X-HPT-Nonce': nonce,
          'X-HPT-Signature': signature
        },
        caPath: metadata.certificatePath
      });
      if (!connected) {
        throw new Error('无法建立安全控制连接。');
      }
      await openWaiter.response;

      helloWaiter = new ControlResponseWaiter(client, 'HELLO_ACK');
      const outgoingHelloPayload: HelloPayload = {
        deviceId: this.deviceId,
        deviceName: 'Harmony Phone',
        capabilities: ['pointer-delta', 'scroll-v1']
      };
      await this.sendText(controlMessage(
        'HELLO',
        null,
        outgoingHelloPayload
      ));
      const helloAck: ControlMessageJson = await helloWaiter.response;
      if (typeof helloAck.sessionId !== 'string' ||
        helloAck.sessionId.length === 0) {
        throw new Error('电脑未返回有效的控制会话。');
      }
      const helloPayload: HelloAckPayloadJson =
        helloAck.payload as HelloAckPayloadJson;
      if (!Array.isArray(helloPayload.capabilities) ||
        !helloPayload.capabilities.includes('pointer-delta') ||
        !Number.isInteger(helloPayload.heartbeatMs) ||
        helloPayload.heartbeatMs < 100) {
        throw new Error('电脑未协商出可用的指针控制能力。');
      }
      this.sessionId = helloAck.sessionId;

      grantWaiter = new ControlResponseWaiter(client, 'CONTROL_GRANTED');
      const requestPayload: EmptyPayload = {};
      await this.sendText(controlMessage(
        'CONTROL_REQUEST',
        this.sessionId,
        requestPayload
      ));
      const granted: ControlMessageJson = await grantWaiter.response;
      const grantedPayload: ControlGrantedPayloadJson =
        granted.payload as ControlGrantedPayloadJson;
      if (granted.sessionId !== this.sessionId ||
        grantedPayload.controllerDeviceId !== this.deviceId) {
        throw new Error('电脑授予的控制会话与当前设备不匹配。');
      }

      this.sequence = 0;
      this.isConnected = true;
      this.lastPointerAtMs = 0;
      this.resetPendingPointer();
      this.startHeartbeat(helloPayload.heartbeatMs);
      this.statusListener({
        connected: true,
        detail: `已连接 ${metadata.agentId}`
      });
      return metadata.agentId;
    } catch (error) {
      await this.closeSocket();
      throw error as Error;
    } finally {
      openWaiter.cancel();
      helloWaiter?.cancel();
      grantWaiter?.cancel();
    }
  }

  move(dx: number, dy: number, velocity: number): void {
    if (!this.isConnected) {
      return;
    }
    this.pendingPointerDx += dx;
    this.pendingPointerDy += dy;
    this.pendingPointerVelocity = Math.max(
      this.pendingPointerVelocity,
      velocity
    );
    const nowMs: number = systemDateTime.getUptime(
      systemDateTime.TimeType.STARTUP,
      false
    );
    if (nowMs - this.lastPointerAtMs < minimumPointerIntervalMs) {
      return;
    }
    this.lastPointerAtMs = nowMs;
    const pendingDx: number = this.pendingPointerDx;
    const pendingDy: number = this.pendingPointerDy;
    const pendingVelocity: number = this.pendingPointerVelocity;
    this.resetPendingPointer();
    const frame: ArrayBuffer = encodePointerDeltaFrame(
      this.nextSequence(),
      nowMs * 1000,
      pendingDx,
      pendingDy,
      pendingVelocity
    );
    this.enqueue(frame);
  }

  click(button: TouchpadButton): void {
    if (!this.isConnected) {
      return;
    }
    this.flushPendingPointer();
    const timestampUs: number = monotonicMicroseconds();
    this.enqueue(encodeButtonFrame(
      this.nextSequence(),
      timestampUs,
      this.buttonCode(button),
      true
    ));
    this.enqueue(encodeButtonFrame(
      this.nextSequence(),
      timestampUs + 1,
      this.buttonCode(button),
      false
    ));
  }

  button(button: TouchpadButton, isDown: boolean): void {
    if (!this.isConnected) {
      return;
    }
    this.flushPendingPointer();
    this.enqueue(encodeButtonFrame(
      this.nextSequence(),
      monotonicMicroseconds(),
      this.buttonCode(button),
      isDown
    ));
  }

  scroll(
    dx: number,
    dy: number,
    phase: ScrollPhase
  ): void {
    if (!this.isConnected) {
      return;
    }
    this.enqueue(encodeScrollFrame(
      this.nextSequence(),
      monotonicMicroseconds(),
      dx,
      dy,
      this.scrollPhaseCode(phase)
    ));
  }

  releaseAll(): void {
    if (!this.isConnected) {
      return;
    }
    this.flushPendingPointer();
    this.enqueue(encodeReleaseAllFrame(
      this.nextSequence(),
      monotonicMicroseconds()
    ));
  }

  async disconnect(): Promise<void> {
    this.stopHeartbeat();
    if (this.isConnected && this.client !== undefined) {
      this.flushPendingPointer();
      this.enqueue(encodeReleaseAllFrame(
        this.nextSequence(),
        monotonicMicroseconds()
      ));
      try {
        await this.sendTail;
      } catch (_error) {
      }
    }
    this.isConnected = false;
    this.resetPendingPointer();
    await this.closeSocket();
  }

  private startHeartbeat(intervalMs: number): void {
    this.stopHeartbeat();
    this.heartbeatId = setInterval((): void => {
      if (!this.isConnected || this.sessionId.length === 0) {
        return;
      }
      const heartbeatPayload: HeartbeatPayload = {
        nonce: messageId()
      };
      this.enqueue(controlMessage(
        'PING',
        this.sessionId,
        heartbeatPayload
      ));
    }, intervalMs);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatId >= 0) {
      clearInterval(this.heartbeatId);
      this.heartbeatId = -1;
    }
  }

  private enqueue(data: string | ArrayBuffer): void {
    this.sendTail = this.sendTail
      .then((): Promise<void> => this.send(data))
      .catch((_error: Error): void => {
        this.markDisconnected('向电脑发送输入失败。');
      });
  }

  private async sendText(data: string): Promise<void> {
    await this.send(data);
  }

  private async send(data: string | ArrayBuffer): Promise<void> {
    if (this.client === undefined) {
      throw new Error('控制连接尚未建立。');
    }
    const sent: boolean = await this.client.send(data);
    if (!sent) {
      throw new Error('控制消息发送失败。');
    }
  }

  private nextSequence(): number {
    const current: number = this.sequence;
    this.sequence = (this.sequence + 1) >>> 0;
    return current;
  }

  private flushPendingPointer(): void {
    if (this.pendingPointerDx === 0 && this.pendingPointerDy === 0) {
      return;
    }
    const nowMs: number = systemDateTime.getUptime(
      systemDateTime.TimeType.STARTUP,
      false
    );
    this.lastPointerAtMs = nowMs;
    const pendingDx: number = this.pendingPointerDx;
    const pendingDy: number = this.pendingPointerDy;
    const pendingVelocity: number = this.pendingPointerVelocity;
    this.resetPendingPointer();
    this.enqueue(encodePointerDeltaFrame(
      this.nextSequence(),
      nowMs * 1000,
      pendingDx,
      pendingDy,
      pendingVelocity
    ));
  }

  private resetPendingPointer(): void {
    this.pendingPointerDx = 0;
    this.pendingPointerDy = 0;
    this.pendingPointerVelocity = 0;
  }

  private buttonCode(button: TouchpadButton): number {
    return button === 'left' ? 1 : 2;
  }

  private scrollPhaseCode(phase: ScrollPhase): number {
    if (phase === 'begin') {
      return 1;
    }
    if (phase === 'update') {
      return 2;
    }
    if (phase === 'end') {
      return 3;
    }
    return 4;
  }

  private markDisconnected(detail: string): void {
    if (!this.isConnected) {
      return;
    }
    this.isConnected = false;
    this.stopHeartbeat();
    this.statusListener({
      connected: false,
      detail: detail
    });
  }

  private async closeSocket(): Promise<void> {
    const client: webSocket.WebSocket | undefined = this.client;
    this.client = undefined;
    this.sessionId = '';
    if (client !== undefined) {
      try {
        await client.close();
      } catch (_error) {
      }
    }
  }
}

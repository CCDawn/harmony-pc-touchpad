import { common } from '@kit.AbilityKit';
import { webSocket } from '@kit.NetworkKit';
import { verifyAndCacheCertificate } from './CertificatePinning';
import { CredentialStore } from './CredentialStore';
import {
  PairingAccepted,
  PairingQr,
  parsePairingAccepted,
  parsePairingQr
} from './PairingContract';

const responseTimeoutMs: number = 10000;

class PairingResponseWaiter {
  readonly response: Promise<PairingAccepted>;
  private settled: boolean = false;
  private timeoutId: number = -1;

  constructor(client: webSocket.WebSocket, deviceId: string) {
    this.response =
      new Promise<PairingAccepted>((resolve, reject): void => {
        this.timeoutId = setTimeout((): void => {
          if (!this.settled) {
            this.settled = true;
            reject(new Error('等待电脑确认配对超时。'));
          }
        }, responseTimeoutMs);

        client.on('message',
          (error: Error | undefined, data: string | ArrayBuffer): void => {
            if (this.settled) {
              return;
            }
            this.settled = true;
            clearTimeout(this.timeoutId);
            if (error !== undefined) {
              reject(new Error('读取电脑配对响应失败。'));
              return;
            }
            if (typeof data !== 'string') {
              reject(new Error('电脑返回了非文本配对响应。'));
              return;
            }
            try {
              resolve(parsePairingAccepted(data, deviceId));
            } catch (parseError) {
              reject(parseError as Error);
            }
          });
      });
  }

  cancel(): void {
    if (!this.settled) {
      this.settled = true;
      clearTimeout(this.timeoutId);
    }
  }
}

export class PairingClient {
  private readonly context: common.Context;
  private readonly credentialStore: CredentialStore;

  constructor(context: common.Context) {
    this.context = context;
    this.credentialStore = new CredentialStore(context);
  }

  async pair(scannedValue: string): Promise<string> {
    const qr: PairingQr = parsePairingQr(scannedValue, Date.now());
    const deviceId: string =
      await this.credentialStore.getOrCreateDeviceId();
    const certificatePath: string =
      await verifyAndCacheCertificate(this.context, qr);
    if (Date.now() >= qr.expiresAtUnixMs) {
      throw new Error('二维码在证书校验期间已过期，请重新扫描。');
    }

    const client: webSocket.WebSocket = webSocket.createWebSocket();
    let accepted: PairingAccepted | undefined;
    const waiter: PairingResponseWaiter =
      new PairingResponseWaiter(client, deviceId);
    try {
      const connected: boolean = await client.connect(qr.endpoint, {
        header: {
          'X-HPT-Version': '1',
          'X-HPT-Device-Id': deviceId,
          'X-HPT-Pairing-Token': qr.pairingToken
        },
        caPath: certificatePath
      });
      if (!connected) {
        throw new Error('无法建立安全配对连接。');
      }
      accepted = await waiter.response;
      await this.credentialStore.savePairing(
        qr,
        certificatePath,
        accepted.deviceSecret);
      return qr.agentId;
    } finally {
      waiter.cancel();
      if (accepted !== undefined) {
        accepted.deviceSecret.fill(0);
      }
      try {
        await client.close();
      } catch (_error) {
      }
    }
  }
}

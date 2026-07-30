import { url } from '@kit.ArkTS';
import { decodeExactBase64Url } from './Encoding';

const identifierPattern: RegExp = /^[A-Za-z0-9._-]{1,128}$/;

interface PairingQrJson {
  v: number;
  agentId: string;
  endpoint: string;
  spkiSha256: string;
  pairingToken: string;
  expiresAtUnixMs: number;
}

interface ProtocolVersionJson {
  major: number;
  minor: number;
}

interface PairingAcceptedPayloadJson {
  deviceId: string;
  secretVersion: number;
  deviceSecret: string;
}

interface PairingAcceptedJson {
  protocol: ProtocolVersionJson;
  kind: string;
  messageId: string;
  sessionId: string | null;
  sentAtUs: string;
  payload: PairingAcceptedPayloadJson;
}

export interface PairingQr {
  agentId: string;
  endpoint: string;
  endpointHost: string;
  endpointPort: number;
  spkiSha256: string;
  pairingToken: string;
  expiresAtUnixMs: number;
}

export interface PairingAccepted {
  deviceId: string;
  deviceSecret: Uint8Array;
}

export function parsePairingQr(raw: string, nowUnixMs: number): PairingQr {
  let value: PairingQrJson;
  try {
    value = JSON.parse(raw) as PairingQrJson;
  } catch (_error) {
    throw new Error('二维码不是有效的配对 JSON。');
  }

  if (value === null ||
    value.v !== 1 ||
    typeof value.agentId !== 'string' ||
    !identifierPattern.test(value.agentId) ||
    typeof value.endpoint !== 'string' ||
    typeof value.spkiSha256 !== 'string' ||
    typeof value.pairingToken !== 'string' ||
    !Number.isSafeInteger(value.expiresAtUnixMs) ||
    value.expiresAtUnixMs <= nowUnixMs) {
    throw new Error('二维码字段无效或已经过期。');
  }

  let endpoint: url.URL;
  try {
    endpoint = url.URL.parseURL(value.endpoint);
  } catch (_error) {
    throw new Error('配对地址无效。');
  }
  if (endpoint.protocol !== 'wss:' ||
    endpoint.pathname !== '/pair' ||
    endpoint.search !== '' ||
    endpoint.hash !== '' ||
    endpoint.username !== '' ||
    endpoint.password !== '' ||
    endpoint.hostname.length === 0) {
    throw new Error('配对地址必须是干净的 wss://.../pair 地址。');
  }

  const endpointPort: number =
    endpoint.port.length === 0 ? 443 : Number(endpoint.port);
  if (!Number.isInteger(endpointPort) ||
    endpointPort < 1 ||
    endpointPort > 65535) {
    throw new Error('配对端口无效。');
  }

  const fingerprint: Uint8Array =
    decodeExactBase64Url(value.spkiSha256, 32, '证书指纹');
  fingerprint.fill(0);
  const token: Uint8Array =
    decodeExactBase64Url(value.pairingToken, 32, '配对令牌');
  token.fill(0);

  return {
    agentId: value.agentId,
    endpoint: value.endpoint,
    endpointHost: endpoint.hostname,
    endpointPort: endpointPort,
    spkiSha256: value.spkiSha256,
    pairingToken: value.pairingToken,
    expiresAtUnixMs: value.expiresAtUnixMs
  };
}

export function parsePairingAccepted(
  raw: string,
  expectedDeviceId: string
): PairingAccepted {
  let value: PairingAcceptedJson;
  try {
    value = JSON.parse(raw) as PairingAcceptedJson;
  } catch (_error) {
    throw new Error('电脑返回了无效的配对响应。');
  }

  if (value === null ||
    value.protocol === undefined ||
    value.protocol.major !== 1 ||
    value.protocol.minor !== 0 ||
    value.kind !== 'PAIRING_ACCEPTED' ||
    typeof value.messageId !== 'string' ||
    value.messageId.length === 0 ||
    value.messageId.length > 64 ||
    value.sessionId !== null ||
    typeof value.sentAtUs !== 'string' ||
    !/^[0-9]+$/.test(value.sentAtUs) ||
    value.payload === undefined ||
    value.payload.deviceId !== expectedDeviceId ||
    value.payload.secretVersion !== 1 ||
    typeof value.payload.deviceSecret !== 'string') {
    throw new Error('电脑返回的配对响应不符合协议。');
  }

  return {
    deviceId: expectedDeviceId,
    deviceSecret: decodeExactBase64Url(
      value.payload.deviceSecret,
      32,
      '设备密钥')
  };
}

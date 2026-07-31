import { common } from '@kit.AbilityKit';
import { preferences } from '@kit.ArkData';
import { util } from '@kit.ArkTS';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import { huks } from '@kit.UniversalKeystoreKit';
import { encodeBase64Url } from './Encoding';
import { PairingQr } from './PairingContract';

const preferencesName: string = 'hpt_pairing';
const deviceIdKey: string = 'deviceId';
const lastPairedAgentIdKey: string = 'lastPairedAgentId';

export interface PairedAgentMetadata {
  agentId: string;
  endpoint: string;
  spkiSha256: string;
  certificatePath: string;
  keyAlias: string;
  pairedAtUnixMs: number;
}

function hmacProperties(): Array<huks.HuksParam> {
  return [
    {
      tag: huks.HuksTag.HUKS_TAG_ALGORITHM,
      value: huks.HuksKeyAlg.HUKS_ALG_HMAC
    },
    {
      tag: huks.HuksTag.HUKS_TAG_KEY_SIZE,
      value: huks.HuksKeySize.HUKS_AES_KEY_SIZE_256
    },
    {
      tag: huks.HuksTag.HUKS_TAG_PURPOSE,
      value: huks.HuksKeyPurpose.HUKS_KEY_PURPOSE_MAC
    },
    {
      tag: huks.HuksTag.HUKS_TAG_DIGEST,
      value: huks.HuksKeyDigest.HUKS_DIGEST_SHA256
    }
  ];
}

function emptyHuksOptions(): huks.HuksOptions {
  return {
    properties: hmacProperties(),
    inData: new Uint8Array(0)
  };
}

async function createKeyAlias(agentId: string): Promise<string> {
  const encoder: util.TextEncoder = util.TextEncoder.create('utf-8');
  const digest: cryptoFramework.Md = cryptoFramework.createMd('SHA256');
  await digest.update({ data: encoder.encodeInto(agentId) });
  return `hpt.${encodeBase64Url((await digest.digest()).data)}`;
}

export class CredentialStore {
  private readonly context: common.Context;

  constructor(context: common.Context) {
    this.context = context;
  }

  async getOrCreateDeviceId(): Promise<string> {
    const store: preferences.Preferences =
      await preferences.getPreferences(this.context, preferencesName);
    const stored: preferences.ValueType =
      await store.get(deviceIdKey, '');
    if (typeof stored === 'string' &&
      /^[A-Za-z0-9._-]{1,128}$/.test(stored)) {
      return stored;
    }

    const deviceId: string =
      `harmony-${util.generateRandomUUID(false).replace(/-/g, '')}`;
    await store.put(deviceIdKey, deviceId);
    await store.flush();
    return deviceId;
  }

  async savePairing(
    qr: PairingQr,
    certificatePath: string,
    deviceSecret: Uint8Array
  ): Promise<void> {
    const keyAlias: string = await createKeyAlias(qr.agentId);
    const lookupOptions: huks.HuksOptions = emptyHuksOptions();
    if (await huks.hasKeyItem(keyAlias, lookupOptions)) {
      await huks.deleteKeyItem(keyAlias, lookupOptions);
    }

    const importOptions: huks.HuksOptions = {
      properties: hmacProperties(),
      inData: deviceSecret
    };
    await huks.importKeyItem(keyAlias, importOptions);

    const metadata: PairedAgentMetadata = {
      agentId: qr.agentId,
      endpoint: qr.endpoint,
      spkiSha256: qr.spkiSha256,
      certificatePath: certificatePath,
      keyAlias: keyAlias,
      pairedAtUnixMs: Date.now()
    };
    const store: preferences.Preferences =
      await preferences.getPreferences(this.context, preferencesName);
    await store.put(`agent.${qr.agentId}`, JSON.stringify(metadata));
    await store.put(lastPairedAgentIdKey, qr.agentId);
    await store.flush();
  }

  async loadLastPairing(): Promise<PairedAgentMetadata | undefined> {
    const store: preferences.Preferences =
      await preferences.getPreferences(this.context, preferencesName);
    const agentIdValue: preferences.ValueType =
      await store.get(lastPairedAgentIdKey, '');
    if (typeof agentIdValue !== 'string' ||
      !/^[A-Za-z0-9._-]{1,128}$/.test(agentIdValue)) {
      return undefined;
    }

    const metadataValue: preferences.ValueType =
      await store.get(`agent.${agentIdValue}`, '');
    if (typeof metadataValue !== 'string' || metadataValue.length === 0) {
      return undefined;
    }

    let metadata: PairedAgentMetadata;
    try {
      metadata = JSON.parse(metadataValue) as PairedAgentMetadata;
    } catch (_error) {
      return undefined;
    }
    if (metadata === null ||
      metadata.agentId !== agentIdValue ||
      typeof metadata.endpoint !== 'string' ||
      !metadata.endpoint.endsWith('/pair') ||
      typeof metadata.certificatePath !== 'string' ||
      metadata.certificatePath.length === 0 ||
      typeof metadata.keyAlias !== 'string' ||
      metadata.keyAlias.length === 0) {
      return undefined;
    }
    return metadata;
  }

  async sign(
    metadata: PairedAgentMetadata,
    canonical: string
  ): Promise<Uint8Array> {
    const encoder: util.TextEncoder = util.TextEncoder.create('utf-8');
    const input: Uint8Array = encoder.encodeInto(canonical);
    const initOptions: huks.HuksOptions = {
      properties: hmacProperties(),
      inData: new Uint8Array(0)
    };
    let handle: number | undefined;
    try {
      const session: huks.HuksSessionHandle =
        await huks.initSession(metadata.keyAlias, initOptions);
      handle = session.handle;
      await huks.updateSession(handle, {
        properties: [],
        inData: input
      });
      const result: huks.HuksReturnResult =
        await huks.finishSession(handle, {
          properties: [],
          inData: new Uint8Array(0)
        });
      handle = undefined;
      if (result.outData === undefined || result.outData.length !== 32) {
        throw new Error('HUKS 未返回有效的认证签名。');
      }
      return result.outData;
    } catch (error) {
      if (handle !== undefined) {
        try {
          await huks.abortSession(handle, {
            properties: [],
            inData: new Uint8Array(0)
          });
        } catch (_abortError) {
        }
      }
      throw error as Error;
    } finally {
      input.fill(0);
    }
  }
}

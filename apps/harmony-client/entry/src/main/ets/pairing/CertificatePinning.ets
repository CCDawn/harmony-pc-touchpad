import { common } from '@kit.AbilityKit';
import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import { cert } from '@kit.DeviceCertificateKit';
import { fileIo } from '@kit.CoreFileKit';
import { socket } from '@kit.NetworkKit';
import { decodeExactBase64Url, encodeBase64, fixedTimeEqual } from './Encoding';
import { PairingQr } from './PairingContract';

function toPem(der: Uint8Array): string {
  const encoded: string = encodeBase64(der);
  const lines: Array<string> = [];
  for (let index: number = 0; index < encoded.length; index += 64) {
    lines.push(encoded.substring(index, Math.min(index + 64, encoded.length)));
  }
  return `-----BEGIN CERTIFICATE-----\n${lines.join('\n')}\n-----END CERTIFICATE-----\n`;
}

async function computeSpkiSha256(
  certificate: socket.X509CertRawData
): Promise<Uint8Array> {
  const parsed: cert.X509Cert = await cert.createX509Cert(certificate);
  const publicKey: cryptoFramework.PubKey = parsed.getPublicKey();
  const digest: cryptoFramework.Md = cryptoFramework.createMd('SHA256');
  await digest.update(publicKey.getEncoded());
  return (await digest.digest()).data;
}

function saveCertificate(
  context: common.Context,
  agentId: string,
  der: Uint8Array
): string {
  const path: string = `${context.filesDir}/hpt-ca-${agentId}.pem`;
  const file: fileIo.File = fileIo.openSync(
    path,
    fileIo.OpenMode.CREATE |
    fileIo.OpenMode.READ_WRITE |
    fileIo.OpenMode.TRUNC);
  try {
    fileIo.writeSync(file.fd, toPem(der));
  } finally {
    fileIo.closeSync(file.fd);
  }
  return path;
}

export async function verifyAndCacheCertificate(
  context: common.Context,
  qr: PairingQr
): Promise<string> {
  const tls: socket.TLSSocket = socket.constructTLSSocketInstance();
  try {
    const useIpv6: boolean = qr.endpointHost.includes(':');
    await tls.bind({
      address: useIpv6 ? '::' : '0.0.0.0',
      family: useIpv6 ? 2 : 1,
      port: 0
    });
    const options: socket.TLSConnectOptions = {
      address: {
        address: qr.endpointHost,
        port: qr.endpointPort
      },
      secureOptions: {},
      ALPNProtocols: ['http/1.1'],
      skipRemoteValidation: true
    };
    await tls.connect(options);
    const certificate: socket.X509CertRawData =
      await tls.getRemoteCertificate();
    if (certificate.data.length === 0) {
      throw new Error('电脑没有提供 TLS 证书。');
    }

    const actual: Uint8Array = await computeSpkiSha256(certificate);
    const expected: Uint8Array =
      decodeExactBase64Url(qr.spkiSha256, 32, '证书指纹');
    const matches: boolean = fixedTimeEqual(actual, expected);
    actual.fill(0);
    expected.fill(0);
    if (!matches) {
      throw new Error('电脑证书与二维码指纹不一致，已拒绝连接。');
    }

    return saveCertificate(context, qr.agentId, certificate.data);
  } finally {
    try {
      await tls.close();
    } catch (_error) {
    }
  }
}

import { util } from '@kit.ArkTS';

const base64: util.Base64Helper = new util.Base64Helper();

export function decodeExactBase64Url(
  value: string,
  expectedBytes: number,
  field: string
): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) {
    throw new Error(`${field} 必须是无填充 base64url。`);
  }

  let decoded: Uint8Array;
  try {
    decoded = base64.decodeSync(value, util.Type.BASIC_URL_SAFE);
  } catch (_error) {
    throw new Error(`${field} 不是有效的 base64url。`);
  }

  if (decoded.length !== expectedBytes ||
    encodeBase64Url(decoded) !== value) {
    decoded.fill(0);
    throw new Error(`${field} 必须恰好包含 ${expectedBytes} 字节。`);
  }
  return decoded;
}

export function encodeBase64Url(value: Uint8Array): string {
  return base64
    .encodeToStringSync(value, util.Type.BASIC_URL_SAFE)
    .replace(/=+$/g, '');
}

export function encodeBase64(value: Uint8Array): string {
  return base64.encodeToStringSync(value, util.Type.BASIC);
}

export function fixedTimeEqual(
  left: Uint8Array,
  right: Uint8Array
): boolean {
  if (left.length !== right.length) {
    return false;
  }

  let difference: number = 0;
  for (let index: number = 0; index < left.length; index++) {
    difference |= left[index] ^ right[index];
  }
  return difference === 0;
}

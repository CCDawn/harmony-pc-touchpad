import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import {
  canonicalizeAuthRequest,
  signAuthRequest,
  validatePairingQr,
  verifyAuthRequest,
} from '../reference/security.mjs';

const vectorsUrl = new URL(
  '../v1/test-vectors/security-auth.json',
  import.meta.url,
);

async function loadVectors() {
  return JSON.parse(await readFile(vectorsUrl, 'utf8'));
}

test('security vectors freeze the QR and HMAC contract across runtimes', async () => {
  const fixture = await loadVectors();
  const qr = fixture.pairingQr;
  const auth = fixture.authRequest;
  const secret = Buffer.from(auth.secretHex, 'hex');

  assert.equal(JSON.stringify(validatePairingQr(qr.payload)), qr.json);
  assert.equal(canonicalizeAuthRequest(auth), auth.canonical);
  assert.equal(signAuthRequest(secret, auth), auth.signature);
  assert.equal(verifyAuthRequest(secret, auth, auth.signature), true);
});

test('pairing tokens cannot move into a network URL', async () => {
  const fixture = await loadVectors();
  const payload = {
    ...fixture.pairingQr.payload,
    endpoint: `${fixture.pairingQr.payload.endpoint}?token=secret`,
  };

  assert.throws(
    () => validatePairingQr(payload),
    /clean wss:\/\/ URL/,
  );
});

test('tampered authentication material fails fixed-length verification', async () => {
  const fixture = await loadVectors();
  const auth = fixture.authRequest;
  const secret = Buffer.from(auth.secretHex, 'hex');

  assert.equal(
    verifyAuthRequest(secret, auth, 'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'),
    false,
  );
  assert.throws(
    () => canonicalizeAuthRequest({ ...auth, path: '/admin' }),
    /GET \/input/,
  );
});

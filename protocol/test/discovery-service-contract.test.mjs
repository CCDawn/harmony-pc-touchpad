import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const contractUrl = new URL('../v1/discovery-service.json', import.meta.url);

async function loadContract() {
  return JSON.parse(await readFile(contractUrl, 'utf8'));
}

test('discovery v1 freezes the DNS-SD service and TXT surface', async () => {
  const contract = await loadContract();

  assert.equal(contract.version, 1);
  assert.equal(contract.serviceType, '_hptouchpad._tcp');
  assert.equal(contract.qualifiedServiceType, '_hptouchpad._tcp.local');
  assert.equal(contract.port, 47431);
  assert.deepEqual(contract.txtKeys, ['v', 'id', 'name', 'pairing']);
  assert.deepEqual(Object.keys(contract.example), contract.txtKeys);
});

test('discovery TXT metadata never carries credentials', async () => {
  const contract = await loadContract();
  const forbidden = new Set(contract.forbiddenTxtKeys);

  assert.deepEqual(
    [...forbidden],
    ['pairingToken', 'spkiSha256', 'deviceSecret'],
  );
  assert.equal(
    Object.keys(contract.example).some((key) => forbidden.has(key)),
    false,
  );
});

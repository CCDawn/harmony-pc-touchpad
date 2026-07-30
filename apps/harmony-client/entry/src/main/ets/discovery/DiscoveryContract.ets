import { mdns } from '@kit.NetworkKit';
import { util } from '@kit.ArkTS';

export const DISCOVERY_SERVICE_TYPE: string = '_hptouchpad._tcp';
export const DISCOVERY_PROTOCOL_VERSION: string = '1';
export const DISCOVERY_PORT: number = 47431;

export interface DiscoveredAgent {
  id: string;
  name: string;
  address: string;
  port: number;
  pairingAllowed: boolean;
  serviceName: string;
}

function decodeAttribute(value: Array<number>): string {
  const decoder: util.TextDecoder = util.TextDecoder.create('utf-8', {
    ignoreBOM: true,
    fatal: true
  });
  return decoder.decodeToString(new Uint8Array(value));
}

function findAttribute(attributes: Array<mdns.ServiceAttribute>, key: string): string | undefined {
  const attribute: mdns.ServiceAttribute | undefined =
    attributes.find((item: mdns.ServiceAttribute): boolean => item.key === key);
  if (attribute === undefined) {
    return undefined;
  }

  try {
    return decodeAttribute(attribute.value);
  } catch (_error) {
    return undefined;
  }
}

function hasExactAttributeSurface(attributes: Array<mdns.ServiceAttribute>): boolean {
  if (attributes.length !== 4) {
    return false;
  }

  const expectedKeys: Array<string> = ['v', 'id', 'name', 'pairing'];
  return expectedKeys.every((key: string): boolean =>
    attributes.filter((item: mdns.ServiceAttribute): boolean => item.key === key).length === 1);
}

export function parseDiscoveredAgent(service: mdns.LocalServiceInfo): DiscoveredAgent | undefined {
  if (service.serviceType !== DISCOVERY_SERVICE_TYPE ||
    service.port !== DISCOVERY_PORT ||
    service.host === undefined ||
    service.host.address.trim().length === 0 ||
    service.serviceAttribute === undefined ||
    !hasExactAttributeSurface(service.serviceAttribute)) {
    return undefined;
  }

  const version: string | undefined = findAttribute(service.serviceAttribute, 'v');
  const id: string | undefined = findAttribute(service.serviceAttribute, 'id');
  const name: string | undefined = findAttribute(service.serviceAttribute, 'name');
  const pairing: string | undefined = findAttribute(service.serviceAttribute, 'pairing');

  if (version !== DISCOVERY_PROTOCOL_VERSION ||
    id === undefined ||
    !/^[A-Za-z0-9._-]{1,128}$/.test(id) ||
    name === undefined ||
    name.trim().length === 0 ||
    name.length > 128 ||
    (pairing !== '0' && pairing !== '1')) {
    return undefined;
  }

  return {
    id: id,
    name: name.trim(),
    address: service.host.address,
    port: DISCOVERY_PORT,
    pairingAllowed: pairing === '1',
    serviceName: service.serviceName
  };
}

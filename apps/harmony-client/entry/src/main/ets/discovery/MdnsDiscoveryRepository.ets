import { common } from '@kit.AbilityKit';
import { mdns } from '@kit.NetworkKit';
import {
  DISCOVERY_SERVICE_TYPE,
  DiscoveredAgent,
  parseDiscoveredAgent
} from './DiscoveryContract';

export type AgentListListener = (agents: Array<DiscoveredAgent>) => void;

export class MdnsDiscoveryRepository {
  private readonly context: common.Context;
  private readonly listener: AgentListListener;
  private discovery: mdns.DiscoveryService | undefined;
  private generation: number = 0;
  private readonly agentsById: Map<string, DiscoveredAgent> = new Map<string, DiscoveredAgent>();
  private readonly idByServiceName: Map<string, string> = new Map<string, string>();

  private readonly onServiceFound = (service: mdns.LocalServiceInfo): void => {
    const activeGeneration: number = this.generation;
    mdns.resolveLocalService(this.context, service)
      .then((resolved: mdns.LocalServiceInfo): void => {
        if (this.discovery === undefined || activeGeneration !== this.generation) {
          return;
        }

        const agent: DiscoveredAgent | undefined = parseDiscoveredAgent(resolved);
        if (agent === undefined) {
          return;
        }

        const previousId: string | undefined =
          this.idByServiceName.get(agent.serviceName);
        if (previousId !== undefined && previousId !== agent.id) {
          this.agentsById.delete(previousId);
        }

        this.agentsById.set(agent.id, agent);
        this.idByServiceName.set(agent.serviceName, agent.id);
        this.publish();
      })
      .catch((_error: Error): void => {
      });
  };

  private readonly onServiceLost = (service: mdns.LocalServiceInfo): void => {
    const id: string | undefined = this.idByServiceName.get(service.serviceName);
    if (id === undefined) {
      return;
    }

    this.idByServiceName.delete(service.serviceName);
    this.agentsById.delete(id);
    this.publish();
  };

  constructor(context: common.Context, listener: AgentListListener) {
    this.context = context;
    this.listener = listener;
  }

  start(): void {
    if (this.discovery !== undefined) {
      return;
    }

    const discovery: mdns.DiscoveryService =
      mdns.createDiscoveryService(this.context, DISCOVERY_SERVICE_TYPE);
    discovery.on('serviceFound', this.onServiceFound);
    discovery.on('serviceLost', this.onServiceLost);

    this.generation++;
    this.discovery = discovery;
    try {
      discovery.startSearchingMDNS();
    } catch (_error) {
      this.discovery = undefined;
      this.generation++;
      discovery.off('serviceFound', this.onServiceFound);
      discovery.off('serviceLost', this.onServiceLost);
      throw new Error('Unable to start local PC discovery.');
    }
  }

  stop(): void {
    const discovery: mdns.DiscoveryService | undefined = this.discovery;
    if (discovery === undefined) {
      return;
    }

    this.discovery = undefined;
    this.generation++;

    try {
      discovery.stopSearchingMDNS();
    } finally {
      discovery.off('serviceFound', this.onServiceFound);
      discovery.off('serviceLost', this.onServiceLost);
      this.agentsById.clear();
      this.idByServiceName.clear();
      this.publish();
    }
  }

  isRunning(): boolean {
    return this.discovery !== undefined;
  }

  private publish(): void {
    const snapshot: Array<DiscoveredAgent> = Array.from(this.agentsById.values());
    snapshot.sort((left: DiscoveredAgent, right: DiscoveredAgent): number => {
      const byName: number = left.name.localeCompare(right.name);
      return byName === 0 ? left.id.localeCompare(right.id) : byName;
    });
    this.listener(snapshot);
  }
}

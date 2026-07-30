import { common } from '@kit.AbilityKit';
import { scanBarcode, scanCore } from '@kit.ScanKit';
import { DiscoveredAgent } from '../discovery/DiscoveryContract';
import { MdnsDiscoveryRepository } from '../discovery/MdnsDiscoveryRepository';
import { PairingClient } from '../pairing/PairingClient';

@Entry
@Component
struct Index {
  @State private agents: Array<DiscoveredAgent> = [];
  @State private isSearching: boolean = false;
  @State private discoveryError: string = '';
  @State private isPairing: boolean = false;
  @State private pairingStatus: string = '';
  @State private pairingError: string = '';
  private repository?: MdnsDiscoveryRepository;
  private pairingClient?: PairingClient;

  aboutToAppear(): void {
    const context: common.Context =
      this.getUIContext().getHostContext() as common.Context;
    this.repository = new MdnsDiscoveryRepository(
      context,
      (agents: Array<DiscoveredAgent>): void => {
        this.agents = agents;
      }
    );
    this.pairingClient = new PairingClient(context);
    this.startDiscovery();
  }

  aboutToDisappear(): void {
    this.repository?.stop();
    this.isSearching = false;
  }

  private startDiscovery(): void {
    try {
      this.repository?.start();
      this.isSearching = this.repository?.isRunning() ?? false;
      this.discoveryError = '';
    } catch (_error) {
      this.isSearching = false;
      this.discoveryError = '无法启动局域网发现，请检查网络后重试。';
    }
  }

  private stopDiscovery(): void {
    try {
      this.repository?.stop();
      this.discoveryError = '';
    } catch (_error) {
      this.discoveryError = '发现服务未能正常停止，请稍后重试。';
    } finally {
      this.isSearching = false;
    }
  }

  private async scanAndPair(): Promise<void> {
    if (this.isPairing || this.pairingClient === undefined) {
      return;
    }

    this.isPairing = true;
    this.pairingStatus = '正在打开扫码器…';
    this.pairingError = '';
    try {
      const context: common.Context =
        this.getUIContext().getHostContext() as common.Context;
      const result: scanBarcode.ScanResult =
        await scanBarcode.startScanForResult(context, {
          scanTypes: [scanCore.ScanType.QR_CODE],
          enableMultiMode: false,
          enableAlbum: false
        });
      this.pairingStatus = '正在校验电脑证书并建立安全连接…';
      const agentId: string =
        await this.pairingClient.pair(result.originalValue);
      this.pairingStatus = `已安全配对：${agentId}`;
    } catch (_error) {
      this.pairingStatus = '';
      this.pairingError =
        '配对失败或已取消。请确认二维码未过期，并确保手机与电脑在同一局域网。';
    } finally {
      this.isPairing = false;
    }
  }

  @Builder
  private agentCard(agent: DiscoveredAgent) {
    Column({ space: 8 }) {
      Row() {
        Column({ space: 4 }) {
          Text(agent.name)
            .fontSize(20)
            .fontWeight(FontWeight.Medium)
            .fontColor('#111827')
          Text(`${agent.address}:${agent.port}`)
            .fontSize(14)
            .fontColor('#6B7280')
        }
        .alignItems(HorizontalAlign.Start)
        .layoutWeight(1)

        Text(agent.pairingAllowed ? '可配对' : '已锁定')
          .fontSize(13)
          .fontColor(agent.pairingAllowed ? '#047857' : '#6B7280')
          .backgroundColor(agent.pairingAllowed ? '#D1FAE5' : '#E5E7EB')
          .borderRadius(12)
          .padding({ left: 10, right: 10, top: 5, bottom: 5 })
      }
      .width('100%')
    }
    .width('100%')
    .padding(18)
    .backgroundColor('#FFFFFF')
    .borderRadius(16)
    .shadow({ radius: 12, color: '#14000000', offsetX: 0, offsetY: 4 })
  }

  build() {
    Column({ space: 18 }) {
      Row() {
        Column({ space: 5 }) {
          Text('附近的电脑')
            .fontSize(30)
            .fontWeight(FontWeight.Bold)
            .fontColor('#111827')
          Text(this.isSearching ? '正在通过局域网安全发现…' : '发现已暂停')
            .fontSize(15)
            .fontColor('#6B7280')
        }
        .alignItems(HorizontalAlign.Start)
        .layoutWeight(1)

        Button(this.isSearching ? '暂停' : '扫描')
          .type(ButtonType.Capsule)
          .backgroundColor(this.isSearching ? '#E5E7EB' : '#2563EB')
          .fontColor(this.isSearching ? '#374151' : '#FFFFFF')
          .onClick((): void => {
            if (this.isSearching) {
              this.stopDiscovery();
            } else {
              this.startDiscovery();
            }
          })
      }
      .width('100%')

      if (this.discoveryError.length > 0) {
        Text(this.discoveryError)
          .width('100%')
          .fontSize(14)
          .fontColor('#B42318')
          .backgroundColor('#FEE4E2')
          .borderRadius(12)
          .padding(12)
      }

      Button(this.isPairing ? '正在安全配对…' : '扫描电脑上的配对二维码')
        .width('100%')
        .height(52)
        .type(ButtonType.Capsule)
        .backgroundColor('#2563EB')
        .fontColor('#FFFFFF')
        .enabled(!this.isPairing)
        .onClick((): void => {
          this.scanAndPair();
        })

      if (this.pairingStatus.length > 0) {
        Text(this.pairingStatus)
          .width('100%')
          .fontSize(14)
          .fontColor('#047857')
          .backgroundColor('#D1FAE5')
          .borderRadius(12)
          .padding(12)
      }

      if (this.pairingError.length > 0) {
        Text(this.pairingError)
          .width('100%')
          .fontSize(14)
          .fontColor('#B42318')
          .backgroundColor('#FEE4E2')
          .borderRadius(12)
          .padding(12)
      }

      if (this.agents.length === 0) {
        Column({ space: 12 }) {
          Text('⌁')
            .fontSize(48)
            .fontColor('#2563EB')
          Text(this.isSearching ? '尚未发现电脑' : '点击“扫描”开始发现')
            .fontSize(18)
            .fontWeight(FontWeight.Medium)
            .fontColor('#374151')
          Text('请确保手机和电脑连接到同一个局域网，并已启动 Windows Agent。')
            .fontSize(14)
            .fontColor('#6B7280')
            .textAlign(TextAlign.Center)
            .maxLines(3)
        }
        .layoutWeight(1)
        .justifyContent(FlexAlign.Center)
        .padding(30)
      } else {
        List({ space: 12 }) {
          ForEach(this.agents, (agent: DiscoveredAgent) => {
            ListItem() {
              this.agentCard(agent)
            }
          }, (agent: DiscoveredAgent): string => agent.id)
        }
        .width('100%')
        .layoutWeight(1)
        .edgeEffect(EdgeEffect.Spring)
      }
    }
    .width('100%')
    .height('100%')
    .padding({ left: 20, right: 20, top: 28, bottom: 18 })
    .backgroundColor('#F6F8FC')
  }
}

import { common } from '@kit.AbilityKit';
import { DiscoveredAgent } from '../discovery/DiscoveryContract';
import { MdnsDiscoveryRepository } from '../discovery/MdnsDiscoveryRepository';

@Entry
@Component
struct Index {
  @State private agents: Array<DiscoveredAgent> = [];
  @State private isSearching: boolean = false;
  private repository?: MdnsDiscoveryRepository;

  aboutToAppear(): void {
    const context: common.Context =
      this.getUIContext().getHostContext() as common.Context;
    this.repository = new MdnsDiscoveryRepository(
      context,
      (agents: Array<DiscoveredAgent>): void => {
        this.agents = agents;
      }
    );
    this.startDiscovery();
  }

  aboutToDisappear(): void {
    this.repository?.stop();
    this.isSearching = false;
  }

  private startDiscovery(): void {
    this.repository?.start();
    this.isSearching = this.repository?.isRunning() ?? false;
  }

  private stopDiscovery(): void {
    this.repository?.stop();
    this.isSearching = false;
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

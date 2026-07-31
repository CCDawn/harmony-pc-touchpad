import { common } from '@kit.AbilityKit';
import { scanBarcode, scanCore } from '@kit.ScanKit';
import { vibrator } from '@kit.SensorServiceKit';
import { TouchpadSettingsPanel } from '../components/TouchpadSettingsPanel';
import { DiscoveredAgent } from '../discovery/DiscoveryContract';
import { MdnsDiscoveryRepository } from '../discovery/MdnsDiscoveryRepository';
import {
  InputConnection,
  InputConnectionStatus
} from '../input/InputConnection';
import {
  TouchpadAction,
  TouchpadEventType,
  TouchpadGesture,
  TouchPoint
} from '../input/TouchpadGesture';
import { CredentialStore } from '../pairing/CredentialStore';
import { PairingClient } from '../pairing/PairingClient';
import {
  DEFAULT_TOUCHPAD_SETTINGS,
  TouchpadSettings
} from '../settings/TouchpadSettings';
import { TouchpadSettingsStore } from '../settings/TouchpadSettingsStore';

interface PairingFailure {
  code?: number;
  message?: string;
}

@Entry
@Component
struct Index {
  @State private agents: Array<DiscoveredAgent> = [];
  @State private isSearching: boolean = false;
  @State private discoveryError: string = '';
  @State private isPairing: boolean = false;
  @State private pairingStatus: string = '';
  @State private pairingError: string = '';
  @State private isConnecting: boolean = false;
  @State private isControlConnected: boolean = false;
  @State private connectionStatus: string = '';
  @State private connectionError: string = '';
  @State private showStatusOverlay: boolean = true;
  @State private showSettings: boolean = false;
  @State private dragSensitivity: number =
    DEFAULT_TOUCHPAD_SETTINGS.dragSensitivity;
  @State private scrollSpeed: number =
    DEFAULT_TOUCHPAD_SETTINGS.scrollSpeed;
  @State private naturalScroll: boolean =
    DEFAULT_TOUCHPAD_SETTINGS.naturalScroll;
  @State private hapticEnabled: boolean =
    DEFAULT_TOUCHPAD_SETTINGS.hapticEnabled;
  @State private hapticStrength: number =
    DEFAULT_TOUCHPAD_SETTINGS.hapticStrength;
  @State private hapticError: string = '';
  private repository?: MdnsDiscoveryRepository;
  private pairingClient?: PairingClient;
  private inputConnection?: InputConnection;
  private settingsStore?: TouchpadSettingsStore;
  private overlayTimerId: number = -1;
  private settingsSaveTimerId: number = -1;
  private pendingTapTimerId: number = -1;
  private readonly touchpadGesture: TouchpadGesture =
    new TouchpadGesture();

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
    this.settingsStore = new TouchpadSettingsStore(context);
    this.loadSettings();
    const credentialStore: CredentialStore = new CredentialStore(context);
    this.inputConnection = new InputConnection(
      credentialStore,
      (status: InputConnectionStatus): void => {
        this.isControlConnected = status.connected;
        this.connectionStatus = status.connected ? status.detail : '';
        this.connectionError = status.connected ? '' : status.detail;
        if (status.connected) {
          this.showStatusOverlay = true;
          this.scheduleOverlayHide();
        }
      }
    );
    this.startDiscovery();
    this.connectInput();
  }

  aboutToDisappear(): void {
    this.repository?.stop();
    this.isSearching = false;
    if (this.overlayTimerId >= 0) {
      clearTimeout(this.overlayTimerId);
      this.overlayTimerId = -1;
    }
    if (this.settingsSaveTimerId >= 0) {
      clearTimeout(this.settingsSaveTimerId);
      this.settingsSaveTimerId = -1;
    }
    this.clearPendingTapTimer();
    this.touchpadGesture.handleTouches(
      'cancel',
      [],
      [],
      Date.now()
    );
    this.inputConnection?.disconnect();
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
      await this.connectInput();
    } catch (error) {
      this.pairingStatus = '';
      const failure: PairingFailure = error as PairingFailure;
      const detail: string =
        typeof failure.message === 'string' ? failure.message.trim() : '';
      const code: string =
        typeof failure.code === 'number' ? `（错误码 ${failure.code}）` : '';
      this.pairingError = detail.length > 0 ?
        `配对失败${code}：${detail}` :
        '配对失败或已取消，请重新扫描。';
    } finally {
      this.isPairing = false;
    }
  }

  private async connectInput(): Promise<void> {
    if (this.isConnecting ||
      this.isControlConnected ||
      this.inputConnection === undefined) {
      return;
    }
    this.isConnecting = true;
    this.connectionError = '';
    this.connectionStatus = '正在建立安全控制连接…';
    try {
      await this.inputConnection.connect();
      this.repository?.stop();
      this.isSearching = false;
    } catch (error) {
      this.connectionStatus = '';
      const failure: PairingFailure = error as PairingFailure;
      const detail: string =
        typeof failure.message === 'string' ? failure.message.trim() : '';
      if (detail !== '尚未找到已配对的电脑，请先扫描配对二维码。') {
        this.connectionError = detail.length > 0 ?
          `连接失败：${detail}` :
          '连接电脑失败，请确认 Windows Agent 正在运行。';
      }
    } finally {
      this.isConnecting = false;
    }
  }

  private handleTouch(event: TouchEvent): void {
    if (this.showSettings) {
      event.stopPropagation();
      return;
    }
    const eventTimeMs: number = event.timestamp / 1000000;
    let type: TouchpadEventType;
    if (event.type === TouchType.Down) {
      type = 'down';
    } else if (event.type === TouchType.Move) {
      type = 'move';
    } else if (event.type === TouchType.Up) {
      type = 'up';
    } else {
      type = 'cancel';
    }
    if (type === 'down') {
      this.clearPendingTapTimer();
    }

    const activeTouches: Array<TouchPoint> = event.touches
      .filter((touch: TouchObject): boolean =>
        touch.type !== TouchType.Up &&
        touch.type !== TouchType.Cancel)
      .map((touch: TouchObject): TouchPoint => ({
        id: touch.id,
        x: touch.x,
        y: touch.y
      }));
    const changedTouches: Array<TouchPoint> = event.changedTouches
      .map((touch: TouchObject): TouchPoint => ({
        id: touch.id,
        x: touch.x,
        y: touch.y
      }));
    const actions: Array<TouchpadAction> =
      this.touchpadGesture.handleTouches(
        type,
        activeTouches,
        changedTouches,
        eventTimeMs
      );
    this.dispatchTouchActions(actions);
    if (this.touchpadGesture.hasPendingTap()) {
      this.schedulePendingTapFlush();
    } else {
      this.clearPendingTapTimer();
    }
    if (type === 'down') {
      this.showStatusOverlay = false;
    }
    event.stopPropagation();
  }

  private dispatchTouchActions(actions: Array<TouchpadAction>): void {
    actions.forEach((action: TouchpadAction): void => {
      if (action.kind === 'move') {
        this.inputConnection?.move(
          action.dx,
          action.dy,
          action.velocity
        );
      } else if (action.kind === 'click') {
        this.inputConnection?.click(action.button);
        this.playClickHaptic(false);
      } else if (action.kind === 'button') {
        this.inputConnection?.button(action.button, action.isDown);
        if (action.isDown) {
          this.playClickHaptic(true);
        }
      } else if (action.kind === 'scroll') {
        this.inputConnection?.scroll(
          action.dx,
          action.dy,
          action.phase
        );
      } else {
        this.inputConnection?.releaseAll();
      }
    });
  }

  private schedulePendingTapFlush(): void {
    this.clearPendingTapTimer();
    this.pendingTapTimerId = setTimeout((): void => {
      this.pendingTapTimerId = -1;
      this.dispatchTouchActions(this.touchpadGesture.flushPendingTap());
    }, this.touchpadGesture.pendingTapTimeoutMs() + 40);
  }

  private clearPendingTapTimer(): void {
    if (this.pendingTapTimerId >= 0) {
      clearTimeout(this.pendingTapTimerId);
      this.pendingTapTimerId = -1;
    }
  }

  private scheduleOverlayHide(): void {
    if (this.overlayTimerId >= 0) {
      clearTimeout(this.overlayTimerId);
    }
    this.overlayTimerId = setTimeout((): void => {
      this.showStatusOverlay = false;
      this.overlayTimerId = -1;
    }, 2400);
  }

  private async loadSettings(): Promise<void> {
    try {
      const settings: TouchpadSettings =
        await this.settingsStore?.load() ?? DEFAULT_TOUCHPAD_SETTINGS;
      this.dragSensitivity = settings.dragSensitivity;
      this.scrollSpeed = settings.scrollSpeed;
      this.naturalScroll = settings.naturalScroll;
      this.hapticEnabled = settings.hapticEnabled;
      this.hapticStrength = settings.hapticStrength;
      this.applyGestureSettings();
    } catch (_error) {
      this.applyGestureSettings();
    }
  }

  private applyGestureSettings(): void {
    this.touchpadGesture.updateSettings({
      dragSensitivity: this.dragSensitivity,
      scrollSpeed: this.scrollSpeed,
      naturalScroll: this.naturalScroll
    });
  }

  private scheduleSettingsSave(): void {
    this.applyGestureSettings();
    if (this.settingsSaveTimerId >= 0) {
      clearTimeout(this.settingsSaveTimerId);
    }
    this.settingsSaveTimerId = setTimeout((): void => {
      this.settingsSaveTimerId = -1;
      this.settingsStore?.save({
        dragSensitivity: this.dragSensitivity,
        scrollSpeed: this.scrollSpeed,
        naturalScroll: this.naturalScroll,
        hapticEnabled: this.hapticEnabled,
        hapticStrength: this.hapticStrength
      }).catch((_error: Error): void => {
      });
    }, 250);
  }

  private playClickHaptic(isDragLock: boolean): void {
    if (!this.hapticEnabled) {
      return;
    }
    const baseDuration: number =
      this.hapticStrength === 1 ? 30 :
        (this.hapticStrength === 2 ? 50 : 70);
    const effect: vibrator.VibrateTime = {
      type: 'time',
      duration: baseDuration + (isDragLock ? 12 : 0)
    };
    const attribute: vibrator.VibrateAttribute = {
      usage: 'touch'
    };
    vibrator.startVibration(effect, attribute)
      .then((): void => {
        this.hapticError = '';
      })
      .catch((error: Error): void => {
        this.hapticError = '触感反馈不可用，请检查系统震动开关和应用权限。';
        console.error(`[HAPTIC] startVibration failed: ${error.message}`);
      });
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
    Stack() {
      Column({ space: 18 }) {
      Row() {
        Column({ space: 5 }) {
          Text(this.isControlConnected ? 'Harmony 触控板' : '附近的电脑')
            .fontSize(30)
            .fontWeight(FontWeight.Bold)
            .fontColor('#111827')
          Text(this.isControlConnected ?
            this.connectionStatus :
            (this.isSearching ? '正在通过局域网安全发现…' : '发现已暂停'))
            .fontSize(15)
            .fontColor('#6B7280')
        }
        .alignItems(HorizontalAlign.Start)
        .layoutWeight(1)

        if (!this.isControlConnected) {
          Button(this.isConnecting ? '连接中' :
            (this.isSearching ? '暂停' : '扫描'))
            .type(ButtonType.Capsule)
            .backgroundColor(this.isSearching ? '#E5E7EB' : '#2563EB')
            .fontColor(this.isSearching ? '#374151' : '#FFFFFF')
            .enabled(!this.isConnecting)
            .onClick((): void => {
              if (this.isSearching) {
                this.stopDiscovery();
              } else {
                this.startDiscovery();
              }
            })
        }
      }
      .width('100%')

      if (this.isControlConnected) {
        Column({ space: 16 }) {
          Column({ space: 10 }) {
            Text('触摸区域')
              .fontSize(22)
              .fontWeight(FontWeight.Medium)
              .fontColor('#FFFFFF')
            Text('一指滑动移动光标 · 轻点执行左键单击')
              .fontSize(14)
              .fontColor('#CCFFFFFF')
          }
          .alignItems(HorizontalAlign.Start)

          Text('⌁')
            .fontSize(64)
            .fontColor('#99FFFFFF')
            .layoutWeight(1)
        }
        .width('100%')
        .layoutWeight(1)
        .padding(24)
        .backgroundColor('#1F2937')
        .borderRadius(24)
        .justifyContent(FlexAlign.Center)
        .onTouch((event: TouchEvent): void => {
          this.handleTouch(event);
        })

        Row({ space: 8 }) {
          Text('●')
            .fontSize(12)
            .fontColor('#10B981')
          Text('安全控制已连接')
            .fontSize(14)
            .fontColor('#047857')
        }
        .width('100%')
        .justifyContent(FlexAlign.Center)
        .padding(12)
        .backgroundColor('#D1FAE5')
        .borderRadius(12)
      } else if (this.discoveryError.length > 0) {
        Text(this.discoveryError)
          .width('100%')
          .fontSize(14)
          .fontColor('#B42318')
          .backgroundColor('#FEE4E2')
          .borderRadius(12)
          .padding(12)
      }

      if (!this.isControlConnected) {
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

        Button(this.isConnecting ? '正在连接电脑…' : '连接已配对的电脑')
          .width('100%')
          .height(48)
          .type(ButtonType.Capsule)
          .backgroundColor('#E5E7EB')
          .fontColor('#374151')
          .enabled(!this.isConnecting)
          .onClick((): void => {
            this.connectInput();
          })
      }

      if (!this.isControlConnected && this.pairingStatus.length > 0) {
        Text(this.pairingStatus)
          .width('100%')
          .fontSize(14)
          .fontColor('#047857')
          .backgroundColor('#D1FAE5')
          .borderRadius(12)
          .padding(12)
      }

      if (!this.isControlConnected && this.pairingError.length > 0) {
        Text(this.pairingError)
          .width('100%')
          .fontSize(14)
          .fontColor('#B42318')
          .backgroundColor('#FEE4E2')
          .borderRadius(12)
          .padding(12)
      }

      if (!this.isControlConnected && this.connectionError.length > 0) {
        Text(this.connectionError)
          .width('100%')
          .fontSize(14)
          .fontColor('#B42318')
          .backgroundColor('#FEE4E2')
          .borderRadius(12)
          .padding(12)
      }

      if (!this.isControlConnected && this.agents.length === 0) {
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
      } else if (!this.isControlConnected) {
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

      if (this.isControlConnected) {
        Stack({ alignContent: Alignment.Top }) {
          Column() {
            Text('触摸板')
              .fontSize(16)
              .fontWeight(FontWeight.Medium)
              .fontColor('#14FFFFFF')
          }
          .width('100%')
          .height('100%')
          .justifyContent(FlexAlign.Center)
          .backgroundColor('#151B26')
          .borderRadius(20)
          .onTouch((event: TouchEvent): void => {
            this.handleTouch(event);
          })

          if (this.showStatusOverlay) {
            Row({ space: 10 }) {
              Text('●')
                .fontSize(11)
                .fontColor('#34D399')
              Text('一指移动/轻点 · 双击按住拖拽 · 两指滚动/右键')
                .fontSize(14)
                .fontColor('#F9FAFB')
            }
            .margin({ top: 18 })
            .padding({
              left: 18,
              right: 18,
              top: 10,
              bottom: 10
            })
            .backgroundColor('#CC111827')
            .borderRadius(22)
          }

          Row() {
            Button(this.showSettings ? '关闭设置' : '设置')
              .type(ButtonType.Capsule)
              .fontSize(14)
              .fontColor('#E5E7EB')
              .backgroundColor('#991F2937')
              .onClick((): void => {
                this.showSettings = !this.showSettings;
              })
          }
          .width('100%')
          .justifyContent(FlexAlign.End)
          .padding({ top: 18, right: 18 })

          if (this.showSettings) {
            Column() {
              TouchpadSettingsPanel({
                dragSensitivity: this.dragSensitivity,
                scrollSpeed: this.scrollSpeed,
                naturalScroll: this.naturalScroll,
                hapticEnabled: this.hapticEnabled,
                hapticStrength: this.hapticStrength,
                hapticError: this.hapticError,
                onSettingsChanged: (): void => {
                  this.scheduleSettingsSave();
                },
                onTestHaptic: (): void => {
                  this.playClickHaptic(false);
                },
                onClose: (): void => {
                  this.showSettings = false;
                }
              })
            }
              .width('100%')
              .height('100%')
              .alignItems(HorizontalAlign.End)
              .margin({ top: 74, right: 18 })
          }
        }
        .width('100%')
        .height('100%')
        .padding(24)
        .backgroundColor('#090D14')
      }
    }
    .width('100%')
    .height('100%')
    .backgroundColor('#090D14')
  }
}

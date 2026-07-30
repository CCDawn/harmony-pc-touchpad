@Component
export struct TouchpadSettingsPanel {
  @Link dragSensitivity: number;
  @Link scrollSpeed: number;
  @Link naturalScroll: boolean;
  @Link hapticEnabled: boolean;
  @Link hapticStrength: number;
  onSettingsChanged: () => void = (): void => {
  };
  onTestHaptic: () => void = (): void => {
  };
  onClose: () => void = (): void => {
  };

  build() {
    Column({ space: 18 }) {
      Row() {
        Text('触摸板设置')
          .fontSize(22)
          .fontWeight(FontWeight.Bold)
          .fontColor('#F9FAFB')
          .layoutWeight(1)
        Button('完成')
          .type(ButtonType.Capsule)
          .fontSize(14)
          .fontColor('#FFFFFF')
          .backgroundColor('#2563EB')
          .onClick(this.onClose)
      }
      .width('100%')

      Column({ space: 8 }) {
        Row() {
          Text('双击拖拽灵敏度')
            .fontSize(16)
            .fontColor('#F3F4F6')
            .layoutWeight(1)
          Text(`${Math.round(this.dragSensitivity * 100)}%`)
            .fontSize(14)
            .fontColor('#93C5FD')
        }
        .width('100%')
        Slider({
          value: this.dragSensitivity,
          min: 0.75,
          max: 2,
          step: 0.05
        })
          .selectedColor('#3B82F6')
          .trackColor('#374151')
          .onChange((value: number): void => {
            this.dragSensitivity = value;
            this.onSettingsChanged();
          })
      }
      .width('100%')

      Column({ space: 8 }) {
        Row() {
          Text('双指滚动速度')
            .fontSize(16)
            .fontColor('#F3F4F6')
            .layoutWeight(1)
          Text(`${this.scrollSpeed.toFixed(1)}×`)
            .fontSize(14)
            .fontColor('#93C5FD')
        }
        .width('100%')
        Slider({
          value: this.scrollSpeed,
          min: 0.5,
          max: 4,
          step: 0.1
        })
          .selectedColor('#3B82F6')
          .trackColor('#374151')
          .onChange((value: number): void => {
            this.scrollSpeed = value;
            this.onSettingsChanged();
          })
      }
      .width('100%')

      Row() {
        Column({ space: 3 }) {
          Text('自然滚动')
            .fontSize(16)
            .fontColor('#F3F4F6')
          Text('手指移动方向与页面内容一致')
            .fontSize(12)
            .fontColor('#9CA3AF')
        }
        .alignItems(HorizontalAlign.Start)
        .layoutWeight(1)
        Toggle({
          type: ToggleType.Switch,
          isOn: this.naturalScroll
        })
          .selectedColor('#3B82F6')
          .onChange((isOn: boolean): void => {
            this.naturalScroll = isOn;
            this.onSettingsChanged();
          })
      }
      .width('100%')

      Row() {
        Text('触控反馈')
          .fontSize(16)
          .fontColor('#F3F4F6')
          .layoutWeight(1)
        Toggle({
          type: ToggleType.Switch,
          isOn: this.hapticEnabled
        })
          .selectedColor('#3B82F6')
          .onChange((isOn: boolean): void => {
            this.hapticEnabled = isOn;
            this.onSettingsChanged();
          })
      }
      .width('100%')

      Column({ space: 8 }) {
        Row() {
          Text('反馈力度')
            .fontSize(16)
            .fontColor('#F3F4F6')
            .layoutWeight(1)
          Text(this.hapticStrength === 1 ? '轻' :
            (this.hapticStrength === 2 ? '中' : '强'))
            .fontSize(14)
            .fontColor('#93C5FD')
        }
        .width('100%')
        Slider({
          value: this.hapticStrength,
          min: 1,
          max: 3,
          step: 1
        })
          .enabled(this.hapticEnabled)
          .selectedColor('#3B82F6')
          .trackColor('#374151')
          .onChange((value: number): void => {
            this.hapticStrength = Math.round(value);
            this.onSettingsChanged();
          })
      }
      .width('100%')

      Button('测试震动')
        .type(ButtonType.Capsule)
        .width('100%')
        .fontColor('#E5E7EB')
        .backgroundColor('#374151')
        .enabled(this.hapticEnabled)
        .onClick(this.onTestHaptic)
    }
    .width(390)
    .padding(24)
    .backgroundColor('#F51F2937')
    .borderRadius(22)
    .shadow({
      radius: 28,
      color: '#66000000',
      offsetX: 0,
      offsetY: 8
    })
  }
}

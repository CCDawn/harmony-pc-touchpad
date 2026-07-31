import { AbilityConstant, UIAbility, Want } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';

export default class EntryAbility extends UIAbility {
  onCreate(_want: Want, _launchParam: AbilityConstant.LaunchParam): void {
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    this.configureAndLoad(windowStage);
  }

  private async configureAndLoad(
    windowStage: window.WindowStage
  ): Promise<void> {
    try {
      const mainWindow: window.Window =
        await windowStage.getMainWindow();
      await mainWindow.setPreferredOrientation(
        window.Orientation.LANDSCAPE
      );
      await mainWindow.setWindowLayoutFullScreen(true);
      const enabledBars: Array<'status' | 'navigation'> = [];
      await mainWindow.setWindowSystemBarEnable(enabledBars);
    } catch (_error) {
    }
    await windowStage.loadContent('pages/Index')
      .catch((_error: Error): void => {
      });
  }
}

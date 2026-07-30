using HarmonyPcTouchpad.Agent.Core;

namespace HarmonyPcTouchpad.Agent.App;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly IInputSink _inputSink;
    private readonly NotifyIcon _trayIcon;

    public AgentApplicationContext(IInputSink inputSink)
    {
        _inputSink = inputSink;

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Harmony PC Touchpad Agent（传输服务尚未启用）",
            ContextMenuStrip = new ContextMenuStrip
            {
                Items = { exitItem }
            },
            Visible = true
        };
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        try
        {
            _inputSink.ReleaseAll();
        }
        finally
        {
            base.ExitThreadCore();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}

using System.Net;
using HarmonyPcTouchpad.Agent.Core;
using HarmonyPcTouchpad.Agent.Transport;
using HarmonyPcTouchpad.Agent.Windows;

namespace HarmonyPcTouchpad.Agent.App;

internal sealed class AgentApplicationContext : ApplicationContext
{
    private readonly IInputSink _inputSink;
    private readonly AgentWebSocketHost _host;
    private readonly WindowsMdnsAdvertiser _advertiser;
    private readonly Func<string> _createPairingPayload;
    private readonly NotifyIcon _trayIcon;
    private bool _advertiserDisposed;
    private bool _hostDisposed;

    public AgentApplicationContext(
        IInputSink inputSink,
        AgentWebSocketHost host,
        WindowsMdnsAdvertiser advertiser,
        IReadOnlyList<IPAddress> listenAddresses,
        Func<string> createPairingPayload)
    {
        _inputSink = inputSink ?? throw new ArgumentNullException(nameof(inputSink));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _advertiser = advertiser ??
            throw new ArgumentNullException(nameof(advertiser));
        _createPairingPayload = createPairingPayload ??
            throw new ArgumentNullException(nameof(createPairingPayload));
        ArgumentNullException.ThrowIfNull(listenAddresses);

        var pairingItem = new ToolStripMenuItem("复制配对信息");
        pairingItem.Click += (_, _) => CopyPairingPayload();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Harmony PC Touchpad Agent（WSS 已启用）",
            ContextMenuStrip = new ContextMenuStrip
            {
                Items = { pairingItem, new ToolStripSeparator(), exitItem }
            },
            Visible = true
        };
        _trayIcon.ShowBalloonTip(
            3000,
            "Harmony PC Touchpad Agent",
            $"WSS 正在监听 {listenAddresses.Count} 个私有网络地址。",
            ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        try
        {
            StopInfrastructure();
        }
        finally
        {
            try
            {
                _inputSink.ReleaseAll();
            }
            finally
            {
                DisposeHost();
                base.ExitThreadCore();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            try
            {
                DisposeAdvertiser();
            }
            finally
            {
                DisposeHost();
            }
        }

        base.Dispose(disposing);
    }

    private void CopyPairingPayload()
    {
        try
        {
            Clipboard.SetText(_createPairingPayload());
            _trayIcon.ShowBalloonTip(
                3000,
                "配对信息已复制",
                "一次性配对信息将在 2 分钟后失效。",
                ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"无法生成配对信息：{error.Message}",
                "Harmony PC Touchpad Agent",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DisposeHost()
    {
        if (_hostDisposed)
        {
            return;
        }

        _host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _hostDisposed = true;
    }

    private void DisposeAdvertiser()
    {
        if (_advertiserDisposed)
        {
            return;
        }

        _advertiser.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _advertiserDisposed = true;
    }

    private void StopInfrastructure()
    {
        try
        {
            DisposeAdvertiser();
        }
        finally
        {
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _host.StopAsync(timeout.Token).GetAwaiter().GetResult();
        }
    }
}

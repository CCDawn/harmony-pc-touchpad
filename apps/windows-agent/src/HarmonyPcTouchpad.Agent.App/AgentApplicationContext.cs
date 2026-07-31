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
    private readonly Func<PairingDisplayContent> _createPairingContent;
    private readonly NotifyIcon _trayIcon;
    private readonly Control _dispatcher;
    private PairingForm? _pairingForm;
    private bool _advertiserDisposed;
    private bool _hostDisposed;

    public AgentApplicationContext(
        IInputSink inputSink,
        AgentWebSocketHost host,
        WindowsMdnsAdvertiser advertiser,
        IReadOnlyList<IPAddress> listenAddresses,
        Func<PairingDisplayContent> createPairingContent,
        bool showPairingOnStartup = false)
    {
        _inputSink = inputSink ?? throw new ArgumentNullException(nameof(inputSink));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _advertiser = advertiser ??
            throw new ArgumentNullException(nameof(advertiser));
        _createPairingContent = createPairingContent ??
            throw new ArgumentNullException(nameof(createPairingContent));
        ArgumentNullException.ThrowIfNull(listenAddresses);
        _dispatcher = new Control();
        _dispatcher.CreateControl();

        var pairingItem = new ToolStripMenuItem("显示配对二维码");
        pairingItem.Click += (_, _) => ShowPairingCode();

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

        if (showPairingOnStartup)
        {
            ShowPairingCode();
        }
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
            _pairingForm?.Dispose();
            _dispatcher.Dispose();
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

    public void RequestShowPairingCode()
    {
        if (_dispatcher.IsDisposed)
        {
            return;
        }

        _dispatcher.BeginInvoke(ShowPairingCode);
    }

    private void ShowPairingCode()
    {
        try
        {
            if (_pairingForm is null || _pairingForm.IsDisposed)
            {
                _pairingForm = new PairingForm(_createPairingContent);
            }

            _pairingForm.Show();
            _pairingForm.Activate();
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"无法显示配对二维码：{error.Message}",
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

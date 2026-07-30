namespace HarmonyPcTouchpad.Agent.App;

internal sealed record PairingDisplayContent(
    string Payload,
    DateTimeOffset ExpiresAt);

internal sealed class PairingForm : Form
{
    private const int PixelsPerModule = 6;

    private readonly Func<PairingDisplayContent> _createContent;
    private readonly PictureBox _qrImage;
    private readonly Label _status;
    private readonly Button _copyButton;
    private readonly System.Windows.Forms.Timer _timer;
    private PairingDisplayContent? _content;

    public PairingForm(Func<PairingDisplayContent> createContent)
    {
        _createContent = createContent ??
            throw new ArgumentNullException(nameof(createContent));

        Text = "连接 Harmony 设备";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 650);
        BackColor = Color.White;

        var title = new Label
        {
            Text = "用 Harmony PC Touchpad 扫描",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        var explanation = new Label
        {
            Text = "二维码包含一次性配对令牌和本机证书指纹，2 分钟后自动失效。",
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.DimGray,
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 0, 0, 14)
        };
        _qrImage = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Width = 440,
            Height = 440,
            BackColor = Color.White,
            Margin = new Padding(0)
        };
        _status = new Label
        {
            TextAlign = ContentAlignment.MiddleCenter,
            Font = SystemFonts.MessageBoxFont,
            ForeColor = Color.FromArgb(4, 120, 87),
            AutoSize = false,
            Width = 440,
            Height = 32
        };
        _copyButton = new Button
        {
            Text = "复制配对信息",
            AutoSize = true
        };
        _copyButton.Click += (_, _) => CopyPayload();
        var refreshButton = new Button
        {
            Text = "刷新二维码",
            AutoSize = true
        };
        refreshButton.Click += (_, _) => RefreshContent();
        var closeButton = new Button
        {
            Text = "关闭",
            AutoSize = true
        };
        closeButton.Click += (_, _) => Close();

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttonRow.Controls.Add(_copyButton);
        buttonRow.Controls.Add(refreshButton);
        buttonRow.Controls.Add(closeButton);

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(30, 24, 30, 18)
        };
        layout.Controls.Add(title);
        layout.Controls.Add(explanation);
        layout.Controls.Add(_qrImage);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttonRow);
        Controls.Add(layout);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 250
        };
        _timer.Tick += (_, _) => UpdateStatus();
        Shown += (_, _) => RefreshContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            Image? image = _qrImage.Image;
            _qrImage.Image = null;
            image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefreshContent()
    {
        try
        {
            PairingDisplayContent content = _createContent();
            byte[] png = PairingQrImageRenderer.RenderPng(
                content.Payload,
                PixelsPerModule);
            using var stream = new MemoryStream(png, writable: false);
            using Image decoded = Image.FromStream(stream);
            var image = new Bitmap(decoded);
            Image? previous = _qrImage.Image;
            _qrImage.Image = image;
            previous?.Dispose();
            _content = content;
            _copyButton.Enabled = true;
            _timer.Start();
            UpdateStatus();
        }
        catch (Exception error)
        {
            _timer.Stop();
            _copyButton.Enabled = false;
            _status.ForeColor = Color.Firebrick;
            _status.Text = $"无法生成二维码：{error.Message}";
        }
    }

    private void CopyPayload()
    {
        if (_content is null ||
            DateTimeOffset.UtcNow >= _content.ExpiresAt)
        {
            return;
        }

        Clipboard.SetText(_content.Payload);
        _status.Text = "配对信息已复制；请勿发送给其他人。";
    }

    private void UpdateStatus()
    {
        if (_content is null)
        {
            return;
        }

        TimeSpan remaining = _content.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
            _copyButton.Enabled = false;
            _status.ForeColor = Color.Firebrick;
            _status.Text = "二维码已失效，请点击“刷新二维码”。";
            return;
        }

        _status.ForeColor = Color.FromArgb(4, 120, 87);
        int seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        _status.Text = $"二维码将在 {seconds} 秒后失效";
    }
}

using Microsoft.Win32;

namespace PhoneAudioReceiver;

sealed class MainForm : Form
{
    const int ReceivingHoldMs = 1500;

    readonly Settings settings = Settings.Load();
    readonly AudioEndpoints endpoints = new();
    readonly ConnectionManager link;
    readonly RegisteredWaitHandle showWait;
    readonly System.Windows.Forms.Timer meterTimer = new() { Interval = 50 };

    readonly NotifyIcon tray;
    readonly ToolStripMenuItem trayToggle;
    readonly PictureBox logo;
    readonly Label statusLabel;
    readonly ComboBox deviceBox;
    readonly Label outputLabel;
    readonly LevelBar level;
    readonly Button connectButton;
    readonly CheckBox startupBox;
    readonly CheckBox autoBox;

    bool startHidden;
    bool exiting;
    bool syncingDevices;
    long lastSoundTick = long.MinValue / 2;
    int meterTicks;
    Indicator shownIndicator = (Indicator)(-1);
    LinkState lastState = (LinkState)(-1);
    string? outputName;

    public MainForm(EventWaitHandle showSignal, bool startHidden)
    {
        this.startHidden = startHidden;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = SystemFonts.MessageBoxFont ?? Font;
        Text = "Phone Audio Receiver";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Icon = AppIcons.Get(Indicator.Connected);

        logo = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 10, 0) };
        var title = new Label
        {
            Text = "Phone Audio Receiver",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Anchor = AnchorStyles.Left,
        };
        var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
        header.Controls.Add(logo);
        header.Controls.Add(title);

        statusLabel = ValueLabel();
        statusLabel.Font = new Font(Font, FontStyle.Bold);
        deviceBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(3, 3, 3, 6) };
        outputLabel = ValueLabel();
        level = new LevelBar { Size = new Size(260, 8), Margin = new Padding(3, 9, 3, 9) };
        connectButton = new Button { Text = "Connect", AutoSize = true, MinimumSize = new Size(120, 32), Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 10) };

        startupBox = new CheckBox { Text = "Start with Windows (minimized to tray)", AutoSize = true, Checked = Startup.IsEnabled };
        autoBox = new CheckBox { Text = "Connect and reconnect automatically", AutoSize = true, Checked = settings.AutoConnect };
        var settingsPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 4) };
        settingsPanel.Controls.Add(startupBox);
        settingsPanel.Controls.Add(autoBox);
        var settingsGroup = new GroupBox { Text = "Settings", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(6) };
        settingsGroup.Controls.Add(settingsPanel);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(header, 0, 0);
        grid.SetColumnSpan(header, 2);
        AddRow(grid, 1, "Status", statusLabel);
        AddRow(grid, 2, "Phone", deviceBox);
        AddRow(grid, 3, "Output", outputLabel);
        AddRow(grid, 4, "Level", level);
        grid.Controls.Add(connectButton, 0, 5);
        grid.SetColumnSpan(connectButton, 2);
        grid.Controls.Add(settingsGroup, 0, 6);
        grid.SetColumnSpan(settingsGroup, 2);
        Controls.Add(grid);

        trayToggle = new ToolStripMenuItem("Connect", null, (_, _) => ToggleConnection());
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => ShowFromTray());
        trayMenu.Items.Add(trayToggle);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApp());
        tray = new NotifyIcon { ContextMenuStrip = trayMenu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };

        link = new ConnectionManager(SynchronizationContext.Current!, settings.DeviceId, settings.AutoConnect);
        link.Changed += UpdateUi;

        connectButton.Click += (_, _) => ToggleConnection();
        deviceBox.SelectedIndexChanged += (_, _) =>
        {
            if (!syncingDevices && deviceBox.SelectedItem is DeviceItem item) link.Select(item.Device.Id);
        };
        if (startupBox.Checked) Startup.Set(true);
        startupBox.CheckedChanged += (_, _) => Startup.Set(startupBox.Checked);
        autoBox.CheckedChanged += (_, _) =>
        {
            settings.AutoConnect = link.AutoConnect = autoBox.Checked;
            settings.Save();
        };
        meterTimer.Tick += (_, _) => OnMeterTick();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => BeginInvoke(ShowFromTray), null, Timeout.Infinite, executeOnlyOnce: false);

        UpdateUi();
    }

    static Label ValueLabel() => new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };

    static void AddRow(TableLayoutPanel grid, int row, string caption, Control value)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 16, 6),
        };
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    void ToggleConnection()
    {
        if (link.IsActive) link.Disconnect();
        else link.Connect();
    }

    void UpdateUi()
    {
        SyncDeviceList();

        var state = link.State;
        if (state != lastState)
        {
            lastState = state;
            endpoints.ResetMeter();
        }
        bool receiving = state == LinkState.Connected && Environment.TickCount64 - lastSoundTick < ReceivingHoldMs;
        var indicator = receiving ? Indicator.Receiving : state switch
        {
            LinkState.Connected => Indicator.Connected,
            LinkState.Connecting or LinkState.Reconnecting or LinkState.Searching => Indicator.Busy,
            LinkState.Error => Indicator.Error,
            _ => Indicator.Idle,
        };

        string text = receiving ? "Receiving audio" : state switch
        {
            LinkState.Searching => "Searching…",
            LinkState.Connecting => "Connecting…",
            LinkState.Connected => "Connected",
            LinkState.Reconnecting => "Reconnecting…",
            LinkState.Error => "Error",
            _ => link.Devices.Count == 0 ? "No paired phone found" : "Disconnected",
        };
        if (state is LinkState.Reconnecting or LinkState.Error && link.ErrorText != null)
            text += $" ({link.ErrorText})";

        statusLabel.Text = text;
        statusLabel.ForeColor = AppIcons.ColorOf(indicator);

        bool active = link.IsActive;
        connectButton.Text = trayToggle.Text = active ? "Disconnect" : "Connect";
        connectButton.Enabled = trayToggle.Enabled = active || link.Target != null;
        deviceBox.Enabled = link.Devices.Count > 1;

        if (indicator != shownIndicator)
        {
            shownIndicator = indicator;
            tray.Icon = AppIcons.Get(indicator);
            logo.Image?.Dispose();
            logo.Image = AppIcons.Draw(AppIcons.ColorOf(indicator), 80);
        }
        string tip = link.Target is { } t && state == LinkState.Connected ? $"{t.Name}: {text}" : text;
        tray.Text = Truncate($"Phone Audio Receiver: {tip}", 63);

        if (state == LinkState.Connected && link.Target is { } target && settings.DeviceId != target.Id)
        {
            settings.DeviceId = target.Id;
            settings.Save();
        }

        meterTimer.Enabled = Visible && state == LinkState.Connected;
        if (!meterTimer.Enabled) level.Value = 0;
    }

    void SyncDeviceList()
    {
        var target = link.Target;
        var current = deviceBox.Items.Cast<DeviceItem>().Select(i => i.Device).ToList();
        bool same = current.SequenceEqual(link.Devices) && (deviceBox.SelectedItem as DeviceItem)?.Device == target;
        if (same) return;

        syncingDevices = true;
        deviceBox.BeginUpdate();
        deviceBox.Items.Clear();
        foreach (var device in link.Devices)
        {
            int index = deviceBox.Items.Add(new DeviceItem(device));
            if (device == target) deviceBox.SelectedIndex = index;
        }
        deviceBox.EndUpdate();
        syncingDevices = false;
    }

    void OnMeterTick()
    {
        if (meterTicks++ % 20 == 0) RefreshOutputName();

        float peak = link.Target is { } t ? endpoints.PhonePeak(t.Name) : 0f;
        bool wasReceiving = Environment.TickCount64 - lastSoundTick < ReceivingHoldMs;
        if (peak > 0.0005f) lastSoundTick = Environment.TickCount64;
        bool isReceiving = Environment.TickCount64 - lastSoundTick < ReceivingHoldMs;

        level.Value = Math.Max(LevelBar.ToDisplay(peak), level.Value * 0.8f);
        if (wasReceiving != isReceiving) UpdateUi();
    }

    void RefreshOutputName()
    {
        string name = endpoints.DefaultOutputName();
        if (name == outputName) return;
        outputName = name;
        outputLabel.Text = Truncate(name, 40);
        endpoints.ResetMeter();
    }

    void ShowFromTray()
    {
        startHidden = false;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) link.Resume();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden && value && !IsHandleCreated)
        {
            CreateHandle();
            value = false;
        }
        base.SetVisibleCore(value);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) RefreshOutputName();
        UpdateUi();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            showWait.Unregister(null);
            meterTimer.Dispose();
            link.Dispose();
            endpoints.Dispose();
            tray.Visible = false;
            tray.Dispose();
        }
        base.Dispose(disposing);
    }

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    sealed record DeviceItem(AudioDevice Device)
    {
        public override string ToString() => Device.Name;
    }
}

sealed class LevelBar : Control
{
    float value;

    public LevelBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    public float Value
    {
        get => value;
        set
        {
            float v = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(v - this.value) < 0.004f) return;
            this.value = v;
            Invalidate();
        }
    }

    // Peak amplitude to a 0..1 bar position on a -60..0 dB scale.
    public static float ToDisplay(float peak) =>
        peak <= 0.001f ? 0f : Math.Clamp((20f * MathF.Log10(peak) + 60f) / 60f, 0f, 1f);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.ControlLight);
        int width = (int)(Width * value);
        if (width <= 0) return;
        using var brush = new SolidBrush(AppIcons.ColorOf(Indicator.Receiving));
        e.Graphics.FillRectangle(brush, 0, 0, width, Height);
    }
}

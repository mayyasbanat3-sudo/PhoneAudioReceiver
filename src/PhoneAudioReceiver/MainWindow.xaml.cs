using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PhoneAudioReceiver;

public partial class MainWindow : Window
{
    const int ReceivingHoldMs = 1500;

    readonly Settings settings;
    readonly AudioEndpoints endpoints = new();
    readonly ConnectionManager link;
    readonly Tray tray = new();
    readonly RegisteredWaitHandle showWait;
    readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly Dictionary<Indicator, SolidColorBrush> brushes = new();

    bool exiting;
    long lastSoundTick = long.MinValue / 2;
    int meterTicks;
    double level;
    LinkState lastState = (LinkState)(-1);
    string? outputName;

    internal MainWindow(Settings settings, EventWaitHandle showSignal)
    {
        this.settings = settings;
        InitializeComponent();

        StartupSwitch.IsChecked = Startup.IsEnabled;
        if (Startup.IsEnabled) Startup.Set(true);
        AutoSwitch.IsChecked = settings.AutoConnect;

        StartupSwitch.Click += (_, _) => Startup.Set(StartupSwitch.IsChecked == true);
        AutoSwitch.Click += (_, _) =>
        {
            settings.AutoConnect = link!.AutoConnect = AutoSwitch.IsChecked == true;
            settings.Save();
        };
        ConnectButton.Click += (_, _) => ToggleConnection();
        PhoneButton.Click += (_, _) => ShowDevicePicker();
        ThemeButton.Click += (_, _) => ToggleTheme();
        MinimizeButton.Click += (_, _) => Hide();
        CloseButton.Click += (_, _) => Hide();
        meterTimer.Tick += (_, _) => OnMeterTick();

        tray.OpenRequested += ShowFromTray;
        tray.ToggleRequested += ToggleConnection;
        tray.ExitRequested += ExitApp;

        Theme.Changed += OnThemeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        link = new ConnectionManager(new DispatcherSynchronizationContext(Dispatcher), settings.DeviceId, settings.AutoConnect);
        link.Changed += UpdateUi;

        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray)), null, Timeout.Infinite, executeOnlyOnce: false);

        OnThemeChanged();
        UpdateUi();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        Backdrop.HideSystemCaptionButtons(new WindowInteropHelper(this).Handle);
        ApplyBackdrop();
    }

    void ApplyBackdrop()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool acrylic = Backdrop.ApplyAcrylic(hwnd, Theme.IsDark);
        Root.SetResourceReference(Border.BackgroundProperty, acrylic ? "WindowTint" : "WindowFallback");
    }

    void ToggleConnection()
    {
        if (link.IsActive) link.Disconnect();
        else link.Connect();
    }

    void ToggleTheme()
    {
        settings.DarkMode = !Theme.IsDark;
        settings.Save();
        Theme.Apply(settings.DarkMode.Value);
    }

    void OnThemeChanged()
    {
        ThemeButton.Content = Theme.IsDark ? "" : "";
        ThemeButton.ToolTip = Theme.IsDark ? "Switch to light mode" : "Switch to dark mode";
        ApplyBackdrop();
        tray.ApplyTheme(Theme.IsDark);
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && settings.DarkMode == null)
            Dispatcher.BeginInvoke(() => Theme.Apply(Theme.SystemPrefersDark()));
    }

    void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) link.Resume();
    }

    void UpdateUi()
    {
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

        string phone = link.Target?.Name ?? "";
        bool noPhone = link.Devices.Count == 0 && state != LinkState.Searching;
        var (glyph, title, detail) = receiving
            ? ("", "Receiving audio", phone)
            : state switch
            {
                LinkState.Searching => ("", "Searching", "Looking for paired phones"),
                LinkState.Connecting => ("", "Connecting", phone),
                LinkState.Connected => ("", "Connected", $"{phone} · waiting for audio"),
                LinkState.Reconnecting => ("", "Reconnecting", link.ErrorText ?? phone),
                LinkState.Error => ("", "Connection failed", link.ErrorText ?? phone),
                _ when noPhone => ("", "No phone found", "Pair your iPhone in Bluetooth settings"),
                _ => ("", "Disconnected", "Connect to hear your phone here"),
            };

        var brush = BrushFor(indicator);
        StatusGlyph.Text = glyph;
        StatusGlyph.Foreground = brush;
        StatusHalo.Fill = brush;
        StatusText.Text = title;
        DetailText.Text = detail;

        bool active = link.IsActive;
        string toggleText = !active ? "Connect" : state == LinkState.Connected ? "Disconnect" : "Cancel";
        ConnectButton.Content = toggleText;
        var style = (Style)FindResource(active ? "SecondaryButton" : "PrimaryButton");
        if (ConnectButton.Style != style) ConnectButton.Style = style;
        ConnectButton.IsEnabled = active || link.Target != null;

        PhoneText.Text = link.Target?.Name ?? "None";
        bool canPick = link.Devices.Count > 1;
        PhoneButton.IsEnabled = canPick;
        PhoneChevron.Visibility = canPick ? Visibility.Visible : Visibility.Collapsed;

        string tip = state == LinkState.Connected && phone.Length > 0 ? $"{title} · {phone}" : title;
        tray.Update(indicator, $"Phone Audio Receiver: {tip}", toggleText, ConnectButton.IsEnabled);

        if (state == LinkState.Connected && link.Target is { } target && settings.DeviceId != target.Id)
        {
            settings.DeviceId = target.Id;
            settings.Save();
        }

        meterTimer.IsEnabled = IsVisible && state == LinkState.Connected;
        if (!meterTimer.IsEnabled) SetLevel(0);
    }

    SolidColorBrush BrushFor(Indicator indicator)
    {
        if (!brushes.TryGetValue(indicator, out var brush))
        {
            brush = new SolidColorBrush(Palette.Of(indicator));
            brush.Freeze();
            brushes[indicator] = brush;
        }
        return brush;
    }

    void ShowDevicePicker()
    {
        var picker = new ContextMenu { PlacementTarget = PhoneButton, Placement = PlacementMode.Bottom };
        foreach (var device in link.Devices)
        {
            var item = new MenuItem { Header = device.Name, IsCheckable = true, IsChecked = device == link.Target };
            item.Click += (_, _) => link.Select(device.Id);
            picker.Items.Add(item);
        }
        picker.IsOpen = true;
    }

    void OnMeterTick()
    {
        if (meterTicks++ % 20 == 0) RefreshOutputName();

        float peak = link.Target is { } t ? endpoints.PhonePeak(t.Name) : 0f;
        bool wasReceiving = Environment.TickCount64 - lastSoundTick < ReceivingHoldMs;
        if (peak > 0.0005f) lastSoundTick = Environment.TickCount64;
        bool isReceiving = Environment.TickCount64 - lastSoundTick < ReceivingHoldMs;

        SetLevel(Math.Max(ToDisplay(peak), level * 0.8));
        if (wasReceiving != isReceiving) UpdateUi();
    }

    void SetLevel(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (Math.Abs(value - level) < 0.004 && value != 0) return;
        level = value;
        LevelFill.Width = LevelTrack.ActualWidth * value;
    }

    // Peak amplitude to a 0..1 bar position on a -60..0 dB scale.
    static double ToDisplay(float peak) =>
        peak <= 0.001f ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    void RefreshOutputName()
    {
        string name = endpoints.DefaultOutputName();
        if (name == outputName) return;
        outputName = name;
        OutputText.Text = name;
        OutputText.ToolTip = name;
        endpoints.ResetMeter();
    }

    void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != IsVisibleProperty) return;
        if (IsVisible) RefreshOutputName();
        UpdateUi();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Theme.Changed -= OnThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        showWait.Unregister(null);
        meterTimer.Stop();
        link.Dispose();
        endpoints.Dispose();
        tray.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}

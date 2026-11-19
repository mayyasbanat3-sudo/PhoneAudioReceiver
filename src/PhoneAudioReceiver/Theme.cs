using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace PhoneAudioReceiver;

static class Theme
{
    static Application? app;

    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Initialize(Application application, bool dark)
    {
        app = application;
        var dicts = app.Resources.MergedDictionaries;
        dicts.Add(Load(dark ? "Dark" : "Light"));
        dicts.Add(Load("Styles"));
        IsDark = dark;
    }

    public static void Apply(bool dark)
    {
        if (app == null || dark == IsDark) return;
        app.Resources.MergedDictionaries[0] = Load(dark ? "Dark" : "Light");
        IsDark = dark;
        Changed?.Invoke();
    }

    public static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    static ResourceDictionary Load(string name) =>
        new() { Source = new Uri($"pack://application:,,,/Themes/{name}.xaml") };
}

// Windows 11 window effects: acrylic backdrop, dark caption, rounded corners.
static class Backdrop
{
    const int UseImmersiveDarkMode = 20;
    const int WindowCornerPreference = 33;
    const int SystemBackdropType = 38;
    const int CornerRound = 2;
    const int CornerRoundSmall = 3;
    const int BackdropAcrylic = 3;

    public static bool ApplyAcrylic(IntPtr hwnd, bool dark)
    {
        SetDarkMode(hwnd, dark);
        Set(hwnd, WindowCornerPreference, CornerRound);
        return Set(hwnd, SystemBackdropType, BackdropAcrylic);
    }

    public static void SetDarkMode(IntPtr hwnd, bool dark) => Set(hwnd, UseImmersiveDarkMode, dark ? 1 : 0);

    // The app draws its own caption buttons; stop DWM from drawing the system ones on top.
    public static void HideSystemCaptionButtons(IntPtr hwnd)
    {
        const int GwlStyle = -16;
        const long SysMenu = 0x00080000, MinimizeBox = 0x00020000, MaximizeBox = 0x00010000;
        long style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style & ~(SysMenu | MinimizeBox | MaximizeBox)));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    public static void RoundSmall(IntPtr hwnd) => Set(hwnd, WindowCornerPreference, CornerRoundSmall);

    static bool Set(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

using System.Windows;

namespace PhoneAudioReceiver;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PhoneAudioReceiver.Show", out bool firstInstance);
        if (!firstInstance)
        {
            showSignal.Set();
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var settings = Settings.Load();        Theme.Initialize(app, settings.DarkMode ?? Theme.SystemPrefersDark());

        var window = new MainWindow(settings, showSignal);
        if (!args.Contains("--minimized")) window.Show();
        app.Run();
    }
}

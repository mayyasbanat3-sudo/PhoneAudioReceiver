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

        // A small static window renders fine on the CPU; this keeps the GPU driver (and its memory)
        // out of the process and leaves the graphics card entirely to games.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var settings = Settings.Load();        Theme.Initialize(app, settings.DarkMode ?? Theme.SystemPrefersDark());

        var window = new MainWindow(settings, showSignal);
        if (!args.Contains("--minimized")) window.Show();
        app.Run();
    }
}

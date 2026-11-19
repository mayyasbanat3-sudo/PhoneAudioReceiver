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

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(showSignal, startHidden: args.Contains("--minimized")));
    }
}

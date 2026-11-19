using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace PhoneAudioReceiver;

enum LinkState { Searching, Disconnected, Connecting, Connected, Reconnecting, Error }

sealed record AudioDevice(string Id, string Name);

// Wraps Windows' built-in A2DP sink (AudioPlaybackConnection). Windows receives, decodes and
// plays the audio itself; this class only opens/keeps the link. All members run on the UI thread.
sealed class ConnectionManager : IDisposable
{
    static readonly int[] RetryDelaysSeconds = { 2, 5, 10, 20, 30, 60 };
    const int MaxRetries = 15;

    readonly SynchronizationContext ui;
    readonly DeviceWatcher watcher;
    readonly Dictionary<string, AudioDevice> devices = new();
    readonly System.Threading.Timer retryTimer;
    AudioPlaybackConnection? connection;
    string? targetId;
    bool wantConnected;
    int attempt;
    bool disposed;

    public LinkState State { get; private set; } = LinkState.Searching;
    public string? ErrorText { get; private set; }
    public bool AutoConnect { get; set; }
    public IReadOnlyCollection<AudioDevice> Devices => devices.Values;
    public AudioDevice? Target => targetId != null && devices.TryGetValue(targetId, out var d) ? d : null;
    public bool IsActive => State is LinkState.Connecting or LinkState.Connected or LinkState.Reconnecting;

    public event Action? Changed;

    public ConnectionManager(SynchronizationContext ui, string? preferredDeviceId, bool autoConnect)
    {
        this.ui = ui;
        targetId = preferredDeviceId;
        AutoConnect = autoConnect;
        retryTimer = new System.Threading.Timer(_ => Post(() => _ = OpenAsync()));

        watcher = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
        watcher.Added += (_, info) => Post(() => OnDeviceAdded(info));
        watcher.Removed += (_, update) => Post(() => OnDeviceRemoved(update.Id));
        watcher.Updated += (_, _) => { };
        watcher.EnumerationCompleted += (_, _) => Post(OnEnumerationCompleted);
        watcher.Start();
    }

    public void Select(string deviceId)
    {
        if (deviceId == targetId) return;
        bool reconnect = IsActive;
        StopConnection();
        targetId = deviceId;
        if (reconnect) Connect();
        else Changed?.Invoke();
    }

    public void Connect()
    {
        if (targetId == null) return;
        wantConnected = true;
        attempt = 0;
        retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _ = OpenAsync();
    }

    public void Disconnect()
    {
        StopConnection();
        ErrorText = null;
        SetState(LinkState.Disconnected);
    }

    // Called after the PC wakes from sleep: the old link is gone, start fresh.
    public void Resume() => Post(() =>
    {
        if (!wantConnected) return;
        CloseConnection();
        Connect();
    });

    async Task OpenAsync()
    {
        if (disposed || !wantConnected || targetId == null || connection != null) return;

        SetState(attempt == 0 ? LinkState.Connecting : LinkState.Reconnecting);
        var conn = AudioPlaybackConnection.TryCreateFromId(targetId);
        if (conn == null)
        {
            Fail("Device unavailable");
            return;
        }

        conn.StateChanged += OnConnectionStateChanged;
        connection = conn;
        try
        {
            await conn.StartAsync();
            var result = await conn.OpenAsync();
            if (connection != conn) return;

            if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                attempt = 0;
                ErrorText = null;
                SetState(LinkState.Connected);
            }
            else
            {
                Fail(Describe(result));
            }
        }
        catch (Exception ex)
        {
            if (connection == conn) Fail(ex.Message);
        }
    }

    void OnConnectionStateChanged(AudioPlaybackConnection sender, object args) => Post(() =>
    {
        if (sender == connection && sender.State == AudioPlaybackConnectionState.Closed && State == LinkState.Connected)
            Fail("Connection lost");
    });

    void Fail(string reason)
    {
        CloseConnection();
        ErrorText = reason;

        bool canRetry = wantConnected && AutoConnect && attempt < MaxRetries
                        && targetId != null && devices.ContainsKey(targetId);
        if (canRetry)
        {
            int delay = RetryDelaysSeconds[Math.Min(attempt, RetryDelaysSeconds.Length - 1)];
            attempt++;
            SetState(LinkState.Reconnecting);
            retryTimer.Change(TimeSpan.FromSeconds(delay), Timeout.InfiniteTimeSpan);
        }
        else
        {
            SetState(LinkState.Error);
        }
    }

    void OnDeviceAdded(DeviceInformation info)
    {
        devices[info.Id] = new AudioDevice(info.Id, info.Name);
        if (info.Id == targetId && wantConnected && connection == null && State != LinkState.Searching)
            Connect();
        Changed?.Invoke();
    }

    void OnDeviceRemoved(string id)
    {
        devices.Remove(id);
        Changed?.Invoke();
    }

    void OnEnumerationCompleted()
    {
        if (targetId == null || !devices.ContainsKey(targetId))
            targetId = devices.Keys.FirstOrDefault();

        SetState(LinkState.Disconnected);
        if (AutoConnect && targetId != null) Connect();
    }

    void StopConnection()
    {
        wantConnected = false;
        retryTimer.Change(Timeout.Infinite, Timeout.Infinite);
        CloseConnection();
    }

    void CloseConnection()
    {
        var conn = connection;
        connection = null;
        if (conn == null) return;
        conn.StateChanged -= OnConnectionStateChanged;
        conn.Dispose();
    }

    void SetState(LinkState state)
    {
        State = state;
        Changed?.Invoke();
    }

    void Post(Action action) => ui.Post(_ => { if (!disposed) action(); }, null);

    static string Describe(AudioPlaybackConnectionOpenResult result) => result.Status switch
    {
        AudioPlaybackConnectionOpenResultStatus.RequestTimedOut => "Phone did not respond",
        AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => "Denied by Windows",
        _ => result.ExtendedError is { } e ? $"Failed (0x{e.HResult:X8})" : "Failed",
    };

    public void Dispose()
    {
        if (disposed) return;
        StopConnection();
        disposed = true;
        retryTimer.Dispose();
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            watcher.Stop();
    }
}

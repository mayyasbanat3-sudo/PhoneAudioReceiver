using System.Runtime.InteropServices;

namespace PhoneAudioReceiver;

// Minimal Core Audio access: default output name and the phone's playback level.
// While connected, Windows plays the phone through an audio session on the default output
// whose display name is "Microphone (<phone> A2DP SNK)". Its meter gives the phone-only level.
sealed class AudioEndpoints : IDisposable
{
    const int ClsctxAll = 0x17;
    const int StgmRead = 0;
    static readonly PropertyKey FriendlyNameKey = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    IAudioMeterInformation? meter;
    string? meterDevice;
    long nextLookupTick;

    public string DefaultOutputName()
    {
        try
        {
            enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device);
            return FriendlyName(device);
        }
        catch
        {
            return "No output device";
        }
    }

    // Forget the cached session, e.g. after a reconnect or an output device change.
    public void ResetMeter()
    {
        if (meter != null) Marshal.ReleaseComObject(meter);
        meter = null;
        meterDevice = null;
    }

    public float PhonePeak(string phoneName)
    {
        if (meterDevice != phoneName || (meter == null && Environment.TickCount64 >= nextLookupTick))
        {
            ResetMeter();
            meterDevice = phoneName;
            meter = FindPhoneSessionMeter(phoneName);
            nextLookupTick = Environment.TickCount64 + 2000;
        }
        if (meter == null) return 0f;

        try
        {
            meter.GetPeakValue(out float peak);
            return peak;
        }
        catch
        {
            ResetMeter();
            return 0f;
        }
    }

    IAudioMeterInformation? FindPhoneSessionMeter(string phoneName)
    {
        try
        {
            enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var output);
            Guid iid = typeof(IAudioSessionManager2).GUID;
            output.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object activated);
            ((IAudioSessionManager2)activated).GetSessionEnumerator(out var sessions);
            sessions.GetCount(out int count);

            IAudioMeterInformation? fallback = null;
            for (int i = 0; i < count; i++)
            {
                sessions.GetSession(i, out var session);
                session.GetDisplayName(out string? name);
                if (name == null || !name.Contains("A2DP", StringComparison.OrdinalIgnoreCase)) continue;

                var sessionMeter = (IAudioMeterInformation)session;
                if (name.Contains(phoneName, StringComparison.OrdinalIgnoreCase)) return sessionMeter;
                fallback ??= sessionMeter;
            }
            return fallback;
        }
        catch
        {
            return null;
        }
    }

    static string FriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(StgmRead, out var store);
        var key = FriendlyNameKey;
        store.GetValue(ref key, out var value);
        try
        {
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : "";
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    public void Dispose()
    {
        ResetMeter();
        Marshal.ReleaseComObject(enumerator);
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    enum DataFlow { Render = 0, Capture = 1 }
    enum Role { Console = 0, Multimedia = 1 }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct PropertyKey(Guid formatId, int propertyId)
    {
        readonly Guid formatId = formatId;
        readonly int propertyId = propertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant
    {
        public ushort Type;
        ushort reserved1, reserved2, reserved3;
        public IntPtr Pointer;
        IntPtr extra;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        void GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        void OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string? name);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        void GetPeakValue(out float peak);
    }
}

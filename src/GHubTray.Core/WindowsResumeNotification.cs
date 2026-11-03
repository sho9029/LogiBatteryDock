using System.Runtime.InteropServices;

namespace GHubTray.Core;

/// <summary>One notification per suspend/resume cycle, independent of any UI host.</summary>
public sealed class WindowsResumeNotification : IDisposable
{
    private readonly object _gate = new();
    private readonly Action _onResume;
    private readonly DeviceNotifyCallback _callback;
    private IntPtr _registration;
    private bool _resumeHandled;
    private bool _disposed;

    public static WindowsResumeNotification? TryCreate(Action onResume)
    {
        var notification = new WindowsResumeNotification(onResume);
        var parameters = new SubscribeParameters { Callback = notification._callback };
        var error = PowerRegisterSuspendResumeNotification(2, ref parameters, out notification._registration);
        if (error == 0) return notification;
        System.Diagnostics.Debug.WriteLine($"Resume registration failed: {error}");
        return null; // Periodic and manual refresh remain available.
    }

    internal WindowsResumeNotification(Action onResume)
    {
        _onResume = onResume;
        _callback = OnPowerEvent;
    }

    private uint OnPowerEvent(IntPtr context, uint type, IntPtr setting)
    {
        try { ProcessPowerEvent(type); }
        catch (Exception exception)
        {
            // Exceptions must never cross the unmanaged callback boundary.
            System.Diagnostics.Debug.WriteLine($"Resume notification failed: {exception.GetType().Name}");
        }
        return 0;
    }

    internal void ProcessPowerEvent(uint type)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (type == 4) // PBT_APMSUSPEND
            {
                _resumeHandled = false;
                return;
            }
            // PBT_APMRESUMEAUTOMATIC also covers unattended wake. Ignore the
            // subsequent interactive resume notification and duplicate events.
            if (type != 18 || _resumeHandled) return;
            _resumeHandled = true;
        }
        _onResume(); // Only enqueue work; never perform device I/O in this callback.
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        if (_registration != IntPtr.Zero)
        {
            PowerUnregisterSuspendResumeNotification(_registration);
            _registration = IntPtr.Zero;
        }
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint DeviceNotifyCallback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscribeParameters
    {
        public DeviceNotifyCallback Callback;
        public IntPtr Context;
    }

    [DllImport("Powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerRegisterSuspendResumeNotification(
        uint flags, ref SubscribeParameters recipient, out IntPtr registration);

    [DllImport("Powrprof.dll", ExactSpelling = true)]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr registration);
}

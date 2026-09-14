using System.Runtime.InteropServices;
using MooTrack.Derivation;

namespace MooTrack.Agent;

public sealed record SignalObserved(ObservedEvent Event, string Source, string Detail);

/// <summary>
/// Owns a message-only _window and the power/session subscriptions delivered to it.
/// Every callback is one-way: it raises an event and returns.
/// </summary>
public sealed class DesktopMonitor(ILogger<DesktopMonitor> logger) : ISignalSource
{
    private readonly ManualResetEventSlim _ready = new(false);

    private Native.WindowProcedure? _procedure;
    private Thread? _pump;
    private IntPtr _window;
    private IntPtr _displayNotification;
    private IntPtr _presenceNotification;
    private volatile bool _stopping;

    public event Action<SignalObserved, DateTimeOffset>? Observed;

    public void Start()
    {
        _pump = new Thread(Pump) { IsBackground = true, Name = "MooTrack.DesktopMonitor" };
        _pump.SetApartmentState(ApartmentState.STA);
        _pump.Start();
        _ready.Wait(TimeSpan.FromSeconds(10));
    }

    private void Pump()
    {
        try
        {
            Create();
            _ready.Set();

            while (!_stopping && Native.GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessageW(ref message);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "desktop monitor _pump stopped");
            _ready.Set();
        }
    }

    private void Create()
    {
        var className = Marshal.StringToHGlobalUni("MooTrackMonitor");
        var instance = Native.GetModuleHandleW(IntPtr.Zero);

        _procedure = Procedure;
        var windowClass = new Native.WindowClass
        {
            Size = (uint)Marshal.SizeOf<Native.WindowClass>(),
            Procedure = _procedure,
            Instance = instance,
            ClassName = className,
        };

        if (Native.RegisterClassExW(ref windowClass) == 0)
            throw new InvalidOperationException(
                $"RegisterClassEx failed: {Marshal.GetLastWin32Error()}");

        _window = Native.CreateWindowExW(
            0, className, IntPtr.Zero, 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, instance, IntPtr.Zero);

        if (_window == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        var sessionId = CurrentSessionId();
        logger.LogInformation("subscribing to power settings in session {Session}", sessionId);

        _displayNotification = Subscribe(PowerSettings.DisplayState(sessionId));
        _presenceNotification = Subscribe(PowerSettings.UserPresence(sessionId));

        if (!Native.WTSRegisterSessionNotification(_window, Native.NOTIFY_FOR_ALL_SESSIONS))
            logger.LogError(
                "session notification subscription failed: {Error}", Marshal.GetLastWin32Error());
    }

    private IntPtr Procedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case Native.WM_POWERBROADCAST:
                    OnPower(wParam, lParam);
                    return new IntPtr(1);
                case Native.WM_WTSSESSION_CHANGE:
                    OnSession(wParam.ToInt32());
                    return IntPtr.Zero;
                case Native.WM_ENDSESSION:
                    Raise(ObservedEvent.Shutdown, "SessionChange", "WM_ENDSESSION");
                    return IntPtr.Zero;
                case Native.WM_DESTROY:
                    Native.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "_window _procedure failed for message {Message}", message);
        }

        return Native.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void OnPower(IntPtr wParam, IntPtr lParam)
    {
        switch (wParam.ToInt32())
        {
            case Native.PBT_APMSUSPEND:
                Raise(ObservedEvent.Suspend, "PowerNotify", "PBT_APMSUSPEND");
                return;
            case Native.PBT_APMRESUMESUSPEND:
            case Native.PBT_APMRESUMEAUTOMATIC:
                Raise(ObservedEvent.Resume, "PowerNotify", "PBT_APMRESUME");
                return;
            case Native.PBT_POWERSETTINGCHANGE:
                OnPowerSetting(lParam);
                return;
        }
    }

    private void OnPowerSetting(IntPtr lParam)
    {
        var setting = Marshal.PtrToStructure<Native.PowerBroadcastSetting>(lParam);

        if (setting.PowerSetting == PowerSettings.ConsoleDisplayState)
        {
            // 0 off, 1 on, 2 dimmed. A dimmed screen is still a screen in use.
            if (setting.Data == 0) Raise(ObservedEvent.DisplayOff, "PowerNotify", "off");
            else Raise(ObservedEvent.DisplayOn, "PowerNotify", setting.Data == 1 ? "on" : "dimmed");
        }
        else if (setting.PowerSetting == PowerSettings.GlobalUserPresence
                 || setting.PowerSetting == PowerSettings.SessionUserPresence)
        {
            if (setting.Data == 0) Raise(ObservedEvent.UserPresent, "PowerNotify", "present");
            else Raise(ObservedEvent.UserInactive, "PowerNotify", "inactive");
        }
    }

    private void OnSession(int change)
    {
        switch (change)
        {
            case Native.WTS_SESSION_LOCK:
                Raise(ObservedEvent.Lock, "SessionChange", "lock");
                return;
            case Native.WTS_SESSION_UNLOCK:
            case Native.WTS_SESSION_LOGON:
                Raise(ObservedEvent.Unlock, "SessionChange",
                    change == Native.WTS_SESSION_LOGON ? "logon" : "unlock");
                return;
            case Native.WTS_SESSION_LOGOFF:
                Raise(ObservedEvent.Lock, "SessionChange", "logoff");
                return;
        }
    }

    private static uint CurrentSessionId() =>
        Native.ProcessIdToSessionId(Native.GetCurrentProcessId(), out var session) ? session : 0;

    private IntPtr Subscribe(Guid setting)
    {
        var value = setting;
        var handle = Native.RegisterPowerSettingNotification(
            _window, ref value, Native.DEVICE_NOTIFY_WINDOW_HANDLE);

        if (handle == IntPtr.Zero)
            logger.LogError(
                "subscription to {Setting} failed with Win32 error {Error}",
                PowerSettings.Describe(setting), Marshal.GetLastWin32Error());
        else
            logger.LogInformation("subscribed to {Setting}", PowerSettings.Describe(setting));

        return handle;
    }

    private void Raise(ObservedEvent observed, string source, string detail)
    {
        try
        {
            Observed?.Invoke(new SignalObserved(observed, source, detail), DateTimeOffset.Now);
        }
        catch (Exception e)
        {
            logger.LogError(e, "observation handler threw for {Event}", observed);
        }
    }

    public void Dispose()
    {
        _stopping = true;

        if (_displayNotification != IntPtr.Zero)
            Native.UnregisterPowerSettingNotification(_displayNotification);
        if (_presenceNotification != IntPtr.Zero)
            Native.UnregisterPowerSettingNotification(_presenceNotification);

        if (_window != IntPtr.Zero)
        {
            Native.WTSUnRegisterSessionNotification(_window);
            Native.DestroyWindow(_window);
            _window = IntPtr.Zero;
        }

        _ready.Dispose();
    }
}

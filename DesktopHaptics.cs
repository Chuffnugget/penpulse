using System.Runtime.InteropServices;
using Windows.Devices.Haptics;
using Windows.Foundation.Metadata;

namespace PenPulse;
// ABI adapter for the documented Windows 11 InputHapticsManager. This keeps the
// app's Windows 11 21H2 minimum while using the new API when it exists. Interface
// IDs and slot order are from Windows.Devices.winmd (UniversalApiContract v19).
internal sealed class DesktopHaptics : IDisposable
{
    // COM slots include the six IInspectable methods. Keep factory and instance
    // indexes separate: the same index refers to a different method on each.
    private static class FactorySlots
    {
        public const int IsSupported = 6;
        public const int DevicePresent = 7;
        public const int GetManager = 8;
    }

    private static class ManagerSlots
    {
        public const int RecentDeviceType = 7;
        public const int RecentController = 8;
        public const int SendFeedback = 10;
        public const int SendFeedbackForDuration = 11;
        public const int StopFeedback = 13;
        public const int SetControllerOverride = 14;
        public const int ClearControllerOverride = 15;
    }

    private const int PenDeviceType = 2;
    private static readonly Guid ManagerFactoryId = new("7bb40f77-e187-5322-844e-aa58223c281a");
    private nint factory, manager;
    public string Diagnostic { get; private set; } = "Not initialized";
    public bool Supported
    {
        get; private set;
    }
    public bool DevicePresent
    {
        get; private set;
    }
    public int DeviceType
    {
        get; private set;
    }
    public long RoutedCommands
    {
        get; private set;
    }
    public long RejectedCommands
    {
        get; private set;
    }
    public long StopCommands
    {
        get; private set;
    }
    public string StopDiagnostic { get; private set; } = "No stop requested";
    public string SendDiagnostic { get; private set; } = "No feedback requested";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BoolCall(nint self, out byte value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IntCall(nint self, out int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ObjectCall(nint self, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OverrideCall(nint self, int deviceType, nint controller, out long token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ClearCall(nint self, long token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SendCall(nint self, ushort waveform, ushort fallback, double intensity, out byte sent);
    // TimeSpan crosses the WinRT ABI as a signed count of 100 ns ticks.
    // Signature/slot: microsoft/windows-rs, Windows/Devices/Haptics, IInputHapticsManager_Vtbl.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DurationCall(nint self, ushort waveform, ushort fallback, double intensity, long duration, out byte sent);
    [DllImport("combase", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string value, uint length, out nint result);
    [DllImport("combase")]
    private static extern int WindowsDeleteString(nint value);
    [DllImport("combase")]
    private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint result);
    private static T Slot<T>(nint p, int slot)
        where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p), slot * IntPtr.Size));
    public DesktopHaptics()
    {
        nint name = 0;
        try
        {
            const string className = "Windows.Devices.Haptics.InputHapticsManager";
            if (!ApiInformation.IsTypePresent(className))
            {
                Diagnostic = "New Windows haptics API is unavailable";
                return;
            }

            Marshal.ThrowExceptionForHR(WindowsCreateString(className, (uint)className.Length, out name));
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in ManagerFactoryId, out factory));
            Marshal.ThrowExceptionForHR(Slot<BoolCall>(factory, FactorySlots.IsSupported)(factory, out var supported));
            Supported = supported != 0;
            RefreshPresence();
            if (!Supported)
            {
                Diagnostic = "Windows haptics manager reports unsupported";
                return;
            }

            Marshal.ThrowExceptionForHR(Slot<ObjectCall>(factory, FactorySlots.GetManager)(factory, out manager));
            Diagnostic = "Windows haptics manager ready";
        }
        catch (Exception e)
        {
            Diagnostic = $"Windows haptics discovery: 0x{e.HResult:X8} {e.Message}";
        }
        finally
        {
            if (name != 0)
                WindowsDeleteString(name);
        }
    }

    private void RefreshPresence()
    {
        if (factory == 0)
            return;
        Marshal.ThrowExceptionForHR(Slot<BoolCall>(factory, FactorySlots.DevicePresent)(factory, out var present));
        DevicePresent = present != 0;
    }

    public SimpleHapticsController? GetPenController()
    {
        if (manager == 0)
            return null;
        nint controller = 0;
        try
        {
            RefreshPresence();
            Marshal.ThrowExceptionForHR(Slot<IntCall>(manager, ManagerSlots.RecentDeviceType)(manager, out var type));
            DeviceType = type;
            // Reject other device types so a pen gesture cannot vibrate a touchpad.
            if (type != PenDeviceType)
            {
                Diagnostic = $"No recent haptic pen on this thread (type={type}, devicePresent={DevicePresent})";
                return null;
            }

            Marshal.ThrowExceptionForHR(Slot<ObjectCall>(manager, ManagerSlots.RecentController)(manager, out controller));
            Diagnostic = controller == 0 ? "Pen identified but Windows returned no haptic controller" : "Pen controller acquired through desktop haptics";
            return controller == 0 ? null : WinRT.MarshalInspectable<SimpleHapticsController>.FromAbi(controller);
        }
        catch (Exception e)
        {
            Diagnostic = $"Desktop haptics: 0x{e.HResult:X8} {e.Message}";
            return null;
        }
        finally
        {
            if (controller != 0)
                Marshal.Release(controller);
        }
    }

    // Raw input can arrive when this thread does not own pointer focus. Provide
    // the pen controller explicitly for just this request, then clear the override.
    public bool TrySend(SimpleHapticsController controller, ushort waveform, double intensity, int durationMilliseconds = 0)
    {
        if (manager == 0)
        {
            SendDiagnostic = "Windows haptics routing unavailable";
            return false;
        }

        nint abi = 0;
        long token = 0;
        bool overridden = false;
        try
        {
            abi = WinRT.MarshalInspectable<SimpleHapticsController>.FromManaged(controller);
            Marshal.ThrowExceptionForHR(Slot<OverrideCall>(manager, ManagerSlots.SetControllerOverride)(manager, PenDeviceType, abi, out token));
            overridden = true;
            byte sent;
            if (durationMilliseconds > 0)
                Marshal.ThrowExceptionForHR(Slot<DurationCall>(manager, ManagerSlots.SendFeedbackForDuration)(manager, waveform, waveform, intensity, TimeSpan.FromMilliseconds(Math.Clamp(durationMilliseconds, 1, 250)).Ticks, out sent));
            else
                Marshal.ThrowExceptionForHR(Slot<SendCall>(manager, ManagerSlots.SendFeedback)(manager, waveform, 0, intensity, out sent));
            if (sent != 0)
            {
                RoutedCommands++;
                SendDiagnostic = "Windows accepted the pen-routed request";
                return true;
            }

            RejectedCommands++;
            SendDiagnostic = "Windows declined the pen-routed request";
            return false;
        }
        catch (Exception e)
        {
            RejectedCommands++;
            SendDiagnostic = $"Haptics routing: 0x{e.HResult:X8} {e.Message}";
            return false;
        }
        finally
        {
            if (overridden)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(Slot<ClearCall>(manager, ManagerSlots.ClearControllerOverride)(manager, token));
                }
                catch (Exception e)
                {
                    SendDiagnostic = $"Clear haptics route: 0x{e.HResult:X8}";
                }
            }

            if (abi != 0)
                Marshal.Release(abi);
        }
    }

    public bool TryStop()
    {
        if (manager == 0)
            return false;
        try
        {
            // Stops the device most recently sent a waveform by this manager,
            // including after the temporary routing override has been cleared.
            Marshal.ThrowExceptionForHR(Slot<BoolCall>(manager, ManagerSlots.StopFeedback)(manager, out var stopped));
            StopCommands++;
            StopDiagnostic = stopped != 0 ? "Windows accepted the stop request" : "Already stopped or stop declined";
            return stopped != 0;
        }
        catch (Exception e)
        {
            StopDiagnostic = $"Stop feedback: 0x{e.HResult:X8} {e.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        if (manager != 0)
        {
            Marshal.Release(manager);
            manager = 0;
        }

        if (factory != 0)
        {
            Marshal.Release(factory);
            factory = 0;
        }
    }
}

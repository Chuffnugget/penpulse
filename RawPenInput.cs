using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PenPulse;

internal sealed class RawPenInput : IDisposable
{
    private readonly Dictionary<nint, Descriptor?> devices = new();
    private readonly nint window;
    private readonly PenShortcutRecognizer shortcuts = new();
    private readonly Dictionary<nint, bool> penKeyboards = new();
    public event Action<nint, PenSample>? Sample;
    public event Action<Gesture>? Shortcut;
    public event Action<string>? Fault;
    public long Reports
    {
        get; private set;
    }
    public long LastReportAt { get; private set; } = -10000;
    public nint LastDevice
    {
        get; private set;
    }
    public long EraserContactReports
    {
        get; private set;
    }
    public long EraserHoverReports
    {
        get; private set;
    }
    public long ShortcutKeyReports
    {
        get; private set;
    }
    public long Shortcuts
    {
        get; private set;
    }
    public long PenShortcutReleaseReports
    {
        get; private set;
    }

    public RawPenInput(nint window)
    {
        this.window = window;
        // Observe pen TLCs in the background. Never use NOLEGACY, capture, or input redirection.
        var registrations = new[]
        {
            new Native.RawDevice
            {
                Page = 0x0d,
                Usage = 0x02,
                Flags = 0x2100,
                Target = window
            },
            new Native.RawDevice
            {
                Page = 0x0d,
                Usage = 0x01,
                Flags = 0x2100,
                Target = window
            },
            new Native.RawDevice
            {
                Page = 0x01,
                Usage = 0x06,
                Flags = 0x2100,
                Target = window
            }
        };
        if (!Native.RegisterRawInputDevices(registrations, (uint)registrations.Length, (uint)Marshal.SizeOf<Native.RawDevice>()))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register the background pen listener.");
    }

    public void DeviceChanged(nint device)
    {
        shortcuts.DeviceRemoved(device);
        penKeyboards.Remove(device);
        if (devices.Remove(device, out var descriptor))
            descriptor?.Dispose();
        if (LastDevice == device)
        {
            LastDevice = 0;
            LastReportAt = -10000;
        }
    }

    public void ResetShortcuts() => shortcuts.Reset();
    private bool IsSlimPenKeyboard(nint device)
    {
        if (device == 0)
            return false;
        if (penKeyboards.TryGetValue(device, out bool found))
            return found;
        uint length = 0;
        if (Native.GetRawInputDeviceInfoW(device, 0x20000007, 0, ref length) == uint.MaxValue || length == 0 || length > 4096)
            return false;
        var name = Marshal.AllocHGlobal(checked((int)(length + 1) * 2));
        try
        {
            found = Native.GetRawInputDeviceInfoW(device, 0x20000007, name, ref length) != uint.MaxValue && PenKeyboardIdentity.SupportsReleaseFallback(Marshal.PtrToStringUni(name) ?? "");
            if (penKeyboards.Count >= 64)
                penKeyboards.Clear();
            penKeyboards[device] = found;
            return found;
        }
        finally
        {
            Marshal.FreeHGlobal(name);
        }
    }

    public void Read(nint input)
    {
        uint size = 0, headerSize = (uint)Marshal.SizeOf<Native.RawHeader>();
        if (Native.GetRawInputData(input, Native.RID_INPUT, 0, ref size, headerSize) == uint.MaxValue || size < headerSize + 8 || size > 1024 * 1024)
            return;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.GetRawInputData(input, Native.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
                return;
            var header = Marshal.PtrToStructure<Native.RawHeader>(buffer);
            if (header.Type == 1)
            {
                if (size < headerSize + Marshal.SizeOf<Native.RawKeyboard>())
                    return;
                var key = Marshal.PtrToStructure<Native.RawKeyboard>(buffer + (int)headerSize);
                if (key.VKey is >= 0x81 and <= 0x83)
                    ShortcutKeyReports++;
                bool penRelease = key.VKey is >= 0x81 and <= 0x83 && (key.Flags & 1) != 0 && IsSlimPenKeyboard(header.Device);
                if (penRelease)
                    PenShortcutReleaseReports++;
                var action = shortcuts.Feed(header.Device, key.VKey, key.Flags, key.MakeCode, Environment.TickCount64, penRelease);
                if (action is Gesture gesture)
                {
                    Shortcuts++;
                    Shortcut?.Invoke(gesture);
                }

                return;
            }

            if (header.Type != 2)
                return;
            if (!devices.TryGetValue(header.Device, out var descriptor))
            {
                try
                {
                    descriptor = new Descriptor(header.Device);
                }
                catch (Exception e)
                {
                    descriptor = null;
                    Fault?.Invoke("Pen report could not be decoded: " + e.Message);
                }

                devices[header.Device] = descriptor;
            }

            if (descriptor == null)
                return;
            int reportSize = Marshal.ReadInt32(buffer, (int)headerSize);
            int count = Marshal.ReadInt32(buffer, (int)headerSize + 4);
            if (reportSize <= 0 || count <= 0 || (long)reportSize * count > size - headerSize - 8)
                return;
            var report = new byte[reportSize];
            for (int i = 0; i < count; i++)
            {
                Marshal.Copy(buffer + (int)headerSize + 8 + i * reportSize, report, 0, reportSize);
                if (!descriptor.TryParse(report, out var sample))
                    continue;
                Reports++;
                LastReportAt = Environment.TickCount64;
                LastDevice = header.Device;
                if (sample.Eraser && sample.Contact)
                    EraserContactReports++;
                else if (sample.Eraser && sample.InRange)
                    EraserHoverReports++;
                Sample?.Invoke(header.Device, sample);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        foreach (var d in devices.Values)
            d?.Dispose();
        devices.Clear();
        shortcuts.Reset();
        penKeyboards.Clear();
        var remove = new[]
        {
            new Native.RawDevice
            {
                Page = 0x0d,
                Usage = 0x02,
                Flags = 1
            },
            new Native.RawDevice
            {
                Page = 0x0d,
                Usage = 0x01,
                Flags = 1
            },
            new Native.RawDevice
            {
                Page = 1,
                Usage = 6,
                Flags = 1
            }
        };
        Native.RegisterRawInputDevices(remove, (uint)remove.Length, (uint)Marshal.SizeOf<Native.RawDevice>());
    }

    private sealed class Descriptor : IDisposable
    {
        private nint data;
        private readonly Native.ValueCaps[] values;
        private readonly Native.ButtonCaps[] buttons;
        private readonly HashSet<byte> contactReportIds;
        private readonly ushort[] usageBuffer = new ushort[256];
        public Descriptor(nint device)
        {
            uint length = 0;
            if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_PREPARSEDDATA, 0, ref length) == uint.MaxValue || length == 0)
                throw new InvalidOperationException("No HID descriptor.");
            data = Marshal.AllocHGlobal((int)length);
            try
            {
                if (Native.GetRawInputDeviceInfoW(device, Native.RIDI_PREPARSEDDATA, data, ref length) == uint.MaxValue || Native.HidP_GetCaps(data, out var caps) != Native.HID_OK)
                    throw new InvalidOperationException("HID capabilities unavailable.");
                values = new Native.ValueCaps[caps.InputValues];
                buttons = new Native.ButtonCaps[caps.InputButtons];
                var vc = caps.InputValues;
                var bc = caps.InputButtons;
                if (vc > 0 && Native.HidP_GetValueCaps(0, values, ref vc, data) != Native.HID_OK)
                    throw new InvalidOperationException("HID values unavailable.");
                if (bc > 0 && Native.HidP_GetButtonCaps(0, buttons, ref bc, data) != Native.HID_OK)
                    throw new InvalidOperationException("HID buttons unavailable.");
                contactReportIds = buttons.Where(c => c.Page == 0x0d && (c.Contains(0x42) || c.Contains(0x45))).Select(c => c.ReportId).ToHashSet();
                if (contactReportIds.Count == 0)
                    throw new InvalidOperationException("No pen or eraser contact switch in this collection.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool TryParse(byte[] report, out PenSample sample)
        {
            sample = default;
            // Other report IDs may describe battery or buttons, not a new contact state.
            if (report.Length == 0 || (!contactReportIds.Contains(0) && !contactReportIds.Contains(report[0])))
                return false;
            bool hasTip = Button(report, 0x42, out bool tipContact);
            bool hasEraser = Button(report, 0x45, out bool erase);
            if (!hasTip && !hasEraser)
                return false;
            Button(report, 0x44, out bool barrel);
            Button(report, 0x3c, out bool inverted);
            bool hasRange = Button(report, 0x32, out bool inRange);
            var bounds = SystemInformation.VirtualScreen;
            double x = Coordinate(report, 0x30, bounds.Width), y = Coordinate(report, 0x31, bounds.Height);
            if (double.IsNaN(x) || double.IsNaN(y))
            {
                if (!Native.GetCursorPos(out var p))
                    return false;
                x = p.X;
                y = p.Y;
            }

            sample = PenSample.FromSwitches(tipContact, erase, inverted, barrel, !hasRange || inRange, x, y);
            return true;
        }

        private bool Button(byte[] report, ushort usage, out bool pressed)
        {
            pressed = false;
            foreach (var cap in buttons)
            {
                if (cap.Page != 0x0d || !cap.Contains(usage) || (cap.ReportId != 0 && report[0] != cap.ReportId))
                    continue;
                uint count = (uint)usageBuffer.Length;
                if (Native.HidP_GetUsages(0, 0x0d, cap.Link, usageBuffer, ref count, data, report, (uint)report.Length) != Native.HID_OK)
                    return false;
                for (int i = 0; i < count; i++)
                    if (usageBuffer[i] == usage)
                    {
                        pressed = true;
                        break;
                    }

                return true;
            }

            return false;
        }

        private double Coordinate(byte[] report, ushort usage, int pixels)
        {
            foreach (var cap in values)
            {
                if (cap.Page != 1 || !cap.Contains(usage) || (cap.ReportId != 0 && cap.ReportId != report[0]) || cap.LogicalMax <= cap.LogicalMin)
                    continue;
                if (Native.HidP_GetUsageValue(0, 1, cap.Link, usage, out uint value, data, report, (uint)report.Length) != Native.HID_OK)
                    continue;
                long signed = value;
                if (cap.LogicalMin < 0 && cap.BitSize > 0 && cap.BitSize < 32 && (value & (1u << (cap.BitSize - 1))) != 0)
                    signed = (long)value - (1L << cap.BitSize);
                else if (cap.LogicalMin < 0 && cap.BitSize == 32)
                    signed = unchecked((int)value);
                return (signed - cap.LogicalMin) * pixels / (double)(cap.LogicalMax - cap.LogicalMin);
            }

            return double.NaN;
        }

        public void Dispose()
        {
            if (data != 0)
            {
                Marshal.FreeHGlobal(data);
                data = 0;
            }
        }
    }
}

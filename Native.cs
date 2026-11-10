using System.Runtime.InteropServices;

namespace PenPulse;

internal static class Native
{
    public const int WM_INPUT = 0x00ff, WM_INPUT_DEVICE_CHANGE = 0x00fe;
    public const int WM_POINTERUPDATE = 0x245, WM_POINTERDOWN = 0x246, WM_POINTERUP = 0x247, WM_POINTERENTER = 0x249;
    public const uint RID_INPUT = 0x10000003, RIDI_PREPARSEDDATA = 0x20000005;
    public const int HID_OK = 0x00110000;
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawDevice
    {
        public ushort Page, Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawHeader
    {
        public uint Type, Size;
        public nint Device, WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RawKeyboard
    {
        public ushort MakeCode, Flags, Reserved, VKey;
        public uint Message, Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHook
    {
        public Point Point;
        public uint MouseData, Flags, Time;
        public nuint Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointerInfo
    {
        public uint Type, Id, Frame, Flags;
        public nint Source, Target;
        public Point Pixel, Himetric, PixelRaw, HimetricRaw;
        public uint Time, History;
        public int InputData;
        public uint KeyStates;
        public ulong Performance;
        public int ButtonChange;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct HidCaps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        public fixed ushort Reserved[17];
        public ushort Links, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }

    // HIDP_VALUE_CAPS is exactly 72 bytes; the union at offset 56 is represented by its range fields.
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct ValueCaps
    {
        [FieldOffset(0)]
        public ushort Page;
        [FieldOffset(2)]
        public byte ReportId;
        [FieldOffset(6)]
        public ushort Link;
        [FieldOffset(12)]
        public byte IsRange;
        [FieldOffset(18)]
        public ushort BitSize;
        [FieldOffset(40)]
        public int LogicalMin;
        [FieldOffset(44)]
        public int LogicalMax;
        [FieldOffset(56)]
        public ushort UsageMin;
        [FieldOffset(58)]
        public ushort UsageMax;
        public bool Contains(ushort usage) => IsRange != 0 ? usage >= UsageMin && usage <= UsageMax : usage == UsageMin;
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct ButtonCaps
    {
        [FieldOffset(0)]
        public ushort Page;
        [FieldOffset(2)]
        public byte ReportId;
        [FieldOffset(6)]
        public ushort Link;
        [FieldOffset(12)]
        public byte IsRange;
        [FieldOffset(56)]
        public ushort UsageMin;
        [FieldOffset(58)]
        public ushort UsageMax;
        public bool Contains(ushort usage) => IsRange != 0 ? usage >= UsageMin && usage <= UsageMax : usage == UsageMin;
    }

    public delegate nint HookProc(int code, nint w, nint l);
    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
    [DllImport("user32", SetLastError = true)]
    public static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32", SetLastError = true)]
    public static extern uint GetRawInputDeviceInfoW(nint device, uint command, nint data, ref uint size);
    [DllImport("user32", SetLastError = true)]
    public static extern nint SetWindowsHookExW(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")]
    public static extern nint CallNextHookEx(nint hook, int code, nint w, nint l);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? name);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPointerType(uint id, out uint type);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPointerInfo(uint id, out PointerInfo info);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);
    [DllImport("user32")]
    public static extern nint GetForegroundWindow();
    [DllImport("user32")]
    public static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32")]
    public static extern uint GetDoubleClickTime();
    [DllImport("user32")]
    public static extern short GetAsyncKeyState(int key);
    [DllImport("user32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint icon);
    [DllImport("hid")]
    public static extern int HidP_GetCaps(nint preparsed, out HidCaps caps);
    [DllImport("hid")]
    public static extern int HidP_GetValueCaps(int type, [Out] ValueCaps[] caps, ref ushort length, nint preparsed);
    [DllImport("hid")]
    public static extern int HidP_GetButtonCaps(int type, [Out] ButtonCaps[] caps, ref ushort length, nint preparsed);
    [DllImport("hid")]
    public static extern int HidP_GetUsages(int type, ushort page, ushort link, [Out] ushort[] usages, ref uint length, nint preparsed, byte[] report, uint reportLength);
    [DllImport("hid")]
    public static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, nint preparsed, byte[] report, uint reportLength);
}

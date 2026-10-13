using System.Runtime.InteropServices;

namespace RemoteInput;

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public int x;
    public int y;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTDEVICE
{
    public ushort usUsagePage;
    public ushort usUsage;
    public uint dwFlags;
    public IntPtr hwndTarget;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTHEADER
{
    public uint dwType;
    public uint dwSize;
    public IntPtr hDevice;
    public IntPtr wParam;
}

// usFlags の後ろに2バイトのパディングがあり、ボタン情報は共用体なので明示レイアウトにする
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct RAWMOUSE
{
    [FieldOffset(0)] public ushort usFlags;
    [FieldOffset(4)] public ushort usButtonFlags;
    [FieldOffset(6)] public short usButtonData;
    [FieldOffset(8)] public uint ulRawButtons;
    [FieldOffset(12)] public int lLastX;
    [FieldOffset(16)] public int lLastY;
    [FieldOffset(20)] public uint ulExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUT
{
    public RAWINPUTHEADER header;
    public RAWMOUSE mouse;
}

internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const uint LLKHF_EXTENDED = 0x01;
    public const uint LLKHF_INJECTED = 0x10;
    public const uint LLKHF_UP = 0x80;
    public const uint LLMHF_INJECTED = 0x01;

    public const int WM_INPUT = 0x00FF;

    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_INPUTSINK = 0x00000100;

    public const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;
    public const ushort RI_MOUSE_WHEEL = 0x0400;
    public const ushort RI_MOUSE_HWHEEL = 0x0800;

    public const uint VK_SNAPSHOT = 0x2C;
    public const uint VK_DELETE = 0x2E;
    public const uint VK_PAUSE = 0x13;
    public const uint VK_NUMLOCK = 0x90;
    public const uint VK_F1 = 0x70;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, out RAWINPUT pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);
}

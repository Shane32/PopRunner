using System.Runtime.InteropServices;

namespace PopRunner;

internal static partial class OverlayNative
{
    internal const uint WsPopup = 0x80000000;
    // TRANSPARENT on a layered window routes every pixel's mouse input to windows underneath,
    // including pixels with nonzero alpha and windows owned by another process.
    internal const uint ExtendedStyle = 0x00080000 | 0x00000020 | 0x00000008 | 0x00000080 | 0x08000000;
    internal const uint SwpNoActivate = 0x0010, SwpNoOwnerZOrder = 0x0200;
    internal const int SwHide = 0, SwShowNoActivate = 4, GwlpUserData = -21;
    internal const uint WmNcCreate = 0x0081, WmNcDestroy = 0x0082, WmNcHitTest = 0x0084,
        WmMouseActivate = 0x0021, WmEraseBackground = 0x0014, WmTimer = 0x0113,
        WmDisplayChange = 0x007E, WmSettingChange = 0x001A, WmDpiChanged = 0x02E0, WmClose = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rectangle Monitor, Work;
        internal uint Flags;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassExW(in WindowClass windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int UnregisterClassW(string className, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    internal static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtrW(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtrW(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    internal static partial int ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    internal static partial int SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SetTimer", SetLastError = true)]
    internal static partial nuint SetTimer(nint hwnd, nuint identifier, uint milliseconds, nint procedure);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    internal static partial int KillTimer(nint hwnd, nuint identifier);

    [LibraryImport("user32.dll", EntryPoint = "MonitorFromPoint")]
    internal static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    internal static partial int GetMonitorInfoW(nint monitor, ref MonitorInfo information);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint hwnd);
}

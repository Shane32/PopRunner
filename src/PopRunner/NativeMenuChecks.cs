#if POP_RUNNER_CHECKS
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PopRunner;

/// <summary>Observes and dismisses the check's popup menu on its own UI thread.</summary>
internal static unsafe partial class NativeMenuChecks
{
    private static nint owner;
    private static nuint timer;
    private static uint uiThread;
    private static bool observed;
    private static long started;
    private static Exception? failure;

    internal static void Check(nint window, Action show)
    {
        if (owner != 0) throw new InvalidOperationException("Native menu checks cannot overlap.");
        var thread = SmokeNative.GetWindowThreadProcessId(window, out var process);
        if (process != Environment.ProcessId || thread != GetCurrentThreadId())
            throw new InvalidOperationException("The menu check must run on its own window's UI thread.");

        owner = window;
        uiThread = thread;
        observed = false;
        failure = null;
        started = Stopwatch.GetTimestamp();
        try
        {
            timer = OverlayNative.SetTimer(0, 0, 25,
                (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, uint, void>)&OnTimer);
            if (timer == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Start native menu check timer");
            show();
            if (failure is { } error) throw new InvalidOperationException("Native menu check failed.", error);
            if (!observed) throw new InvalidOperationException("The native popup menu closed before the check observed it.");
        }
        finally
        {
            StopTimer();
            owner = 0;
            failure = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnTimer(nint window, uint message, nuint identifier, uint timestamp)
    {
        if (owner == 0 || identifier != timer) return;
        try
        {
            var info = new GuiThreadInfo { Size = (uint)sizeof(GuiThreadInfo) };
            if (GetGUIThreadInfo(uiThread, ref info) != 0 &&
                (info.Flags & 0x10) != 0 && info.MenuOwner == owner)
            {
                if (SmokeNative.PostMessageW(owner, 0x1F, 0, 0) == 0) // WM_CANCELMODE
                    throw new InvalidOperationException("Cannot dismiss the check's native popup menu.");
                observed = true;
                StopTimer();
            }
            else if (Stopwatch.GetElapsedTime(started).TotalSeconds > 8)
                throw new InvalidOperationException("The native popup menu did not appear within eight seconds.");
        }
        catch (Exception error)
        {
            // Exceptions must never cross an unmanaged callback boundary.
            failure = error;
            StopTimer();
            TrayNative.PostQuitMessage(1);
        }
    }

    private static void StopTimer()
    {
        if (timer == 0) return;
        OverlayNative.KillTimer(0, timer);
        timer = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal OverlayNative.Rectangle CaretBounds;
    }

    [LibraryImport("kernel32.dll")] private static partial uint GetCurrentThreadId();
    [LibraryImport("user32.dll")] private static partial int GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
}
#endif

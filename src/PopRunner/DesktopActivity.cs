using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace PopRunner;

/// <summary>Read-only checks made immediately before an automatic playback attempt.</summary>
internal static unsafe partial class DesktopActivity
{
    internal const uint RecentInputMilliseconds = 5 * 60 * 1_000;
    private const string MicrophoneRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    internal static bool IsEligible() =>
        IsCurrentSessionUnlocked() && HasRecentInput() && IsMicrophoneAvailable();

    private static bool IsCurrentSessionUnlocked()
    {
        // WTS_CURRENT_SESSION queries this process's session, including an RDP session.
        // WTSActive alone is insufficient: a locked session can still be active.
        // https://learn.microsoft.com/windows/win32/api/wtsapi32/ns-wtsapi32-wtsinfoex_level1_w
        nint buffer = 0;
        try
        {
            if (WTSQuerySessionInformationW(0, uint.MaxValue, 25, out buffer, out var bytes) == 0 ||
                buffer == 0 || bytes < sizeof(SessionInfoEx))
                return false;

            var info = (SessionInfoEx*)buffer;
            return IsInteractiveSession(info->Level, info->Data.State, info->Data.Flags);
        }
        finally
        {
            if (buffer != 0) WTSFreeMemory(buffer);
        }
    }

    internal static bool IsInteractiveSession(uint level, int state, int flags) =>
        level == 1 && state == 0 /* WTSActive */ && flags == 1 /* WTS_SESSIONSTATE_UNLOCK */;

    private static bool HasRecentInput()
    {
        var input = new LastInputInfo { Size = (uint)sizeof(LastInputInfo) };
        if (GetLastInputInfo(ref input) == 0) return false;

        // LASTINPUTINFO uses the same 32-bit uptime clock as Environment.TickCount.
        // Unsigned subtraction remains correct when that clock wraps after 49.7 days.
        // GetLastInputInfo is specific to the calling session, just like the WTS query.
        // https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getlastinputinfo
        return IsRecentInput(unchecked((uint)Environment.TickCount), input.Time);
    }

    internal static bool IsRecentInput(uint currentTick, uint lastInputTick) =>
        unchecked(currentTick - lastInputTick) <= RecentInputMilliseconds;

    private static bool IsMicrophoneAvailable()
    {
        try
        {
            // This privacy-history registry is a best-effort indication, not a documented
            // live microphone ownership API. A crashed app can leave an active record,
            // and apps bypassing Windows privacy tracking may not appear. Read both
            // user and machine stores, including packaged and NonPackaged descendants.
            // Missing stores mean no recorded use; access/read errors suppress playback.
            return IsMicrophoneStoreInactive(Registry.CurrentUser) &&
                   IsMicrophoneStoreInactive(Registry.LocalMachine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static bool IsMicrophoneStoreInactive(RegistryKey hive)
    {
        using var store = hive.OpenSubKey(MicrophoneRegistryPath, writable: false);
        return store is null || IsMicrophoneTreeInactive(store, depth: 0);
    }

    internal static bool IsMicrophoneTreeInactive(RegistryKey key, int depth = 0)
    {
        // A normal store has only a few levels. Refuse pathological input rather than
        // risking unbounded recursion inside the tray window's message handler.
        if (depth > 32 || !IsMicrophoneRecordInactive(
                key.GetValue("LastUsedTimeStart"), key.GetValue("LastUsedTimeStop")))
            return false;

        foreach (var childName in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(childName, writable: false);
            // A key disappearing while we scan is an uncertain observation. Try again
            // on the next minute instead of potentially interrupting a new capture.
            if (child is null || !IsMicrophoneTreeInactive(child, depth + 1)) return false;
        }

        return true;
    }

    internal static bool IsMicrophoneRecordInactive(object? startValue, object? stopValue)
    {
        if (!TryReadTimestamp(startValue, out var start) || !TryReadTimestamp(stopValue, out var stop))
            return false;

        // Empty/initialized permission records are not microphone use. A positive
        // start with zero stop is active; missing or older stop is also uncertain.
        // Requiring start > 0 avoids treating an unused stop=0 record as recording.
        if (start is null or 0) return true;
        return stop is > 0 && stop >= start;
    }

    private static bool TryReadTimestamp(object? value, out long? timestamp)
    {
        // Registry QWORD values are Int64; accept nonnegative DWORDs as well.
        timestamp = value switch
        {
            null => null,
            long number when number >= 0 => number,
            int number when number >= 0 => number,
            _ => null
        };
        return value is null || timestamp.HasValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfoEx
    {
        internal uint Level;
        internal SessionInfoLevel1 Data;
    }

    // Complete WTSINFOEX_LEVEL1_W layout lets the native alignment of LARGE_INTEGER
    // place Data correctly without hardcoding pointer offsets or reflection marshalling.
    [StructLayout(LayoutKind.Sequential)]
    private struct SessionInfoLevel1
    {
        internal uint SessionId;
        internal int State;
        internal int Flags;
        internal fixed char StationName[33];
        internal fixed char UserName[21];
        internal fixed char DomainName[18];
        internal long LogonTime;
        internal long ConnectTime;
        internal long DisconnectTime;
        internal long LastInputTime;
        internal long CurrentTime;
        internal uint IncomingBytes;
        internal uint OutgoingBytes;
        internal uint IncomingFrames;
        internal uint OutgoingFrames;
        internal uint IncomingCompressedBytes;
        internal uint OutgoingCompressedBytes;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetLastInputInfo(ref LastInputInfo input);

    [LibraryImport("wtsapi32.dll")]
    private static partial int WTSQuerySessionInformationW(nint server, uint sessionId, int infoClass,
        out nint buffer, out uint bytesReturned);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);
}

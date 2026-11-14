#if POP_RUNNER_CHECKS
using Microsoft.Win32;

namespace PopRunner;

/// <summary>Exercises the real automatic-play policy without using the user's preference or live activity state.</summary>
internal static class NativeAutomaticTriggerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var registrySubKey = @"Software\PopRunner.Tests\AutomaticTrigger-" + Guid.NewGuid().ToString("N");
        try
        {
            CheckPolicy(registrySubKey, check);
            CheckPreferences(registrySubKey, check);
            CheckActivity(registrySubKey, check);
            _ = DesktopActivity.IsEligible();
            check(true, "The read-only native desktop-activity probe must complete without throwing.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(registrySubKey, throwOnMissingSubKey: false);
        }
    }

    private static void CheckPolicy(string registrySubKey, Action<bool, string> check)
    {
        var calls = new List<string>();
        var playing = false;
        var eligible = true;
        var draw = 0;
        var automatic = new AutomaticTrigger(
            () => { calls.Add("eligible"); return eligible; },
            () => { calls.Add("playing"); return playing; },
            () => calls.Add("play"),
            upperBound => { calls.Add($"draw:{upperBound}"); return draw < 0 ? upperBound - 1 : draw; },
            registrySubKey);
        check(automatic.Frequency == AutomaticTriggerFrequency.Daily,
            "A missing automatic-trigger preference must default to Daily.");

        automatic.SetFrequency(AutomaticTriggerFrequency.Disabled);
        automatic.OnMinute();
        check(calls.Count == 0, "Disabled automatic play must not query activity, roll, or start playback.");

        automatic.SetFrequency(AutomaticTriggerFrequency.Hourly);
        playing = true;
        automatic.OnMinute();
        check(calls.SequenceEqual(["playing"]),
            "Automatic play must leave existing playback alone without checking activity or drawing a chance.");

        calls.Clear();
        playing = false;
        eligible = false;
        automatic.OnMinute();
        check(calls.SequenceEqual(["playing", "eligible"]),
            "An ineligible minute must not draw a chance or start playback.");

        eligible = true;
        foreach (var (frequency, denominator) in new[]
        {
            (AutomaticTriggerFrequency.Hourly, 60),
            (AutomaticTriggerFrequency.Daily, 480),
            (AutomaticTriggerFrequency.Weekly, 2400)
        })
        {
            automatic.SetFrequency(frequency);
            calls.Clear();
            draw = -1;
            automatic.OnMinute();
            check(calls.SequenceEqual(["playing", "eligible", $"draw:{denominator}"]),
                $"{frequency} must make one eligible-minute draw with probability 1/{denominator} and leave misses idle.");

            calls.Clear();
            draw = 0;
            automatic.OnMinute();
            check(calls.SequenceEqual(["playing", "eligible", $"draw:{denominator}", "play"]),
                $"{frequency} must invoke random playback exactly once when its eligible-minute draw hits.");
        }
    }

    private static void CheckPreferences(string registrySubKey, Action<bool, string> check)
    {
        AutomaticTrigger Load() => new(() => true, () => false, () => { },
            nextRandom: _ => 1, registrySubKey: registrySubKey);

        foreach (var frequency in new[]
        {
            AutomaticTriggerFrequency.Disabled,
            AutomaticTriggerFrequency.Hourly,
            AutomaticTriggerFrequency.Daily,
            AutomaticTriggerFrequency.Weekly
        })
        {
            var automatic = Load();
            automatic.SetFrequency(frequency);
            using var key = Registry.CurrentUser.OpenSubKey(registrySubKey);
            check(automatic.Frequency == frequency && Load().Frequency == frequency &&
                key is not null && key.GetValueKind(AutomaticTrigger.RegistryValueName) == RegistryValueKind.DWord &&
                key.GetValue(AutomaticTrigger.RegistryValueName) is int stored && stored == (int)frequency,
                $"The {frequency} preference must be stored as a DWORD and restored by a new instance.");
        }

        using (var key = Registry.CurrentUser.CreateSubKey(registrySubKey, writable: true))
        {
            key.SetValue(AutomaticTrigger.RegistryValueName, 99, RegistryValueKind.DWord);
            check(Load().Frequency == AutomaticTriggerFrequency.Daily,
                "An unknown persisted automatic-trigger value must fall back to Daily.");

            key.SetValue(AutomaticTrigger.RegistryValueName, "1", RegistryValueKind.String);
            check(Load().Frequency == AutomaticTriggerFrequency.Daily,
                "A preference with the wrong registry value type must fall back to Daily.");

            key.DeleteValue(AutomaticTrigger.RegistryValueName);
            check(Load().Frequency == AutomaticTriggerFrequency.Daily,
                "A missing value in an existing preference key must fall back to Daily.");
        }
    }

    private static void CheckActivity(string registrySubKey, Action<bool, string> check)
    {
        check(DesktopActivity.IsInteractiveSession(1, 0, 1),
            "An active, explicitly unlocked WTS session must permit the subsequent activity checks.");
        check(!DesktopActivity.IsInteractiveSession(1, 0, 0) &&
            !DesktopActivity.IsInteractiveSession(1, 4, 1) &&
            !DesktopActivity.IsInteractiveSession(1, 1, 1) &&
            !DesktopActivity.IsInteractiveSession(0, 0, 1) &&
            !DesktopActivity.IsInteractiveSession(1, 0, -1),
            "Locked, disconnected, inactive, or unrecognized WTS session state must suppress automatic playback.");

        var limit = DesktopActivity.RecentInputMilliseconds;
        check(DesktopActivity.IsRecentInput(1, 1) &&
            DesktopActivity.IsRecentInput(limit + 1, 1) &&
            !DesktopActivity.IsRecentInput(limit + 2, 1),
            "Recent-input detection must accept the idle threshold itself and reject older input.");
        check(DesktopActivity.IsRecentInput(100, uint.MaxValue - 99) &&
            !DesktopActivity.IsRecentInput(limit + 100, uint.MaxValue - 99) &&
            !DesktopActivity.IsRecentInput(100, 101),
            "Recent-input arithmetic must handle the 32-bit uptime wrap without treating stale or future timestamps as recent.");

        check(DesktopActivity.IsMicrophoneRecordInactive(null, null) &&
            DesktopActivity.IsMicrophoneRecordInactive(0L, 0L) &&
            DesktopActivity.IsMicrophoneRecordInactive(null, 0L),
            "An unused microphone permission record must not be mistaken for an active recording.");
        check(!DesktopActivity.IsMicrophoneRecordInactive(100L, 0L) &&
            !DesktopActivity.IsMicrophoneRecordInactive(100L, null) &&
            !DesktopActivity.IsMicrophoneRecordInactive(100L, 99L),
            "A started microphone record without a completed matching stop must suppress automatic playback.");
        check(DesktopActivity.IsMicrophoneRecordInactive(100L, 100L) &&
            DesktopActivity.IsMicrophoneRecordInactive(100L, 101L) &&
            DesktopActivity.IsMicrophoneRecordInactive(100, 101),
            "Completed microphone records must accept valid QWORD and DWORD timestamps.");
        check(!DesktopActivity.IsMicrophoneRecordInactive("100", 101L) &&
            !DesktopActivity.IsMicrophoneRecordInactive(100L, "101") &&
            !DesktopActivity.IsMicrophoneRecordInactive(-1L, 101L) &&
            !DesktopActivity.IsMicrophoneRecordInactive(null, -1),
            "Malformed microphone timestamps must fail closed rather than allowing automatic playback.");

        using var tree = Registry.CurrentUser.CreateSubKey(registrySubKey + @"\Microphone", writable: true);
        check(DesktopActivity.IsMicrophoneTreeInactive(tree),
            "An empty microphone store must not block automatic playback.");
        using var packaged = tree.CreateSubKey("Example.Package", writable: true);
        packaged.SetValue("LastUsedTimeStart", 100L, RegistryValueKind.QWord);
        packaged.SetValue("LastUsedTimeStop", 110L, RegistryValueKind.QWord);
        using var desktop = tree.CreateSubKey(@"NonPackaged\Example.Desktop", writable: true);
        desktop.SetValue("LastUsedTimeStart", 200L, RegistryValueKind.QWord);
        desktop.SetValue("LastUsedTimeStop", 0L, RegistryValueKind.QWord);
        check(!DesktopActivity.IsMicrophoneTreeInactive(tree),
            "The real registry scan must find active microphone use in nested NonPackaged entries.");

        desktop.SetValue("LastUsedTimeStop", 210L, RegistryValueKind.QWord);
        check(DesktopActivity.IsMicrophoneTreeInactive(tree),
            "Completed packaged and desktop-app microphone records must allow automatic playback.");
        packaged.SetValue("LastUsedTimeStop", 0L, RegistryValueKind.QWord);
        check(!DesktopActivity.IsMicrophoneTreeInactive(tree),
            "The real registry scan must also find active packaged-app microphone use.");
        packaged.SetValue("LastUsedTimeStop", 110L, RegistryValueKind.QWord);
        tree.SetValue("LastUsedTimeStart", 300L, RegistryValueKind.QWord);
        check(!DesktopActivity.IsMicrophoneTreeInactive(tree),
            "An incomplete microphone record at the store root must also suppress automatic playback.");
    }
}
#endif

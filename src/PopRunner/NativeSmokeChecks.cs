#if POP_RUNNER_CHECKS
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PopRunner.Core;

namespace PopRunner;

/// <summary>Checks the real native windows and rendering paths, including input through opaque pixels.</summary>
internal static unsafe class NativeSmokeChecks
{
    private static string? clickFile;

    internal static int Run(string[] args)
    {
        if (args.Length == 6 && args[0] == "--click-target") return RunClickTarget(args);
        if (args.Length != 2 || args[0] != "--self-test")
            throw new ArgumentException("Usage: PopRunner.exe [--self-test <output-directory>]");
        var directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);
        var report = Path.Combine(directory, "native-smoke-checks.txt");
        try
        {
            var checks = CheckApp(directory);
            File.WriteAllText(report, $"PASS — {checks} native checks: embedded sprites, premultiplied alpha, clipped nearest-neighbor rendering, bold default random command, configured scenario submenu and random selection, automatic-trigger menu/preferences/activity gates/odds/timer dispatch, double-click/keyboard activation with ignored single clicks, native routes and layer captures, configured entrance delays/replay with ambient bubbles and stationary actors, configured action placement, potion lifecycle/alignment, supporting actor handoff, immutable source configuration, primary-monitor/taskbar bounds, topmost/no-activate styles, cross-process clicks through opaque and transparent pixels, configured tick timing, complete traversal, and Exit.\n");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(report, error.ToString());
            return 1;
        }
    }

    private static int CheckApp(string directory)
    {
        var settingsKey = @"Software\PopRunner.Tests\Tray-" + Guid.NewGuid().ToString("N");
        try { return CheckApp(directory, settingsKey); }
        finally { Registry.CurrentUser.DeleteSubKeyTree(settingsKey, throwOnMissingSubKey: false); }
    }

    private static int CheckApp(string directory, string settingsKey)
    {
        var count = 0;
        var progress = Path.Combine(directory, "native-check-progress.txt");
        File.WriteAllText(progress, "Native self-test started.\n");
        void Phase(string phase) => File.AppendAllText(progress, $"{DateTime.UtcNow:O} {phase}\n");
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new InvalidOperationException(message);
        }
        Phase("Automatic trigger policy and activity checks");
        NativeAutomaticTriggerChecks.Run(Check);
        Phase("Image checks");
        NativeImageChecks.Run(Check, Phase);
        Phase("Load configuration");
        Check(sizeof(TrayNative.NotifyIconData) == 976 && sizeof(TrayNative.Message) == 48, "Win64 interop layout is incorrect.");
        var config = AnimationConfig.Load();
        var firstScenario = config.Scenarios[0];
        var entranceFrames = config.Clips[firstScenario.Steps[0].Clip].Frames.Select(frame => frame.Frame).ToHashSet();
        var originalTarget = config.Scenarios[0].Steps.First(s => s.UntilX.HasValue).UntilX;
        Phase("Create overlay");
        using var overlay = new DesktopOverlay(config);
        Phase("Create tray");
        var playback = new DesktopPlayback(config, overlay.Play);
        var exits = 0;
        var randomRequests = 0;
        var automaticRequests = 0;
        var activityEligible = false;
        var automaticDraw = 1;
        var automaticRolls = 0;
        var automaticBound = 0;
        var automaticTrigger = new AutomaticTrigger(() => activityEligible, () => overlay.IsPlaying,
            () => { automaticRequests++; playback.PlayRandom(); },
            bound => { automaticRolls++; automaticBound = bound; return automaticDraw; }, settingsKey);
        using var tray = new TrayHost(config.Scenarios, () => { randomRequests++; playback.PlayRandom(); }, playback.Play,
            () => exits++, automaticTrigger);
        Check(tray.TrayIconCreated && tray.IconHandle != 0, "Shell did not accept the Prince tray icon.");
        Check(SmokeNative.IsWindowVisible(tray.Handle) == 0 && !overlay.IsPlaying, "Startup should only show a tray icon.");
        Check(SmokeNative.GetMenuItemCount(tray.MenuHandle) == 6, "Tray menu must contain random play, play and automatic submenus, separator, About and Exit.");
        Check(SmokeNative.GetMenuDefaultItem(tray.MenuHandle, 0, 0) == TrayHost.PlayRandomCommand &&
            (SmokeNative.GetMenuState(tray.MenuHandle, 0, 0x400) & 0x1000) != 0,
            "Play random must be the bold default tray command.");
        Check(SmokeNative.GetSubMenu(tray.MenuHandle, 1) == tray.ScenarioMenuHandle &&
            SmokeNative.GetMenuItemCount(tray.ScenarioMenuHandle) == config.Scenarios.Count,
            "The native play submenu must expose all configured scenarios.");
        var text = stackalloc char[128];
        var length = SmokeNative.GetMenuStringW(tray.MenuHandle, 0, text, 128, 0x400);
        Check(new string(text, 0, length) == "Play Random", "Tray default random command has the wrong text.");
        length = SmokeNative.GetMenuStringW(tray.MenuHandle, 1, text, 128, 0x400);
        Check(new string(text, 0, length) == "Play", "Tray play submenu has the wrong text.");
        length = SmokeNative.GetMenuStringW(tray.MenuHandle, 2, text, 128, 0x400);
        Check(new string(text, 0, length) == "Automatic Trigger" &&
            SmokeNative.GetSubMenu(tray.MenuHandle, 2) == tray.AutomaticMenuHandle &&
            SmokeNative.GetMenuItemCount(tray.AutomaticMenuHandle) == 4,
            "The automatic trigger submenu is missing.");
        var frequencies = new[] { AutomaticTriggerFrequency.Hourly, AutomaticTriggerFrequency.Daily,
            AutomaticTriggerFrequency.Weekly, AutomaticTriggerFrequency.Disabled };
        for (var index = 0; index < frequencies.Length; index++)
        {
            length = SmokeNative.GetMenuStringW(tray.AutomaticMenuHandle, (uint)index, text, 128, 0x400);
            Check(new string(text, 0, length) == frequencies[index].ToString() &&
                SmokeNative.GetMenuItemID(tray.AutomaticMenuHandle, index) ==
                    TrayHost.FirstAutomaticTriggerCommand + (uint)frequencies[index],
                "An automatic trigger menu choice is missing or mislabeled.");
        }
        Check(automaticTrigger.Frequency == AutomaticTriggerFrequency.Weekly &&
            (SmokeNative.GetMenuState(tray.AutomaticMenuHandle,
                TrayHost.FirstAutomaticTriggerCommand + (uint)AutomaticTriggerFrequency.Weekly, 0) & 8) != 0,
            "Automatic playback must default to Weekly with a menu selection mark.");
        for (var index = 0; index < config.Scenarios.Count; index++)
        {
            length = SmokeNative.GetMenuStringW(tray.ScenarioMenuHandle, (uint)index, text, 128, 0x400);
            Check(SmokeNative.GetMenuItemID(tray.ScenarioMenuHandle, index) == TrayHost.FirstScenarioCommand + (uint)index &&
                new string(text, 0, length) == $"{index + 1}. {config.Scenarios[index].Title.Replace("&", "&&", StringComparison.Ordinal)}",
                $"Scenario {index + 1} is missing from the native play submenu.");
        }
        length = SmokeNative.GetMenuStringW(tray.MenuHandle, 4, text, 128, 0x400);
        Check(new string(text, 0, length).Contains("About", StringComparison.Ordinal), "Tray About command is missing.");
        length = SmokeNative.GetMenuStringW(tray.MenuHandle, 5, text, 128, 0x400);
        Check(new string(text, 0, length).Contains("xit", StringComparison.Ordinal), "Tray Exit command is missing.");
        Phase("Automatic trigger commands and timer dispatch");
        foreach (var frequency in frequencies)
        {
            SmokeNative.SendMessageW(tray.Handle, TrayNative.WmCommand,
                TrayHost.FirstAutomaticTriggerCommand + (uint)frequency, 0);
            Check(automaticTrigger.Frequency == frequency && automaticRequests == 0 && automaticRolls == 0,
                "Changing frequency must persist the choice without playing or drawing immediately.");
            var restored = new AutomaticTrigger(() => false, () => false, () => { }, registrySubKey: settingsKey);
            Check(restored.Frequency == frequency, "A tray menu selection did not persist in the registry.");
            foreach (var item in frequencies)
                Check(((SmokeNative.GetMenuState(tray.AutomaticMenuHandle,
                    TrayHost.FirstAutomaticTriggerCommand + (uint)item, 0) & 8) != 0) == (item == frequency),
                    "The automatic trigger menu must mark exactly the selected frequency.");
            Check((OverlayNative.KillTimer(tray.Handle, TrayHost.AutomaticTriggerTimer) != 0) ==
                (frequency != AutomaticTriggerFrequency.Disabled),
                "The minute timer must run only while automatic playback is enabled.");
        }
        SmokeNative.SendMessageW(tray.Handle, TrayNative.WmCommand,
            TrayHost.FirstAutomaticTriggerCommand + (uint)AutomaticTriggerFrequency.Daily, 0);
        SmokeNative.SendMessageW(tray.Handle, OverlayNative.WmTimer, TrayHost.AutomaticTriggerTimer, 0);
        Check(automaticRolls == 0 && automaticRequests == 0, "The tray minute handler must skip ineligible activity.");
        activityEligible = true;
        SmokeNative.SendMessageW(tray.Handle, OverlayNative.WmTimer, TrayHost.AutomaticTriggerTimer, 0);
        Check(automaticRolls == 1 && automaticBound == 480 && automaticRequests == 0,
            "An eligible Daily minute must make one draw without playing on a miss.");
        automaticDraw = 0;
        SmokeNative.SendMessageW(tray.Handle, OverlayNative.WmTimer, TrayHost.AutomaticTriggerTimer, 0);
        OverlayNative.KillTimer(overlay.Handle, 1);
        Check(automaticRolls == 2 && automaticRequests == 1 && overlay.IsPlaying &&
            playback.RandomScenarioIndices.Contains(overlay.CurrentScenarioIndex),
            "A successful automatic draw must start an eligible random scene.");
        SmokeNative.SendMessageW(tray.Handle, OverlayNative.WmTimer, TrayHost.AutomaticTriggerTimer, 0);
        Check(automaticRolls == 2 && automaticRequests == 1,
            "An automatic minute must not restart an animation already playing.");
        SmokeNative.SendMessageW(overlay.Handle, TrayNative.WmClose, 0, 0);
        SmokeNative.SendMessageW(tray.Handle, TrayNative.WmCommand,
            TrayHost.FirstAutomaticTriggerCommand + (uint)AutomaticTriggerFrequency.Disabled, 0);
        SmokeNative.SendMessageW(tray.Handle, OverlayNative.WmTimer, TrayHost.AutomaticTriggerTimer, 0);
        Check(!overlay.IsPlaying && automaticRolls == 2 && automaticRequests == 1,
            "A stale timer message must not play after automatic triggering is disabled.");
        var selected = -1;
        var selector = new DesktopPlayback(config, index => selected = index);
        var eligible = config.Desktop.RandomScenarioIds;
        Check(selector.RandomScenarioIndices.Select(index => config.Scenarios[index].Id).SequenceEqual(eligible),
            "The random pool must match the configured scenario IDs.");
        for (var draw = 0; draw < 128; draw++)
        {
            selector.PlayRandom();
            Check(selector.RandomScenarioIndices.Contains(selected), "Random selection chose a scenario outside its configured pool.");
        }
        foreach (var invalidIndex in new[] { -1, config.Scenarios.Count })
        {
            try { selector.Play(invalidIndex); Check(false, "Selection accepted a nonexistent scenario index."); }
            catch (ArgumentOutOfRangeException) { Check(true, "Selection rejects nonexistent scenario indices."); }
        }
        var invalidPool = AnimationConfig.Load();
        invalidPool.Desktop.RandomScenarioIds = [];
        try { _ = new DesktopPlayback(invalidPool, _ => { }); Check(false, "Selection accepted an empty random pool."); }
        catch (InvalidDataException) { Check(true, "Selection rejects an empty random pool."); }
        invalidPool.Desktop.RandomScenarioIds = ["missing-scenario"];
        try { _ = new DesktopPlayback(invalidPool, _ => { }); Check(false, "Selection accepted an unknown random scenario."); }
        catch (InvalidDataException) { Check(true, "Selection rejects unknown random scenarios."); }

        Phase("Popup menu");
        NativeMenuChecks.Check(tray.Handle, () =>
            SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, unchecked((nuint)uint.MaxValue), (nint)(0x10000 | 0x7B)));
        Phase("Popup menu completed");
        Check(true, "Native right-click popup opened and dismissed.");

        Phase("Playback and click-through");
        var previousForeground = SmokeNative.GetForegroundWindow();
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x400)); // NIN_SELECT
        Check(!overlay.IsPlaying && randomRequests == 0, "A single-click selection must not start playback.");
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x202)); // WM_LBUTTONUP
        Check(!overlay.IsPlaying && randomRequests == 0, "A single mouse-button release must not start playback.");
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x203)); // WM_LBUTTONDBLCLK
        OverlayNative.KillTimer(overlay.Handle, 1); // deterministic test ticks
        Check(overlay.IsPlaying && SmokeNative.IsWindowVisible(overlay.Handle) != 0, "Tray double-click did not start the overlay.");
        Check(randomRequests == 1 && playback.RandomScenarioIndices.Contains(overlay.CurrentScenarioIndex),
            "Tray double-click must choose one eligible random scenario.");
        overlay.AdvanceOneTick();
        var doubleClickedScenario = overlay.CurrentScenarioIndex;
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x400)); // trailing NIN_SELECT
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x202)); // trailing WM_LBUTTONUP
        Check(randomRequests == 1 && overlay.ElapsedTicks == 1 && overlay.CurrentScenarioIndex == doubleClickedScenario,
            "The trailing single-click notifications must not restart a double-clicked animation.");
        Check(SmokeNative.GetForegroundWindow() == previousForeground, "Overlay stole foreground activation.");
        SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.PlayRandomCommand, 0);
        OverlayNative.KillTimer(overlay.Handle, 1);
        Check(randomRequests == 2 && playback.RandomScenarioIndices.Contains(overlay.CurrentScenarioIndex),
            "The native random menu command did not use the shared random selection.");
        SmokeNative.SendMessageW(tray.Handle, TrayHost.CallbackMessage, 0, (nint)(0x10000 | 0x401)); // NIN_KEYSELECT
        OverlayNative.KillTimer(overlay.Handle, 1);
        Check(randomRequests == 3 && playback.RandomScenarioIndices.Contains(overlay.CurrentScenarioIndex),
            "Keyboard tray activation must still choose an eligible random scenario.");
        SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.FirstScenarioCommand, 0);
        OverlayNative.KillTimer(overlay.Handle, 1); // deterministic first route for input checks
        var layout = overlay.Layout;
        var expectedStyles = (nuint)OverlayNative.ExtendedStyle;
        var actualStyles = (nuint)OverlayNative.GetWindowLongPtrW(overlay.Handle, -20);
        Check((actualStyles & expectedStyles) == expectedStyles, "Overlay lacks layered/transparent/topmost/tool/no-activate styles.");
        Check(SmokeNative.SendMessageW(overlay.Handle, 0x84, 0, 0) == -1, "Overlay hit testing did not return HTTRANSPARENT.");
        Check(SmokeNative.GetWindowRect(overlay.Handle, out var bounds) != 0, "Cannot inspect native overlay bounds.");
        Check(bounds.Left == layout.MonitorLeft && bounds.Right == layout.MonitorLeft + layout.MonitorWidth,
            "Overlay extends beyond the primary monitor's horizontal bounds.");
        Check(bounds.Bottom <= layout.MonitorTop + layout.MonitorHeight && bounds.Top >= layout.MonitorTop,
            "Overlay exceeds the primary monitor's vertical bounds.");
        var expectedFloor = Math.Clamp(Math.Min(layout.MonitorTop + layout.MonitorHeight, layout.WorkTop + layout.WorkHeight) -
            config.Desktop.TaskbarGapPixels, layout.MonitorTop + 1, layout.MonitorTop + layout.MonitorHeight);
        Check(layout.Top + overlay.FloorPixelY == expectedFloor && bounds.Bottom - expectedFloor == overlay.BottomPaddingPixels,
            "Below-floor sprite padding lifted the DOS floor away from the configured taskbar edge.");
        Check(entranceFrames.Contains(overlay.CurrentFrame) && FindPixel(overlay.Pixels, true) < 0,
            "The Prince must begin already running fully offscreen.");
        var entryTicks = AdvanceToVisiblePrince(overlay);
        Check(entryTicks > 0 && entranceFrames.Contains(overlay.CurrentFrame),
            "The Prince did not enter the monitor in his running cycle.");
        var pixels = overlay.Pixels;
        var opaque = FindPixel(pixels, true);
        var empty = FindPixel(pixels, false);
        Check(opaque >= 0 && empty >= 0, "The overlay needs both opaque Prince pixels and transparent surroundings.");
        NativeImageChecks.SavePng(Path.Combine(directory, "overlay.png"), layout.Width, layout.Height, pixels);
        SavePrinceCrop(directory, layout, pixels, overlay.NativeFrameBounds);
        CheckClicksThrough(overlay, directory, entryTicks, opaque, empty, Check);

        // The renderer presents every tick, including the original offscreen exit and completion hold.
        var ticks = 0;
        while (overlay.IsPlaying && ticks++ < 10000) overlay.AdvanceOneTick();
        Check(overlay.Completed && !overlay.IsPlaying && SmokeNative.IsWindowVisible(overlay.Handle) == 0,
            "Completed animation did not hide its window.");
        Check(overlay.LastError == null, "A native callback reported an animation failure: " + overlay.LastError);
        Check(config.Scenarios[0].Steps.First(s => s.UntilX.HasValue).UntilX == originalTarget,
            "Desktop traversal changed the shared source scenario.");
        SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.FirstScenarioCommand, 0);
        OverlayNative.KillTimer(overlay.Handle, 1);
        Check(overlay.ElapsedTicks == 0 && overlay.IsPlaying && overlay.CurrentDirection == firstScenario.Direction &&
            entranceFrames.Contains(overlay.CurrentFrame) && FindPixel(overlay.Pixels, true) < 0,
            "Explicit tray replay did not restart the running offscreen entrance.");
        for (var i = 0; i < 12; i++) overlay.AdvanceOneTick();
        Check(overlay.ElapsedTicks == 12 && entranceFrames.Contains(overlay.CurrentFrame), "Explicit native ticks did not preserve the configured entrance clip.");
        SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.FirstScenarioCommand, 0);
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 400)
        {
            while (SmokeNative.PeekMessageW(out var message, 0, 0, 0, 1) != 0)
            {
                TrayNative.TranslateMessage(in message);
                TrayNative.DispatchMessage(in message);
            }
            Thread.Sleep(1);
        }
        OverlayNative.KillTimer(overlay.Handle, 1);
        var expectedTimerTicks = timer.Elapsed.TotalSeconds * overlay.Engine!.TicksPerSecond;
        Check(Math.Abs(overlay.ElapsedTicks - expectedTimerTicks) <= Math.Max(2, expectedTimerTicks * 0.3) && overlay.LastError == null,
            "The real Win32 timer did not advance the configured fixed clock.");
        Phase("All native scenarios");
        NativeScenarioChecks.Run(overlay, tray, config, directory, Check);
        Phase("Potion floor alignment options");
        CheckPotionFloorOptions(overlay, config, directory, Check);
        Phase("Exit");
        SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.ExitCommand, 0);
        Check(tray.Run() == 0 && exits == 1, "Tray Exit did not terminate its native message loop cleanly.");
        Phase("Completed");
        return count;
    }

    private static void CheckPotionFloorOptions(DesktopOverlay overlay, AnimationConfig config, string directory, Action<bool, string> check)
    {
        var scenarioIndex = config.Scenarios.FindIndex(scenario => scenario.HasPotion);
        if (scenarioIndex < 0 || config.PotionGraphics is not { } potion) return;
        var originalAlignment = potion.AlignWithFeet;
        OverlayBounds? unalignedBody = null, unalignedBubbles = null;
        var unalignedFloor = 0;
        try
        {
            foreach (var alignWithFeet in new[] { false, true })
            {
                potion.AlignWithFeet = alignWithFeet;
                overlay.Play(scenarioIndex);
                OverlayNative.KillTimer(overlay.Handle, 1);
                var layout = overlay.Layout;
                var floor = layout.Top + overlay.FloorPixelY;
                var expectedFloor = Math.Clamp(Math.Min(layout.MonitorTop + layout.MonitorHeight, layout.WorkTop + layout.WorkHeight) -
                    config.Desktop.TaskbarGapPixels, layout.MonitorTop + 1, layout.MonitorTop + layout.MonitorHeight);
                check(floor == expectedFloor, "Potion alignment settings must not move the desktop floor away from the taskbar.");
                var body = overlay.NativePotionBounds ?? throw new InvalidOperationException("Potion alignment check has no vessel layer.");
                var bubbles = overlay.NativeBubbleBounds ?? throw new InvalidOperationException("Potion alignment check has no bubble layer.");
                check(layout.Top >= layout.MonitorTop && layout.Top + layout.Height <= layout.MonitorTop + layout.MonitorHeight,
                    "Potion alignment must keep the overlay inside the primary monitor.");
                if (!alignWithFeet)
                {
                    unalignedBody = body;
                    unalignedBubbles = bubbles;
                    unalignedFloor = floor;
                }
                else
                {
                    check(body.Top + body.Height == floor && floor == unalignedFloor,
                        "Feet alignment must place the vessel bottom on the same fixed DOS floor.");
                    var expectedShift = potion.BodyOffsetY * layout.ScaleY;
                    var tolerance = layout.ScaleY + 1; // One source-pixel quantization step, plus destination rounding.
                    check(Math.Abs(unalignedBody!.Value.Top - body.Top - expectedShift) <= tolerance &&
                        Math.Abs(unalignedBubbles!.Value.Top - bubbles.Top - expectedShift) <= tolerance,
                        "Feet alignment must shift both original potion layers together without changing their relative offsets.");
                }
                NativeImageChecks.SavePng(Path.Combine(directory, $"potion-align-feet-{alignWithFeet.ToString().ToLowerInvariant()}.png"),
                    layout.Width, layout.Height, overlay.Pixels);
            }
        }
        finally
        {
            potion.AlignWithFeet = originalAlignment;
            SmokeNative.SendMessageW(overlay.Handle, 0x10, 0, 0); // Stop only the self-test overlay.
        }
    }

    private static int AdvanceToVisiblePrince(DesktopOverlay overlay)
    {
        var ticks = 0;
        while (overlay.IsPlaying && ticks < 1000 &&
            (overlay.NativeFrameBounds.Left < overlay.Layout.Left || FindPixel(overlay.Pixels, true) < 0))
        {
            overlay.AdvanceOneTick();
            ticks++;
        }
        return ticks;
    }

    private static void CheckClicksThrough(DesktopOverlay overlay, string directory, int entryTicks, int opaque, int empty, Action<bool, string> check)
    {
        var readyFile = Path.Combine(directory, "click-target.txt");
        File.Delete(readyFile);
        File.Delete(readyFile + ".clicks");
        var layout = overlay.Layout;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "--click-target", readyFile, layout.Left.ToString(CultureInfo.InvariantCulture),
                     layout.Top.ToString(CultureInfo.InvariantCulture), layout.Width.ToString(CultureInfo.InvariantCulture), layout.Height.ToString(CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(arg);
        using var target = Process.Start(start) ?? throw new InvalidOperationException("Cannot start click-through test target.");
        nint targetWindow = 0;
        SmokeNative.GetCursorPos(out var oldCursor);
        var oldForeground = SmokeNative.GetForegroundWindow();
        try
        {
            WaitUntil(() =>
            {
                if (target.HasExited) return true;
                try
                {
                    if (!long.TryParse(ReadSharedText(readyFile), NumberStyles.Integer, CultureInfo.InvariantCulture, out var handle) || handle == 0)
                        return false;
                    targetWindow = (nint)handle;
                    return true;
                }
                catch (IOException) { return false; }
            }, "Separate-process click target did not start.");
            check(!target.HasExited, "Separate-process click target exited before the test.");
            // Raise the overlay above the test window without activation, then freeze its clock.
            overlay.Play();
            OverlayNative.KillTimer(overlay.Handle, 1);
            for (var tick = 0; tick < entryTicks; tick++) overlay.AdvanceOneTick();
            check(overlay.Pixels[opaque * 4 + 3] != 0 && overlay.Pixels[empty * 4 + 3] == 0,
                "Click-through test did not restore its visible Prince and transparent test pixels.");
            var expectedClicks = 0;
            var inputs = stackalloc SmokeNative.Input[2];
            foreach (var pixel in new[] { opaque, empty })
            {
                var point = new TrayNative.Point { X = layout.Left + pixel % layout.Width, Y = layout.Top + pixel / layout.Width };
                check(SmokeNative.WindowFromPoint(point) == targetWindow,
                    "An overlay pixel intercepts hit testing instead of reaching the other process.");
                check(SmokeNative.SetCursorPos(point.X, point.Y) != 0, "Could not position cursor over the test target.");
                inputs[0] = new SmokeNative.Input { Type = 0, Mouse = new SmokeNative.MouseInput { Flags = 0x0002 } };
                inputs[1] = new SmokeNative.Input { Type = 0, Mouse = new SmokeNative.MouseInput { Flags = 0x0004 } };
                check(SmokeNative.SendInput(2, inputs, sizeof(SmokeNative.Input)) == 2, "Windows rejected the click-through test input.");
                expectedClicks++;
                WaitUntil(() =>
                {
                    try { return ReadSharedText(readyFile + ".clicks").Count(character => character == '\n') >= expectedClicks; }
                    catch (IOException) { return false; }
                }, "A click through the overlay did not reach the separate target process.");
                check(true, "Cross-process mouse click delivered.");
            }
        }
        finally
        {
            SmokeNative.SetCursorPos(oldCursor.X, oldCursor.Y);
            if (targetWindow != 0) SmokeNative.PostMessageW(targetWindow, 0x10, 0, 0);
            if (!target.WaitForExit(3000)) target.Kill();
            if (oldForeground != 0) TrayNative.SetForegroundWindow(oldForeground);
        }
    }

    private static int RunClickTarget(string[] args)
    {
        clickFile = args[1] + ".clicks";
        const string className = "PopRunner.ClickThroughTestTarget";
        var instance = OverlayNative.GetModuleHandleW(null);
        fixed (char* name = className)
        {
            var windowClass = new OverlayNative.WindowClass
            {
                Size = (uint)sizeof(OverlayNative.WindowClass),
                Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&ClickTargetProcedure,
                Instance = instance, ClassName = (nint)name, Background = 6
            };
            if (OverlayNative.RegisterClassExW(in windowClass) == 0) return 2;
        }
        var hwnd = OverlayNative.CreateWindowExW(0x08000080, className, "PopRunner input test", 0x80000000,
            int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture),
            int.Parse(args[4], CultureInfo.InvariantCulture), int.Parse(args[5], CultureInfo.InvariantCulture), 0, 0, instance, 0);
        if (hwnd == 0) return 3;
        OverlayNative.SetWindowPos(hwnd, -1, 0, 0, 0, 0, 0x53); // TOPMOST, NOSIZE/NOMOVE/NOACTIVATE/SHOWWINDOW
        File.WriteAllText(args[1], ((long)hwnd).ToString(CultureInfo.InvariantCulture));
        while (TrayNative.GetMessage(out var message, 0, 0, 0) > 0)
        {
            TrayNative.TranslateMessage(in message);
            TrayNative.DispatchMessage(in message);
        }
        OverlayNative.UnregisterClassW(className, instance);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint ClickTargetProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == 0x202) { File.AppendAllText(clickFile!, "click\n"); return 0; }
            if (message == 0x10) { OverlayNative.DestroyWindow(hwnd); return 0; }
            if (message == 2) { TrayNative.PostQuitMessage(0); return 0; }
        }
        catch { TrayNative.PostQuitMessage(4); return 0; }
        return OverlayNative.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static string ReadSharedText(string path)
    {
        // The target may still be writing; polling must not block its file access.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void WaitUntil(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.ElapsedMilliseconds > 8000) throw new InvalidOperationException(failure);
            Thread.Sleep(10);
        }
    }

    private static int FindPixel(ReadOnlySpan<byte> pixels, bool opaque)
    {
        for (var i = 0; i < pixels.Length; i += 4)
            if (opaque ? pixels[i + 3] == 255 : pixels[i + 3] == 0) return i / 4;
        return -1;
    }

    private static void SavePrinceCrop(string directory, DesktopLayout layout, ReadOnlySpan<byte> pixels, OverlayBounds bounds)
    {
        var left = Math.Max(0, bounds.Left - layout.Left - 4);
        var top = Math.Max(0, bounds.Top - layout.Top - 4);
        var right = Math.Min(layout.Width, bounds.Left - layout.Left + bounds.Width + 4);
        var bottom = Math.Min(layout.Height, bounds.Top - layout.Top + bounds.Height + 4);
        var width = right - left;
        var height = bottom - top;
        var crop = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            pixels.Slice(((top + y) * layout.Width + left) * 4, width * 4).CopyTo(crop.AsSpan(y * width * 4));
        NativeImageChecks.SavePng(Path.Combine(directory, "prince.png"), width, height, crop);
    }
}

internal static unsafe partial class SmokeNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct Input { [FieldOffset(0)] internal uint Type; [FieldOffset(8)] internal MouseInput Mouse; }

    [LibraryImport("user32.dll")] internal static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll")] internal static partial int GetWindowRect(nint window, out OverlayNative.Rectangle rect);
    [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll")] internal static partial int GetMenuItemCount(nint menu);
    [LibraryImport("user32.dll")] internal static partial uint GetMenuDefaultItem(nint menu, uint byPosition, uint flags);
    [LibraryImport("user32.dll")] internal static partial uint GetMenuState(nint menu, uint item, uint flags);
    [LibraryImport("user32.dll")] internal static partial uint GetMenuItemID(nint menu, int position);
    [LibraryImport("user32.dll")] internal static partial nint GetSubMenu(nint menu, int position);
    [LibraryImport("user32.dll")] internal static partial int GetMenuStringW(nint menu, uint item, char* text, int maximum, uint flags);
    [LibraryImport("user32.dll")] internal static partial nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial int PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial nint WindowFromPoint(TrayNative.Point point);
    [LibraryImport("user32.dll")] internal static partial int GetCursorPos(out TrayNative.Point point);
    [LibraryImport("user32.dll")] internal static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll")] internal static partial int PeekMessageW(out TrayNative.Message message, nint window, uint minimum, uint maximum, uint remove);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial uint SendInput(uint count, Input* inputs, int size);
}
#endif

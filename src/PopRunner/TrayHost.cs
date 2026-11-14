using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PopRunner.Core;

namespace PopRunner;

/// <summary>A shell notification icon and an ordinary hidden Win32 owner window.</summary>
internal sealed unsafe class TrayHost : IDisposable
{
    internal const uint CallbackMessage = TrayNative.TrayCallbackMessage;
    internal const uint AboutCommand = 1;
    internal const uint ExitCommand = 2;
    internal const uint PlayRandomCommand = 3;
    internal const uint FirstAutomaticTriggerCommand = 10;
    internal const uint FirstScenarioCommand = 100;
    internal const nuint AutomaticTriggerTimer = 2;
    private const uint AutomaticTriggerIntervalMilliseconds = 60_000;
    private const string ClassName = "PopRunner.TrayHost";
    private static TrayHost? current;
    private readonly Action onPlayRandom;
    private readonly Action<int> onPlay;
    private readonly int scenarioCount;
    private readonly Action onExit;
    private readonly AutomaticTrigger automaticTrigger;
    private readonly nint instance;
    private readonly uint taskbarCreated;
    private TrayNative.NotifyIconData notification;
    private bool classRegistered;
    private bool scenarioMenuAttached;
    private bool automaticMenuAttached;
    private bool exitRequested;
    private bool disposed;

    public nint Handle { get; private set; }
    internal nint MenuHandle { get; private set; }
    internal nint ScenarioMenuHandle { get; private set; }
    internal nint AutomaticMenuHandle { get; private set; }
    internal nint IconHandle { get; private set; }
    internal bool TrayIconCreated { get; private set; }

    public TrayHost(IReadOnlyList<Scenario> scenarios, Action onPlayRandom, Action<int> onPlay, Action onExit,
        AutomaticTrigger automaticTrigger)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(onPlayRandom);
        ArgumentNullException.ThrowIfNull(onPlay);
        ArgumentNullException.ThrowIfNull(onExit);
        ArgumentNullException.ThrowIfNull(automaticTrigger);
        if (current is not null) throw new InvalidOperationException("Only one tray host can be active.");
        this.onPlayRandom = onPlayRandom;
        this.onPlay = onPlay;
        scenarioCount = scenarios.Count;
        this.onExit = onExit;
        this.automaticTrigger = automaticTrigger;
        instance = TrayNative.GetModuleHandle(null);
        taskbarCreated = TrayNative.RegisterWindowMessage("TaskbarCreated");
        current = this;
        try
        {
            IconHandle = CreatePrinceIcon();
            fixed (char* className = ClassName)
            {
                var windowClass = new TrayNative.WindowClass
                {
                    Size = (uint)sizeof(TrayNative.WindowClass),
                    Procedure = &WindowProcedure,
                    Instance = instance,
                    ClassName = className,
                    Icon = IconHandle,
                    SmallIcon = IconHandle
                };
                if (TrayNative.RegisterClassEx(in windowClass) == 0) ThrowLastError("Register tray window class");
                classRegistered = true;
            }

            // A top-level window receives Explorer's TaskbarCreated broadcast; message-only windows do not.
            Handle = TrayNative.CreateWindowEx(0x00000080, ClassName, "PopRunner tray", 0x80000000,
                0, 0, 0, 0, 0, 0, instance, 0); // WS_EX_TOOLWINDOW, WS_POPUP; never shown.
            if (Handle == 0) ThrowLastError("Create tray owner window");
            MenuHandle = TrayNative.CreatePopupMenu();
            if (MenuHandle == 0) ThrowLastError("Create tray menu");
            ScenarioMenuHandle = TrayNative.CreatePopupMenu();
            if (ScenarioMenuHandle == 0) ThrowLastError("Create scenario submenu");
            for (var index = 0; index < scenarios.Count; index++)
            {
                var label = $"{index + 1}. {scenarios[index].Title.Replace("&", "&&", StringComparison.Ordinal)}";
                if (TrayNative.AppendMenu(ScenarioMenuHandle, 0, FirstScenarioCommand + (uint)index, label) == 0)
                    ThrowLastError("Populate scenario submenu");
            }
            if (TrayNative.AppendMenu(MenuHandle, 0, PlayRandomCommand, "Play Random") == 0 ||
                TrayNative.AppendMenu(MenuHandle, 0x00000010, (nuint)ScenarioMenuHandle, "Play") == 0)
                ThrowLastError("Populate tray playback menu");
            scenarioMenuAttached = true;
            AutomaticMenuHandle = TrayNative.CreatePopupMenu();
            if (AutomaticMenuHandle == 0) ThrowLastError("Create automatic trigger submenu");
            foreach (var frequency in new[] { AutomaticTriggerFrequency.Hourly, AutomaticTriggerFrequency.Daily,
                AutomaticTriggerFrequency.Weekly, AutomaticTriggerFrequency.Disabled })
            {
                if (TrayNative.AppendMenu(AutomaticMenuHandle, 0, FirstAutomaticTriggerCommand + (uint)frequency,
                    frequency.ToString()) == 0)
                    ThrowLastError("Populate automatic trigger submenu");
            }
            if (TrayNative.AppendMenu(MenuHandle, 0x00000010, (nuint)AutomaticMenuHandle, "Automatic Trigger") == 0)
                ThrowLastError("Attach automatic trigger submenu");
            automaticMenuAttached = true;
            UpdateAutomaticMenu();
            if (TrayNative.SetMenuDefaultItem(MenuHandle, PlayRandomCommand, 0) == 0 ||
                TrayNative.AppendMenu(MenuHandle, 0x00000800, 0, "") == 0 ||
                TrayNative.AppendMenu(MenuHandle, 0, AboutCommand, "&About PopRunner...") == 0 ||
                TrayNative.AppendMenu(MenuHandle, 0, ExitCommand, "E&xit") == 0)
                ThrowLastError("Populate tray menu");

            notification = new TrayNative.NotifyIconData
            {
                Size = (uint)sizeof(TrayNative.NotifyIconData),
                Window = Handle,
                Id = 1,
                Flags = 0x00000001 | 0x00000002 | 0x00000004 | 0x00000080,
                CallbackMessage = CallbackMessage,
                Icon = IconHandle
            };
            const string tip = "PopRunner - double-click to play a random animation";
            fixed (char* target = notification.Tip)
                tip.AsSpan().CopyTo(new Span<char>(target, 128));
            AddTrayIcon();
            UpdateAutomaticTimer();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Run()
    {
        while (true)
        {
            var result = TrayNative.GetMessage(out var message, 0, 0, 0);
            if (result == -1) ThrowLastError("Read window message");
            if (result == 0) return unchecked((int)message.WParam);
            TrayNative.TranslateMessage(in message);
            TrayNative.DispatchMessage(in message);
        }
    }

    public void RequestExit()
    {
        if (exitRequested) return;
        exitRequested = true;
        try { onExit(); }
        finally { TrayNative.PostQuitMessage(0); }
    }

    internal void ShowAbout() => TrayNative.MessageBox(Handle,
        "PopRunner\n\nA native Win32, Native AOT desktop companion.\n\n" +
        "Double-click the tray icon to play a random animation above the taskbar.\n" +
        "Right-click for Play Random, Play, Automatic Trigger, or Exit.\n\n" +
        "Prince of Persia artwork © its original copyright holders.\n\n" +
        "Not for distribution.",
        "About PopRunner", 0x00000040);

    internal void ShowMenu()
    {
        if (TrayNative.GetCursorPos(out var position) != 0)
            ShowMenu(position.X, position.Y);
    }

    private void ShowMenu(int x, int y)
    {
        TrayNative.SetForegroundWindow(Handle);
        var command = TrayNative.TrackPopupMenuEx(MenuHandle, 0x00000182, x, y, Handle, 0);
        // Without WM_NULL the notification-area popup can remain stuck after dismissal.
        TrayNative.PostMessage(Handle, 0, 0, 0);
        DispatchCommand(command);
    }

    private void DispatchCommand(uint command)
    {
        if (command == AboutCommand) ShowAbout();
        else if (command == ExitCommand) RequestExit();
        else if (command == PlayRandomCommand) onPlayRandom();
        else if (command >= FirstAutomaticTriggerCommand && command <= FirstAutomaticTriggerCommand + 3)
        {
            automaticTrigger.SetFrequency((AutomaticTriggerFrequency)(command - FirstAutomaticTriggerCommand));
            UpdateAutomaticMenu();
            UpdateAutomaticTimer();
        }
        else if (command >= FirstScenarioCommand && command - FirstScenarioCommand < (uint)scenarioCount)
            onPlay((int)(command - FirstScenarioCommand));
    }

    private void UpdateAutomaticMenu()
    {
        if (TrayNative.CheckMenuRadioItem(AutomaticMenuHandle, FirstAutomaticTriggerCommand,
            FirstAutomaticTriggerCommand + 3, FirstAutomaticTriggerCommand + (uint)automaticTrigger.Frequency, 0) == 0)
            ThrowLastError("Update automatic trigger selection");
    }

    private void UpdateAutomaticTimer()
    {
        OverlayNative.KillTimer(Handle, AutomaticTriggerTimer);
        if (automaticTrigger.Frequency != AutomaticTriggerFrequency.Disabled &&
            OverlayNative.SetTimer(Handle, AutomaticTriggerTimer, AutomaticTriggerIntervalMilliseconds, 0) == 0)
            ThrowLastError("Start automatic trigger timer");
    }

    private void AddTrayIcon()
    {
        if (TrayNative.ShellNotifyIcon(0, ref notification) == 0) // NIM_ADD
            ThrowLastError("Add Prince notification icon");
        TrayIconCreated = true;
        notification.Version = 4;
        if (TrayNative.ShellNotifyIcon(4, ref notification) == 0) // NIM_SETVERSION
            ThrowLastError("Enable version 4 notification-icon messages");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        var host = current;
        if (host is null) return TrayNative.DefWindowProc(window, message, wParam, lParam);
        try
        {
            if (message == CallbackMessage)
            {
                // Version 4 packs the event in LOWORD(lParam), and the icon ID in HIWORD.
                var eventId = (uint)((nuint)lParam & 0xFFFF);
                // Ignore single-click selection and the release after a double-click.
                if (eventId is TrayNative.WmLButtonDoubleClick or TrayNative.NinKeySelect)
                    host.onPlayRandom();
                else if (eventId == TrayNative.WmContextMenu)
                {
                    var x = unchecked((short)((nuint)wParam & 0xFFFF));
                    var y = unchecked((short)(((nuint)wParam >> 16) & 0xFFFF));
                    if (x == -1 && y == -1) host.ShowMenu();
                    else host.ShowMenu(x, y);
                }
                return 0;
            }
            if (message == TrayNative.WmCommand && lParam == 0)
            {
                host.DispatchCommand((uint)(wParam & 0xFFFF));
                return 0;
            }
            if (message == OverlayNative.WmTimer && wParam == AutomaticTriggerTimer)
            {
                host.automaticTrigger.OnMinute();
                return 0;
            }
            if (message == host.taskbarCreated && host.taskbarCreated != 0)
            {
                host.TrayIconCreated = false;
                host.AddTrayIcon();
                return 0;
            }
            if (message == TrayNative.WmClose)
            {
                host.RequestExit();
                return 0;
            }
            if (message == TrayNative.WmDestroy)
            {
                if (!host.disposed) host.RequestExit();
                return 0;
            }
            return TrayNative.DefWindowProc(window, message, wParam, lParam);
        }
        catch (Exception error)
        {
            // Managed exceptions must never cross an unmanaged window-procedure boundary.
            TrayNative.MessageBox(window, error.Message, "PopRunner error", 0x00000010);
            if (!host.exitRequested)
            {
                host.exitRequested = true;
                try { host.onExit(); }
                catch { /* The original failure is already reported. */ }
            }
            TrayNative.PostQuitMessage(1);
            return 0;
        }
    }

    private static nint CreatePrinceIcon()
    {
        using var stream = AnimationResources.OpenImage("Assets/Prince/res407.png");
        var prince = PngImage.Load(stream);
        var width = Math.Max(16, TrayNative.GetSystemMetricsForDpi(49, TrayNative.GetDpiForSystem()));
        var height = Math.Max(16, TrayNative.GetSystemMetricsForDpi(50, TrayNative.GetDpiForSystem()));
        var left = prince.Width;
        var top = prince.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < prince.Height; y++)
        for (var x = 0; x < prince.Width; x++)
        {
            if (prince.Pixels[(y * prince.Width + x) * 4 + 3] == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left) throw new InvalidDataException("The Prince tray sprite contains no visible pixels.");
        var sourceWidth = right - left + 1;
        var sourceHeight = bottom - top + 1;
        // The DOS sprites have taller displayed pixels. Keep that silhouette in a square tray slot.
        var scale = Math.Min((double)width / sourceWidth, height / (sourceHeight * 1.2));
        var drawWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var drawHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale * 1.2));
        var offsetX = (width - drawWidth) / 2;
        var offsetY = (height - drawHeight) / 2;
        var info = new TrayNative.BitmapInfo
        {
            Size = 40,
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            ImageSize = (uint)(width * height * 4)
        };
        var color = TrayNative.CreateDIBSection(0, in info, 0, out var pixels, 0, 0);
        if (color == 0 || pixels == 0) ThrowLastError("Create tray-icon bitmap");
        nint mask = 0;
        try
        {
            var output = new Span<byte>((void*)pixels, width * height * 4);
            output.Clear();
            for (var y = 0; y < drawHeight; y++)
            for (var x = 0; x < drawWidth; x++)
            {
                var sourceX = left + x * sourceWidth / drawWidth;
                var sourceY = top + y * sourceHeight / drawHeight;
                // The source faces left; mirror it so the tray Prince faces the animation's direction.
                sourceX = right - (sourceX - left);
                var source = (sourceY * prince.Width + sourceX) * 4;
                var target = ((offsetY + y) * width + offsetX + x) * 4;
                prince.Pixels.AsSpan(source, 4).CopyTo(output[target..]);
            }
            // An all-zero monochrome AND mask lets the 32-bit bitmap's alpha control transparency.
            var maskBits = new byte[((width + 15) / 16) * 2 * height];
            fixed (byte* bits = maskBits)
                mask = TrayNative.CreateBitmap(width, height, 1, 1, bits);
            if (mask == 0) ThrowLastError("Create tray-icon mask");
            var iconInfo = new TrayNative.IconInfo { IsIcon = 1, Color = color, Mask = mask };
            var icon = TrayNative.CreateIconIndirect(in iconInfo);
            if (icon == 0) ThrowLastError("Create Prince tray icon");
            return icon;
        }
        finally
        {
            if (mask != 0) TrayNative.DeleteObject(mask);
            TrayNative.DeleteObject(color);
        }
    }

    private static void ThrowLastError(string operation) =>
        throw new Win32Exception(Marshal.GetLastPInvokeError(), operation);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (TrayIconCreated)
        {
            TrayNative.ShellNotifyIcon(2, ref notification); // NIM_DELETE
            TrayIconCreated = false;
        }
        if (Handle != 0)
        {
            OverlayNative.KillTimer(Handle, AutomaticTriggerTimer);
            TrayNative.DestroyWindow(Handle);
            Handle = 0;
        }
        if (MenuHandle != 0)
        {
            TrayNative.DestroyMenu(MenuHandle);
            MenuHandle = 0;
            // DestroyMenu also destroys attached submenus.
            if (scenarioMenuAttached) ScenarioMenuHandle = 0;
            if (automaticMenuAttached) AutomaticMenuHandle = 0;
        }
        if (ScenarioMenuHandle != 0)
        {
            TrayNative.DestroyMenu(ScenarioMenuHandle);
            ScenarioMenuHandle = 0;
        }
        scenarioMenuAttached = false;
        if (AutomaticMenuHandle != 0)
        {
            TrayNative.DestroyMenu(AutomaticMenuHandle);
            AutomaticMenuHandle = 0;
        }
        automaticMenuAttached = false;
        if (classRegistered)
        {
            TrayNative.UnregisterClass(ClassName, instance);
            classRegistered = false;
        }
        if (IconHandle != 0)
        {
            TrayNative.DestroyIcon(IconHandle);
            IconHandle = 0;
        }
        if (ReferenceEquals(current, this)) current = null;
    }
}

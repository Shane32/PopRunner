using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PopRunner.Core;

namespace PopRunner;

/// <summary>A click-through, per-pixel-alpha Win32 window driven by the shared fixed-tick animation.</summary>
internal sealed unsafe class DesktopOverlay : IDisposable
{
    private const string WindowClassName = "PopRunner.DesktopOverlay";
    private const nuint AnimationTimer = 1;
    private readonly AnimationConfig config;
    private readonly Dictionary<int, PngImage> images = [];
    private readonly nint instance;
    private GCHandle selfHandle;
    private LayeredSurface? surface;
    private AnimationEngine? engine;
    private AnimationClock? clock;
    private DesktopScenarioPlayback? playback;
    private long previousTimestamp;
    private bool disposed;
    private bool registeredClass;
    private bool updatingLayout;

    public nint Handle { get; private set; }
    public bool IsPlaying { get; private set; }
    internal DesktopLayout Layout { get; private set; }
#if POP_RUNNER_CHECKS
    internal AnimationEngine? Engine => engine;
    internal AnimationConfig PlaybackConfig => playback?.Config ?? config;
    internal Scenario? CurrentScenario => engine?.Scenario;
    internal string? CurrentScenarioId => engine?.Scenario.Id;
    internal double? ActionX => playback?.ActionX;
    internal double? ActualActionFraction => playback?.ActionX is double x
        ? (engine!.Scenario.Direction > 0 ? x : playback.WidthUnits - x) / playback.WidthUnits : null;
    internal int EntranceCycleOffset => playback?.EntranceCycleOffset ?? 0;
    internal int? GreetingWaitTicks => playback?.GreetingWaitTicks;
#else
    private AnimationConfig PlaybackConfig => playback?.Config ?? config;
#endif
    internal int CurrentScenarioIndex { get; private set; } = -1;
    internal int BottomPaddingPixels { get; private set; }
    internal int FloorPixelY => Layout.Height - BottomPaddingPixels;
    internal string? LastError { get; private set; }
#if POP_RUNNER_CHECKS
    internal long ElapsedTicks => engine?.ElapsedTicks ?? 0;
    internal int CurrentFrame => engine?.Frame ?? 0;
    internal int CurrentDirection => engine?.Direction ?? 0;
    internal double CurrentX => engine?.X ?? 0;
    internal ReadOnlySpan<byte> Pixels => surface is null ? ReadOnlySpan<byte>.Empty : surface.Pixels;
    internal bool Completed => engine?.Completed ?? false;
    internal SpritePlacement? CurrentPlacement => engine is null ? null :
        engine.GetPlacement(images[engine.Frame].Width, images[engine.Frame].Height);
    internal OverlayBounds NativeFrameBounds { get; private set; }
    internal OverlayBounds? NativePotionBounds { get; private set; }
    internal OverlayBounds? NativeBubbleBounds { get; private set; }
    internal IReadOnlyList<OverlayBounds> NativeSupportingActorBounds { get; private set; } = [];
#endif

    public DesktopOverlay(AnimationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        this.config = config;
        // The configuration's frame dictionary contains the original art used by the configured tracks.
        foreach (var (frame, sprite) in config.Sprites)
        {
            using var stream = AnimationResources.OpenImage(sprite.Image);
            images.Add(frame, PngImage.Load(stream));
        }
        instance = OverlayNative.GetModuleHandleW(null);
        try
        {
            fixed (char* className = WindowClassName)
            {
                var windowClass = new OverlayNative.WindowClass
                {
                    Size = (uint)sizeof(OverlayNative.WindowClass),
                    Instance = instance,
                    Procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint>)&WindowProcedure,
                    ClassName = (nint)className
                };
                if (OverlayNative.RegisterClassExW(in windowClass) == 0) ThrowLastError("Register overlay window class");
                registeredClass = true;
            }
            selfHandle = GCHandle.Alloc(this);
            Handle = OverlayNative.CreateWindowExW(OverlayNative.ExtendedStyle, WindowClassName,
                "PopRunner animation", OverlayNative.WsPopup, 0, 0, 1, 1, 0, 0, instance,
                GCHandle.ToIntPtr(selfHandle));
            if (Handle == 0) ThrowLastError("Create overlay window");
            UpdateLayout();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

#if POP_RUNNER_CHECKS
    /// <summary>Starts the first scenario; retained as a deterministic entry point for native checks.</summary>
    public void Play() => Play(0);
#endif

    /// <summary>Starts or restarts a configured animation in the primary monitor's room.</summary>
    public void Play(int scenarioIndex)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(scenarioIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(scenarioIndex, config.Scenarios.Count);
        Stop();
        LastError = null;
        UpdateLayout();
        StartPlayback(scenarioIndex);
        IsPlaying = true;
        try
        {
            Render();
            OverlayNative.ShowWindow(Handle, OverlayNative.SwShowNoActivate);
            if (OverlayNative.SetTimer(Handle, AnimationTimer, (uint)config.Desktop.TimerIntervalMilliseconds, 0) == 0)
                ThrowLastError("Start animation timer");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void StartPlayback(int scenarioIndex)
    {
        playback = DesktopScenarioLayout.Create(config, scenarioIndex, Layout.Width / Layout.ScaleX, images);
        engine = new AnimationEngine(playback.Config, playback.Scenario);
        clock = new AnimationClock(engine.TicksPerSecond, config.Clock.MaxCatchUpSeconds);
        CurrentScenarioIndex = scenarioIndex;
        previousTimestamp = Stopwatch.GetTimestamp();
    }

#if POP_RUNNER_CHECKS
    /// <summary>Deterministic native-rendering check hook; production playback uses the fixed clock.</summary>
    internal void AdvanceOneTick()
    {
        if (!IsPlaying || engine is null) return;
        engine.Tick();
        Render();
        if (engine.Completed) Stop();
    }
#endif

    private void OnTimer()
    {
        if (!IsPlaying || engine is null || clock is null) return;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(previousTimestamp, now).TotalSeconds;
        previousTimestamp = now;
        var count = clock.Advance(elapsed, 1, engine.Tick);
        if (count == 0) return;
        Render();
        if (engine.Completed) Stop();
    }

    private void Stop()
    {
        IsPlaying = false;
        if (Handle == 0) return;
        OverlayNative.KillTimer(Handle, AnimationTimer);
        OverlayNative.ShowWindow(Handle, OverlayNative.SwHide);
    }

    private void Render()
    {
        if (surface is null || engine is null) return;
        surface.Clear();
#if POP_RUNNER_CHECKS
        NativePotionBounds = null;
        NativeBubbleBounds = null;
#endif
        if (engine.PotionVisible && PlaybackConfig.PotionGraphics is { } potion)
        {
            var body = images[potion.BodyFrame];
#if POP_RUNNER_CHECKS
            NativePotionBounds = Draw(body, engine.GetPotionPlacement(body.Width, body.Height));
#else
            Draw(body, engine.GetPotionPlacement(body.Width, body.Height));
#endif
            if (engine.PotionBubbleFrame is int frame)
            {
                var bubbles = images[frame];
#if POP_RUNNER_CHECKS
                NativeBubbleBounds = Draw(bubbles, engine.GetPotionPlacement(bubbles.Width, bubbles.Height, true));
#else
                Draw(bubbles, engine.GetPotionPlacement(bubbles.Width, bubbles.Height, true));
#endif
            }
        }
        if (engine.PrimaryVisible)
        {
            var image = images[engine.Frame];
            var placement = engine.GetPlacement(image.Width, image.Height);
#if POP_RUNNER_CHECKS
            NativeFrameBounds = Draw(image, placement);
#else
            Draw(image, placement);
#endif
        }
#if POP_RUNNER_CHECKS
        else
        {
            var image = images[engine.Frame];
            NativeFrameBounds = Bounds(image, engine.GetPlacement(image.Width, image.Height));
        }
        var actors = new OverlayBounds[engine.SupportingActors.Count];
#endif
        for (var index = 0; index < engine.SupportingActors.Count; index++)
        {
            var actor = engine.SupportingActors[index];
            var actorImage = images[actor.Frame];
#if POP_RUNNER_CHECKS
            actors[index] = Draw(actorImage, actor.GetPlacement(actorImage.Width, actorImage.Height));
#else
            Draw(actorImage, actor.GetPlacement(actorImage.Width, actorImage.Height));
#endif
        }
#if POP_RUNNER_CHECKS
        NativeSupportingActorBounds = actors;
#endif
        surface.Present(Handle, Layout.Left, Layout.Top);
    }

#if POP_RUNNER_CHECKS
    private OverlayBounds Bounds(PngImage image, SpritePlacement placement) => new(
        Layout.Left + Pixel(placement.Left * Layout.ScaleX),
        Layout.Top + FloorPixelY - PixelSize((config.Stage.FloorY + 1 - placement.Top) * Layout.ScaleY),
        PixelSize(image.Width * Layout.ScaleX), PixelSize(image.Height * Layout.ScaleY));

    private OverlayBounds Draw(PngImage image, SpritePlacement placement)
#else
    private void Draw(PngImage image, SpritePlacement placement)
#endif
    {
        var left = Pixel(placement.Left * Layout.ScaleX);
        var top = FloorPixelY - PixelSize((config.Stage.FloorY + 1 - placement.Top) * Layout.ScaleY);
        surface!.Draw(image, left, top,
            Layout.ScaleX, Layout.ScaleY, placement.Mirrored);
#if POP_RUNNER_CHECKS
        return new OverlayBounds(Layout.Left + left, Layout.Top + top,
            PixelSize(image.Width * Layout.ScaleX), PixelSize(image.Height * Layout.ScaleY));
#endif
    }

    private void UpdateLayout()
    {
        if (Handle == 0 || updatingLayout) return;
        updatingLayout = true;
        var refreshDpi = false;
        try
        {
            var monitor = OverlayNative.MonitorFromPoint(default, 1); // MONITOR_DEFAULTTOPRIMARY
            var info = new OverlayNative.MonitorInfo { Size = (uint)sizeof(OverlayNative.MonitorInfo) };
            if (monitor == 0 || OverlayNative.GetMonitorInfoW(monitor, ref info) == 0)
                ThrowLastError("Read primary monitor geometry");
            var dpi = OverlayNative.GetDpiForWindow(Handle);
            var scaleX = Math.Max(0.5, config.Desktop.SpriteScale * (dpi == 0 ? 96 : dpi) / 96);
            var scaleY = scaleX * config.Stage.PixelAspectY;
            var above = images.Max(pair => pair.Value.Height - config.Sprites[pair.Key].AnchorY);
            var below = Math.Max(0, config.Sprites.Values.Max(sprite => sprite.AnchorY));
            if (config.PotionGraphics is { } potion)
            {
                var bodyOffsetY = potion.BodyOffsetY + potion.VerticalOffsetY;
                var bubbleOffsetY = potion.BubbleOffsetY + potion.VerticalOffsetY;
                above = Math.Max(above, images[potion.BodyFrame].Height - bodyOffsetY);
                above = Math.Max(above, potion.BubbleFrames.Max(frame => images[frame].Height - bubbleOffsetY));
                below = Math.Max(below, Math.Max(bodyOffsetY, bubbleOffsetY));
            }
            var width = info.Monitor.Right - info.Monitor.Left;
            // Keep the DOS floor at the taskbar edge. Below-floor art may extend the window
            // into the taskbar region; reserving that space must not lift the Prince's feet.
            var floor = Math.Clamp(Math.Min(info.Monitor.Bottom, info.Work.Bottom) - config.Desktop.TaskbarGapPixels,
                info.Monitor.Top + 1, info.Monitor.Bottom);
            var top = Math.Max(info.Monitor.Top, floor - PixelSize(above * scaleY) - 2);
            var bottom = Math.Min(info.Monitor.Bottom, floor + PixelSize(below * scaleY));
            var height = bottom - top;
            BottomPaddingPixels = bottom - floor;
            var previousLayout = Layout;
            Layout = new DesktopLayout(info.Monitor.Left, info.Monitor.Top, width,
                info.Monitor.Bottom - info.Monitor.Top, info.Work.Left, info.Work.Top,
                info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top,
                info.Monitor.Left, top, width, height, scaleX, scaleY);
            if (surface is null || surface.Width != width || surface.Height != height)
            {
                var replacement = new LayeredSurface(width, height);
                surface?.Dispose();
                surface = replacement;
            }
            if (OverlayNative.SetWindowPos(Handle, new nint(-1), Layout.Left, Layout.Top, width, height,
                    OverlayNative.SwpNoActivate | OverlayNative.SwpNoOwnerZOrder) == 0)
                ThrowLastError("Position overlay above taskbar");
            // Moving to a newly primary monitor can synchronously send WM_DPICHANGED while this
            // method is guarded. Recompute once after positioning so that notification is retained.
            refreshDpi = OverlayNative.GetDpiForWindow(Handle) != dpi;
            if (IsPlaying)
            {
                if (Layout != previousLayout) StartPlayback(CurrentScenarioIndex);
                Render();
            }
        }
        finally { updatingLayout = false; }
        if (refreshDpi) UpdateLayout();
    }

    private static int Pixel(double coordinate) => (int)Math.Round(coordinate, MidpointRounding.AwayFromZero);
    private static int PixelSize(double extent) => (int)Math.Ceiling(extent);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        DesktopOverlay? owner = null;
        try
        {
            var userData = OverlayNative.GetWindowLongPtrW(hwnd, OverlayNative.GwlpUserData);
            if (message == OverlayNative.WmNcCreate)
            {
                userData = *(nint*)lParam; // CREATESTRUCTW.lpCreateParams is the first field.
                OverlayNative.SetWindowLongPtrW(hwnd, OverlayNative.GwlpUserData, userData);
            }
            if (userData != 0) owner = GCHandle.FromIntPtr(userData).Target as DesktopOverlay;
            switch (message)
            {
                case OverlayNative.WmNcHitTest: return new nint(-1); // HTTRANSPARENT, in addition to WS_EX_TRANSPARENT.
                case OverlayNative.WmMouseActivate: return new nint(3); // MA_NOACTIVATE
                case OverlayNative.WmEraseBackground: return new nint(1);
                case OverlayNative.WmTimer when wParam == AnimationTimer:
                    owner?.OnTimer();
                    return 0;
                case OverlayNative.WmDisplayChange:
                case OverlayNative.WmSettingChange:
                case OverlayNative.WmDpiChanged:
                    owner?.UpdateLayout();
                    return 0;
                case OverlayNative.WmClose:
                    owner?.Stop();
                    return 0;
                case OverlayNative.WmNcDestroy:
                    OverlayNative.SetWindowLongPtrW(hwnd, OverlayNative.GwlpUserData, 0);
                    break;
            }
        }
        catch (Exception error)
        {
            // An exception must never unwind through an unmanaged window procedure.
            if (owner is not null)
            {
                owner.LastError = error.ToString();
                owner.Stop();
            }
            return 0;
        }
        return OverlayNative.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static void ThrowLastError(string action) => throw new Win32Exception(Marshal.GetLastPInvokeError(), action);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
        if (Handle != 0)
        {
            OverlayNative.DestroyWindow(Handle);
            Handle = 0;
        }
        surface?.Dispose();
        surface = null;
        if (selfHandle.IsAllocated) selfHandle.Free();
        if (registeredClass)
        {
            OverlayNative.UnregisterClassW(WindowClassName, instance);
            registeredClass = false;
        }
    }
}

#if POP_RUNNER_CHECKS
internal readonly record struct OverlayBounds(int Left, int Top, int Width, int Height);
#endif

internal readonly record struct DesktopLayout(int MonitorLeft, int MonitorTop, int MonitorWidth, int MonitorHeight,
    int WorkLeft, int WorkTop, int WorkWidth, int WorkHeight, int Left, int Top, int Width, int Height,
    double ScaleX, double ScaleY);

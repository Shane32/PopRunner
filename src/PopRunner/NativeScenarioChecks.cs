#if POP_RUNNER_CHECKS
using System.Text;
using PopRunner.Core;

namespace PopRunner;

/// <summary>Exercises scenario selection, desktop geometry and the actual native layer composition.</summary>
internal static class NativeScenarioChecks
{
    internal static void Run(DesktopOverlay overlay, TrayHost tray, AnimationConfig config,
        string directory, Action<bool, string> check)
    {
        var sourceSnapshot = Snapshot(config);
        var images = config.Sprites.ToDictionary(pair => pair.Key, pair =>
        {
            using var stream = AnimationResources.OpenImage(pair.Value.Image);
            return PngImage.Load(stream);
        });
        var report = new StringBuilder();
        for (var index = 0; index < config.Scenarios.Count; index++)
        {
            var source = config.Scenarios[index];
            SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.FirstScenarioCommand + (uint)index, 0);
            OverlayNative.KillTimer(overlay.Handle, 1);
            check(overlay.CurrentScenarioIndex == index && overlay.CurrentScenarioId == source.Id && overlay.IsPlaying,
                $"Native submenu command did not start scenario {index + 1}.");
            if (source.StartDelayTicks > 0)
            {
                overlay.AdvanceOneTick();
                SmokeNative.SendMessageW(tray.Handle, 0x111, TrayHost.FirstScenarioCommand + (uint)index, 0);
                OverlayNative.KillTimer(overlay.Handle, 1);
                check(overlay.ElapsedTicks == 0 && overlay.Engine?.StartDelayTicksRemaining == source.StartDelayTicks,
                    $"{source.Id}: native replay did not reset the configured entrance delay.");
            }
            var engine = overlay.Engine ?? throw new InvalidOperationException("Native playback has no animation engine.");
            var playbackConfig = overlay.PlaybackConfig ?? throw new InvalidOperationException("Native playback has no configuration.");
            var layout = overlay.Layout;
            var expectedFloor = Math.Clamp(Math.Min(layout.MonitorTop + layout.MonitorHeight, layout.WorkTop + layout.WorkHeight) -
                config.Desktop.TaskbarGapPixels, layout.MonitorTop + 1, layout.MonitorTop + layout.MonitorHeight);
            check(layout.Top + overlay.FloorPixelY == expectedFloor &&
                layout.Top + layout.Height <= layout.MonitorTop + layout.MonitorHeight,
                $"{source.Id}: sprite padding moved the floor away from the taskbar or exceeded the monitor.");
            var entryRight = source.Direction < 0;
            var sourceStart = new AnimationEngine(config, source);
            var departureStep = engine.Scenario.Steps.FindLastIndex(step => step.UntilX.HasValue);
            if (departureStep < 0) departureStep = engine.Scenario.Steps.Count - 1;
            check(engine.Direction == sourceStart.Direction && engine.ClipName == sourceStart.ClipName && engine.Frame == sourceStart.Frame &&
                Near(engine.TicksPerSecond, sourceStart.TicksPerSecond) && engine.StartDelayTicksRemaining == source.StartDelayTicks,
                $"{source.Id}: desktop entrance changed the incoming direction or running pose.");
            check(IsOutside(overlay.NativeFrameBounds, layout, entryRight),
                $"{source.Id}: the complete Prince must begin beyond the incoming monitor edge.");
            check(overlay.NativeFrameBounds.Top >= layout.Top &&
                overlay.NativeFrameBounds.Top + overlay.NativeFrameBounds.Height <= layout.Top + layout.Height,
                $"{source.Id}: initial sprite was vertically clipped.");
            var initialBounds = overlay.NativeFrameBounds;

            var captured = new HashSet<string>(StringComparer.Ordinal);
            var fullyEntered = false;
            var entryClipped = false;
            var exitClipped = false;
            double? leadingSkidX = null;
            var hiddenTick = -1L;
            var consumedTick = -1L;
            var primaryHiddenTick = -1L;
            var actorFrames = new Dictionary<int, int>();
            var initialActors = engine.SupportingActors.Select(actor => (actor.X, actor.Frame, actor.Direction)).ToArray();
            var leadBubbleFrames = new HashSet<int>();
            var previousStep = -1;
            var lastMotionDirection = source.Direction;
            if (overlay.ActionX.HasValue)
            {
                if (source.HasPotion) CheckActionFraction(engine.Scenario.PotionX);
                else if (source.Actors.Count > 0) CheckActionFraction(engine.Scenario.Actors[0].StartX);
            }
            for (var tick = 0; overlay.IsPlaying && tick < 10000 + source.StartDelayTicks; tick++)
            {
                var bounds = overlay.NativeFrameBounds;
                if (engine.StartDelayTicksRemaining > 0)
                {
                    check(engine.ElapsedTicks < source.StartDelayTicks && engine.StepIndex == 0 &&
                        Near(engine.X, engine.Scenario.StartX) && engine.Frame == sourceStart.Frame && engine.Direction == source.Direction &&
                        IsOutside(bounds, layout, entryRight),
                        $"{source.Id}: the offscreen Prince moved or changed pose during the entrance delay.");
                    check(engine.SupportingActors.Select((actor, actorIndex) =>
                        actor.ElapsedTicks == engine.ElapsedTicks && actor.StartDelayTicksRemaining == engine.StartDelayTicksRemaining &&
                        Near(actor.X, initialActors[actorIndex].X) && actor.Frame == initialActors[actorIndex].Frame &&
                        actor.Direction == initialActors[actorIndex].Direction).All(unchanged => unchanged),
                        $"{source.Id}: a supporting actor advanced its choreography during the entrance delay.");
                    if (engine.ElapsedTicks == 0)
                    {
                        Capture("lead-in");
                        VerifyPixels();
                    }
                    if (source.HasPotion && engine.PotionBubbleFrame is int bubbleFrame && leadBubbleFrames.Add(bubbleFrame))
                    {
                        Capture($"lead-in-bubbles-{bubbleFrame}");
                        VerifyPixels();
                    }
                }
                if (engine.PrimaryVisible && bounds.Left >= layout.Left && bounds.Left + bounds.Width <= layout.Left + layout.Width)
                    fullyEntered = true;
                if (engine.PrimaryVisible && IsPartlyClipped(bounds, layout))
                {
                    if (engine.Direction == source.Direction && !fullyEntered && !entryClipped)
                    {
                        Capture("enter-clipped");
                        VerifyPixels();
                        entryClipped = true;
                    }
                    if (fullyEntered && !exitClipped && engine.StepIndex == departureStep)
                    {
                        Capture("exit-clipped");
                        VerifyPixels();
                        exitClipped = true;
                    }
                }
                if (overlay.ActionX.HasValue && !source.HasPotion && source.Actors.Count == 0 &&
                    engine.StepIndex > 0 && engine.StepIndex < departureStep)
                    leadingSkidX = leadingSkidX is not double prior ? engine.X :
                        source.Direction > 0 ? Math.Max(prior, engine.X) : Math.Min(prior, engine.X);
                if (engine.StepIndex != previousStep)
                {
                    if (engine.StepIndex > 0)
                    {
                        check(fullyEntered, $"{source.Id}: the Prince acted before fully entering the monitor.");
                        if (source.DesktopEdgeEntry)
                            check(bounds.Left >= layout.Left && bounds.Left + bounds.Width <= layout.Left + layout.Width,
                                $"{source.Id}: the finite route's hold or return started while its sprite was clipped.");
                    }
                    if (engine.StepIndex > 0 || source.DesktopEdgeEntry)
                    {
                        Capture(engine.ClipName);
                        VerifyPixels();
                    }
                    previousStep = engine.StepIndex;
                }
                if (source.DesktopEdgeEntry)
                {
                    var clip = playbackConfig.Clips[engine.ClipName];
                    if (clip.ExitDx == 0 && !clip.ExitFlip && clip.Frames.All(frame => frame.Dx == 0 && !frame.Flip))
                        check(bounds.Left >= layout.Left && bounds.Left + bounds.Width <= layout.Left + layout.Width,
                            $"{source.Id}: the standing hold was not fully visible inside the monitor.");
                }
                if (source.HasPotion)
                {
                    if (engine.PotionVisible)
                    {
                        check(overlay.NativePotionBounds is { } bottle && FitsVertically(bottle, layout) &&
                            overlay.NativeBubbleBounds is { } bubbles && FitsVertically(bubbles, layout),
                            $"{source.Id}: a potion layer is clipped before reaching the monitor boundary.");
                    }
                    else
                        check(overlay.NativePotionBounds is null && overlay.NativeBubbleBounds is null,
                            $"{source.Id}: hidden potion layers retain visible native bounds.");
                    if (!engine.PotionVisible && hiddenTick < 0)
                    {
                        hiddenTick = engine.ElapsedTicks;
                        var original = AtPotionPickup(config, source);
                        check(!original.PotionVisible && Near(engine.X - engine.Scenario.PotionX, original.X - source.PotionX),
                            $"{source.Id}: desktop remapping changed configured potion pickup alignment.");
                        check(engine.PotionConsumed == original.PotionConsumed,
                            $"{source.Id}: desktop remapping changed the configured potion state at pickup.");
                    }
                    if (engine.PotionConsumed && consumedTick < 0) consumedTick = engine.ElapsedTicks;
                    if (hiddenTick >= 0) check(!engine.PotionVisible, $"{source.Id}: a picked-up potion reappeared.");
                    if (consumedTick >= 0) check(engine.PotionConsumed, $"{source.Id}: potion consumption was lost during playback.");
                }
                for (var actorIndex = 0; actorIndex < engine.SupportingActors.Count; actorIndex++)
                {
                    var actor = engine.SupportingActors[actorIndex];
                    check(Near(actor.TicksPerSecond, engine.TicksPerSecond) &&
                        (actor.Completed ? actor.ElapsedTicks <= engine.ElapsedTicks : actor.ElapsedTicks == engine.ElapsedTicks),
                        "Native supporting actor clocks lost synchronization.");
                    if (!actorFrames.TryGetValue(actorIndex, out var previousFrame) || actor.Frame != previousFrame)
                    {
                        actorFrames[actorIndex] = actor.Frame;
                        Capture($"actor-{actorIndex + 1}-frame-{actor.Frame}");
                        VerifyPixels();
                    }
                    if (!engine.PrimaryVisible && primaryHiddenTick < 0)
                    {
                        primaryHiddenTick = engine.ElapsedTicks;
                        check(engine.SupportingActors.Any(supporting => supporting.HidesPrimaryActor),
                            "Native playback hid its primary sprite without a configured supporting actor handoff.");
                        VerifyPixels();
                    }
                }
                var previousX = engine.X;
                overlay.AdvanceOneTick();
                if (!Near(engine.X, previousX)) lastMotionDirection = Math.Sign(engine.X - previousX);
            }
            check(fullyEntered && entryClipped, $"{source.Id}: the Prince did not visibly enter through a clipped screen edge.");
            check(engine.Completed && !overlay.IsPlaying && overlay.LastError is null && SmokeNative.IsWindowVisible(overlay.Handle) == 0,
                $"{source.Id}: native scenario did not complete and hide cleanly: {overlay.LastError}");
            if (source.StartDelayTicks > 0 && source.HasPotion && config.PotionGraphics is { } ambient)
            {
                var expectedBubbleFrames = Enumerable.Range(0, source.StartDelayTicks).Select(tick =>
                    ambient.BubbleFrames[(ambient.InitialBubbleFrame + tick / ambient.TicksPerFrame) % ambient.BubbleFrames.Length]);
                check(leadBubbleFrames.SetEquals(expectedBubbleFrames),
                    $"{source.Id}: potion bubbles did not keep their configured clock while the Prince waited.");
            }
            if (leadingSkidX is double skidX) CheckActionFraction(skidX);
            if (source.Actors.Count == 0)
                check(exitClipped && IsOutside(overlay.NativeFrameBounds, layout, lastMotionDirection > 0),
                    $"{source.Id}: the Prince must clip at the exit edge, then finish fully offscreen.");
            if (source.DesktopEdgeEntry)
            {
                for (var tick = 0; tick < 100000 && !sourceStart.Completed; tick++) sourceStart.Tick();
                check(sourceStart.Completed && engine.ElapsedTicks == sourceStart.ElapsedTicks && engine.Frame == sourceStart.Frame &&
                    engine.Direction == sourceStart.Direction && Near(engine.X - engine.Scenario.StartX, sourceStart.X - source.StartX),
                    $"{source.Id}: native edge placement changed the finite route's configured motion, duration or final pose.");
                check(lastMotionDirection == -source.Direction && IsOutside(overlay.NativeFrameBounds, layout, entryRight),
                    $"{source.Id}: the finite route did not retreat completely through its incoming edge.");
                var closestGap = Math.Min(EntryGap(initialBounds), EntryGap(overlay.NativeFrameBounds));
                check(closestGap >= 0 && closestGap <= 2 * layout.ScaleX + 2,
                    $"{source.Id}: the finite route was placed too far beyond its incoming edge.");
            }
            check(Snapshot(config) == sourceSnapshot, $"{source.Id}: native layout mutated the shared source configuration.");
            report.AppendLine(FormattableString.Invariant($"{index + 1}. {source.Id}: {engine.ElapsedTicks} ticks; entrance={source.Direction}; captures={string.Join(", ", captured)}"));

            void CheckActionFraction(double actionX)
            {
                var position = actionX * config.Stage.MovementUnitPixels * layout.ScaleX;
                var fraction = (entryRight ? layout.Width - position : position) / layout.Width;
                var maxMovement = source.Steps.Max(step =>
                {
                    var clip = config.Clips[step.Clip];
                    return Math.Max(Math.Abs(clip.ExitDx), clip.Frames.Max(frame => Math.Abs(frame.Dx)));
                });
                var tolerance = (maxMovement * config.Stage.MovementUnitPixels * layout.ScaleX + 3) / layout.Width;
                check(Math.Abs(fraction - config.Desktop.ActionEdgeFraction) <= tolerance,
                    $"{source.Id}: action is at {fraction:0.000} from its entry edge instead of the configured fraction.");
                report.AppendLine(FormattableString.Invariant($"  action fraction from entry: {fraction:0.0000}"));
            }

            void Capture(string phase)
            {
                if (!captured.Add(phase)) return;
                NativeImageChecks.SavePng(Path.Combine(directory, $"native-{index + 1:00}-{phase}.png"), layout.Width, layout.Height, overlay.Pixels);
            }

            int EntryGap(OverlayBounds bounds) => entryRight ? bounds.Left - layout.Left - layout.Width
                : layout.Left - bounds.Left - bounds.Width;

            void VerifyPixels()
            {
                using var expected = new LayeredSurface(layout.Width, layout.Height);
                if (engine.PotionVisible && playbackConfig.PotionGraphics is { } potion)
                {
                    var body = images[potion.BodyFrame];
                    Draw(body, engine.GetPotionPlacement(body.Width, body.Height));
                    var bubbles = images[engine.PotionBubbleFrame!.Value];
                    Draw(bubbles, engine.GetPotionPlacement(bubbles.Width, bubbles.Height, bubbles: true));
                }
                if (engine.PrimaryVisible) DrawActor(engine);
                foreach (var actor in engine.SupportingActors) DrawActor(actor);
                check(expected.Pixels.SequenceEqual(overlay.Pixels),
                    $"{source.Id}/{engine.ClipName}/{engine.ElapsedTicks}: native pixels differ from the visible source sprite layers.");

                void DrawActor(AnimationEngine actor)
                {
                    var image = images[actor.Frame];
                    Draw(image, actor.GetPlacement(image.Width, image.Height));
                }
                void Draw(PngImage image, SpritePlacement placement)
                {
                    var left = (int)Math.Round(placement.Left * layout.ScaleX, MidpointRounding.AwayFromZero);
                    var top = overlay.FloorPixelY - (int)Math.Ceiling((playbackConfig.Stage.FloorY + 1 - placement.Top) * layout.ScaleY);
                    expected.Draw(image, left, top, layout.ScaleX, layout.ScaleY, placement.Mirrored);
                }
            }
        }
        File.WriteAllText(Path.Combine(directory, "native-scenarios.txt"), report.ToString());
    }

    private static AnimationEngine AtPotionPickup(AnimationConfig config, Scenario scenario)
    {
        var engine = new AnimationEngine(config, scenario);
        for (var tick = 0; tick < 10000 + scenario.StartDelayTicks && !engine.Completed && engine.PotionVisible; tick++) engine.Tick();
        return engine;
    }

    private static bool IsOutside(OverlayBounds bounds, DesktopLayout layout, bool right) =>
        right ? bounds.Left >= layout.Left + layout.Width : bounds.Left + bounds.Width <= layout.Left;

    private static bool FitsVertically(OverlayBounds bounds, DesktopLayout layout) =>
        (bounds.Top >= layout.Top || layout.Top == layout.MonitorTop) &&
        (bounds.Top + bounds.Height <= layout.Top + layout.Height ||
            layout.Top + layout.Height == layout.MonitorTop + layout.MonitorHeight);

    private static bool IsPartlyClipped(OverlayBounds bounds, DesktopLayout layout) =>
        bounds.Left < layout.Left + layout.Width && bounds.Left + bounds.Width > layout.Left &&
        (bounds.Left < layout.Left || bounds.Left + bounds.Width > layout.Left + layout.Width);

    private static bool Near(double first, double second) => Math.Abs(first - second) < 1e-7;

    private static string Snapshot(AnimationConfig config)
    {
        var text = new StringBuilder();
        foreach (var scenario in config.Scenarios)
        {
            text.AppendLine(FormattableString.Invariant($"{scenario.Id}|{scenario.Direction}|{scenario.StartX:R}|{scenario.PotionX:R}|{scenario.TicksPerSecond:R}|{scenario.StartDelayTicks}|{scenario.DesktopEdgeEntry}"));
            Steps(scenario.Steps);
            foreach (var actor in scenario.Actors)
            {
                text.AppendLine(FormattableString.Invariant($"{actor.Name}|{actor.Direction}|{actor.StartX:R}"));
                Steps(actor.Steps);
            }
        }
        foreach (var (name, clip) in config.Clips)
        {
            text.AppendLine(FormattableString.Invariant($"{name}|{clip.Loop}|{clip.ExitDx:R}|{clip.ExitFlip}"));
            foreach (var frame in clip.Frames)
                text.AppendLine(FormattableString.Invariant($"{frame.Frame}|{frame.Ticks}|{frame.Dx:R}|{frame.Flip}|{frame.HidePotion}|{frame.ConsumePotion}|{frame.HidePrimaryActor}"));
        }
        return text.ToString();

        void Steps(IEnumerable<ScenarioStep> steps)
        {
            foreach (var step in steps)
                text.AppendLine(FormattableString.Invariant($"{step.Clip}|{step.StartFrameIndex}|{step.Repeat}|{step.UntilX:R}|{string.Join(",", step.ExitOnFrames)}"));
        }
    }
}
#endif

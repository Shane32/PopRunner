using PopRunner.Core;

namespace PopRunner;

/// <summary>Moves a cloned DOS route into a monitor-sized room without stretching sprite movement.</summary>
internal static class DesktopScenarioLayout
{
    private const double OffscreenMarginPixels = 4;

    internal static DesktopScenarioPlayback Create(AnimationConfig source, int scenarioIndex,
        double nativeWidth, IReadOnlyDictionary<int, PngImage> images)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nativeWidth);
        if (!double.IsFinite(nativeWidth)) throw new ArgumentOutOfRangeException(nameof(nativeWidth));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(scenarioIndex, source.Scenarios.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(scenarioIndex);
        var original = source.Scenarios[scenarioIndex];
        var scenario = CloneScenario(original);
        var playbackConfig = new AnimationConfig
        {
            Notes = source.Notes,
            Clock = source.Clock,
            Desktop = source.Desktop,
            Stage = source.Stage,
            PotionGraphics = source.PotionGraphics,
            Sprites = source.Sprites,
            Clips = new Dictionary<string, AnimationClip>(source.Clips, StringComparer.Ordinal),
            Scenarios = source.Scenarios.Select((item, index) =>
                index == scenarioIndex ? scenario : CloneScenario(item)).ToList()
        };
        if (scenario.DesktopEdgeEntry)
            return CreateFiniteEdgePlayback(playbackConfig, scenario, nativeWidth, images);
        var travels = scenario.Steps.Select((step, index) => (Step: step, Index: index))
            .Where(item => item.Step.UntilX.HasValue).ToArray();
        if (travels.Length == 0 || travels[0].Index != 0)
            throw new InvalidDataException("A desktop route must begin with its looping run step.");
        var hasInteraction = scenario.HasPotion || scenario.Actors.Count > 0 ||
            scenario.Steps.Any(step => step.Clip == "skidTurn");
        var summary = InspectSource(source, original, images, hasInteraction,
            travels.Length > 1 ? travels[^1].Index : int.MaxValue);
        var unit = source.Stage.MovementUnitPixels;
        var widthUnits = nativeWidth / unit;
        double? actionX = null;
        if (hasInteraction)
        {
            var reference = scenario.HasPotion ? original.PotionX : scenario.Actors.Count > 0
                ? original.Actors[0].StartX : summary.LeadingSkidX;
            var referencePixels = reference * unit;
            var desiredPixels = nativeWidth * (scenario.Direction > 0
                ? source.Desktop.ActionEdgeFraction : 1 - source.Desktop.ActionEdgeFraction);
            // Keep the entire interaction on screen when it fits, even on a narrow/DPI-scaled monitor.
            var minimum = referencePixels - summary.InteractionLeft;
            var maximum = nativeWidth + referencePixels - summary.InteractionRight;
            desiredPixels = minimum <= maximum ? Math.Clamp(desiredPixels, minimum, maximum)
                : (minimum + maximum) / 2;
            // A binary-fraction translation keeps integer DOS dx and equality-at-untilX phase-stable.
            // Its subpixel error is far below the renderer's original whole-pixel quantization.
            var shift = Math.Round((desiredPixels / unit - reference) * 1024) / 1024;
            actionX = reference + shift;
            Translate(scenario, shift);
        }

        var firstClip = source.Clips[scenario.Steps[0].Clip];
        var cycleDistance = firstClip.Frames.Sum(frame => frame.Dx) + firstClip.ExitDx;
        var cycleTicks = firstClip.Frames.Sum(frame => frame.Ticks);
        if (cycleDistance <= 0 || firstClip.ExitFlip || firstClip.Frames.Any(frame => frame.Flip))
            throw new InvalidDataException("The entrance run must advance in its facing direction.");
        var translatedStart = scenario.StartX;
        var first = new AnimationEngine(playbackConfig, scenario);
        var firstImage = images[first.Frame];
        var initial = first.GetPlacement(firstImage.Width, firstImage.Height);
        var cycles = (int)Math.Ceiling(scenario.Direction > 0
            ? (initial.Left + initial.Width + OffscreenMarginPixels) / (cycleDistance * unit)
            : (nativeWidth + OffscreenMarginPixels - initial.Left) / (cycleDistance * unit));

        var originalWait = scenario.Actors.Count == 0 ? 0 : WaitDuration(source, original.Actors[0]);
        if (originalWait > 0)
            cycles = Math.Max(cycles, (int)Math.Ceiling((1.0 - originalWait) / cycleTicks));
        // Retain at least one entrance tick, including when a tiny monitor shortens the source approach.
        var entranceRunTicks = summary.FirstRunTicks - scenario.StartDelayTicks;
        cycles = Math.Max(cycles, (int)Math.Ceiling((1.0 - entranceRunTicks) / cycleTicks));
        scenario.StartX = translatedStart - scenario.Direction * cycles * cycleDistance;
        while (true)
        {
            first = new AnimationEngine(playbackConfig, scenario);
            initial = first.GetPlacement(firstImage.Width, firstImage.Height);
            if (scenario.Direction > 0 ? initial.Left + initial.Width <= -OffscreenMarginPixels
                : initial.Left >= nativeWidth + OffscreenMarginPixels) break;
            cycles++;
            scenario.StartX = translatedStart - scenario.Direction * cycles * cycleDistance;
        }

        int? greetingWait = null;
        for (var actorIndex = 0; actorIndex < scenario.Actors.Count; actorIndex++)
        {
            var actor = scenario.Actors[actorIndex];
            var waitStep = actor.Steps[0];
            var waitClip = source.Clips[waitStep.Clip];
            if (waitClip.Frames.Count != 1 || waitClip.Frames[0].Dx != 0 || waitClip.Frames[0].Flip)
                throw new InvalidDataException("A desktop supporting actor must begin with one stationary waiting pose.");
            var waitTicks = checked(WaitDuration(source, original.Actors[actorIndex]) + cycles * cycleTicks);
            if (waitTicks <= 0) throw new InvalidDataException("The desktop approach ended before its supporting actor's wait.");
            var name = $"__desktop_wait_{scenario.Id}_{actorIndex}";
            playbackConfig.Clips.Add(name, new AnimationClip
            {
                Label = waitClip.Label,
                Frames = [CloneFrame(waitClip.Frames[0], waitTicks)]
            });
            waitStep.Clip = name;
            waitStep.Repeat = 1;
            if (actorIndex == 0) greetingWait = waitTicks;
        }

        if (!hasInteraction || travels.Length > 1)
        {
            var departure = travels[^1].Step;
            var margin = scenario.Steps.SelectMany(step => source.Clips[step.Clip].Frames)
                .Max(frame => images[frame.Frame].Width +
                    Math.Abs(source.Sprites[frame.Frame].AnchorX) * unit) + OffscreenMarginPixels;
            departure.UntilX = summary.FinalDirection > 0 ? (nativeWidth + margin) / unit : -margin / unit;
        }
        return new DesktopScenarioPlayback(playbackConfig, scenario, actionX, cycles,
            summary.FirstRunTicks, summary.FirstRunTicks + (long)cycles * cycleTicks, greetingWait,
            widthUnits);
    }

    private static DesktopScenarioPlayback CreateFiniteEdgePlayback(AnimationConfig config, Scenario scenario,
        double nativeWidth, IReadOnlyDictionary<int, PngImage> images)
    {
        if (scenario.Steps.Any(step => step.UntilX.HasValue))
            throw new InvalidDataException("A finite desktop edge route must use configured clip durations.");
        const double margin = 1; // One original image pixel, before desktop scaling.
        var original = Measure();
        var shiftPixels = scenario.Direction > 0
            ? -margin - Math.Max(original.Initial.Left + original.Initial.Width, original.Final.Left + original.Final.Width)
            : nativeWidth + margin - Math.Min(original.Initial.Left, original.Final.Left);
        Translate(scenario, shiftPixels / config.Stage.MovementUnitPixels);
        var placed = Measure();
        // Original whole-pixel truncation can land just inside the requested boundary.
        // Move outward by one source pixel if necessary; clip movement and timing stay untouched.
        for (var attempt = 0; attempt < 3 && !Outside(placed); attempt++)
        {
            Translate(scenario, -scenario.Direction / config.Stage.MovementUnitPixels);
            placed = Measure();
        }
        if (!Outside(placed)) throw new InvalidDataException("The finite desktop route could not be placed outside its entry edge.");
        if (placed.HoldLeft < 0 || placed.HoldRight > nativeWidth)
            throw new InvalidDataException("The finite desktop route's standing hold does not fit inside the monitor.");
        return new DesktopScenarioPlayback(config, scenario, null, 0,
            original.ActionTick, placed.ActionTick, null, nativeWidth / config.Stage.MovementUnitPixels);

        bool Outside((SpritePlacement Initial, SpritePlacement Final, double HoldLeft, double HoldRight, long ActionTick) bounds) =>
            scenario.Direction > 0
                ? Math.Max(bounds.Initial.Left + bounds.Initial.Width, bounds.Final.Left + bounds.Final.Width) <= -margin
                : Math.Min(bounds.Initial.Left, bounds.Final.Left) >= nativeWidth + margin;

        (SpritePlacement Initial, SpritePlacement Final, double HoldLeft, double HoldRight, long ActionTick) Measure()
        {
            var engine = new AnimationEngine(config, scenario);
            SpritePlacement Placement()
            {
                var image = images[engine.Frame];
                return engine.GetPlacement(image.Width, image.Height);
            }
            var initial = Placement();
            var holdLeft = double.PositiveInfinity;
            var holdRight = double.NegativeInfinity;
            long actionTick = -1;
            for (var tick = 0; tick < 100000; tick++)
            {
                if (engine.StepIndex > 0 && actionTick < 0) actionTick = engine.ElapsedTicks;
                var clip = config.Clips[engine.ClipName];
                if (clip.ExitDx == 0 && !clip.ExitFlip && clip.Frames.All(frame => frame.Dx == 0 && !frame.Flip))
                {
                    var bounds = Placement();
                    holdLeft = Math.Min(holdLeft, bounds.Left);
                    holdRight = Math.Max(holdRight, bounds.Left + bounds.Width);
                }
                if (engine.Completed)
                    return (initial, Placement(), holdLeft, holdRight, actionTick < 0 ? engine.ElapsedTicks : actionTick);
                engine.Tick();
            }
            throw new InvalidDataException("The finite desktop route did not finish.");
        }
    }

    private static SourceSummary InspectSource(AnimationConfig config, Scenario scenario,
        IReadOnlyDictionary<int, PngImage> images, bool hasInteraction, int departureStep)
    {
        var engine = new AnimationEngine(config, scenario);
        long firstRunTicks = -1;
        var leading = double.NegativeInfinity;
        var left = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        void Include(AnimationEngine actor)
        {
            var image = images[actor.Frame];
            var bounds = actor.GetPlacement(image.Width, image.Height);
            left = Math.Min(left, bounds.Left);
            right = Math.Max(right, bounds.Left + bounds.Width);
        }
        for (var count = 0; count < 100000; count++)
        {
            if (engine.StepIndex > 0 && firstRunTicks < 0) firstRunTicks = engine.ElapsedTicks;
            if (engine.ClipName == "skidTurn") leading = Math.Max(leading, engine.X * scenario.Direction);
            if (hasInteraction && engine.StepIndex > 0 && engine.StepIndex < departureStep)
            {
                if (engine.PrimaryVisible) Include(engine);
                if (engine.PotionVisible && config.PotionGraphics is { } potion)
                {
                    var image = images[potion.BodyFrame];
                    var bounds = engine.GetPotionPlacement(image.Width, image.Height);
                    left = Math.Min(left, bounds.Left);
                    right = Math.Max(right, bounds.Left + bounds.Width);
                }
            }
            if (hasInteraction)
                foreach (var actor in engine.SupportingActors) Include(actor);
            if (engine.Completed) break;
            engine.Tick();
        }
        if (!engine.Completed) throw new InvalidDataException("The original desktop route did not finish.");
        return new SourceSummary(firstRunTicks < 0 ? engine.ElapsedTicks : firstRunTicks,
            engine.Direction, double.IsFinite(leading) ? leading * scenario.Direction : 0,
            left, right);
    }

    private static int WaitDuration(AnimationConfig config, SupportingActor actor) =>
        checked(config.Clips[actor.Steps[0].Clip].Frames.Sum(frame => frame.Ticks) * actor.Steps[0].Repeat);

    private static void Translate(Scenario scenario, double shift)
    {
        scenario.StartX += shift;
        if (scenario.HasPotion) scenario.PotionX += shift;
        TranslateSteps(scenario.Steps, shift);
        foreach (var actor in scenario.Actors)
        {
            actor.StartX += shift;
            TranslateSteps(actor.Steps, shift);
        }
    }

    private static void TranslateSteps(List<ScenarioStep> steps, double shift)
    {
        foreach (var step in steps)
            if (step.UntilX is double target) step.UntilX = target + shift;
    }

    private static Scenario CloneScenario(Scenario source) => new()
    {
        Id = source.Id, Title = source.Title, Subtitle = source.Subtitle, Route = source.Route,
        Direction = source.Direction, StartX = source.StartX, HasPotion = source.HasPotion,
        PotionX = source.PotionX, TicksPerSecond = source.TicksPerSecond, StartDelayTicks = source.StartDelayTicks,
        DesktopEdgeEntry = source.DesktopEdgeEntry,
        Steps = source.Steps.Select(CloneStep).ToList(),
        Actors = source.Actors.Select(actor => new SupportingActor
        {
            Name = actor.Name, Direction = actor.Direction, StartX = actor.StartX,
            Steps = actor.Steps.Select(CloneStep).ToList()
        }).ToList()
    };

    private static ScenarioStep CloneStep(ScenarioStep step) => new()
    {
        Clip = step.Clip, StartFrameIndex = step.StartFrameIndex, Repeat = step.Repeat,
        UntilX = step.UntilX, ExitOnFrames = [.. step.ExitOnFrames]
    };

    private static FrameStep CloneFrame(FrameStep frame, int ticks) => new()
    {
        Frame = frame.Frame, Dx = frame.Dx, Ticks = ticks, Flip = frame.Flip,
        HidePotion = frame.HidePotion, ConsumePotion = frame.ConsumePotion,
        HidePrimaryActor = frame.HidePrimaryActor
    };

    private readonly record struct SourceSummary(long FirstRunTicks, int FinalDirection,
        double LeadingSkidX, double InteractionLeft, double InteractionRight);
}

internal sealed record DesktopScenarioPlayback(AnimationConfig Config, Scenario Scenario, double? ActionX,
    int EntranceCycleOffset, long OriginalActionTick, long ActionTick, int? GreetingWaitTicks,
    double WidthUnits);

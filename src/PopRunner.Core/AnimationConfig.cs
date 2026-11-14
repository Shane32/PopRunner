using System.Text.Json;
using System.Text.Json.Serialization;

namespace PopRunner.Core;

public sealed class AnimationConfig
{
    public string[] Notes { get; set; } = [];
    public ClockConfig Clock { get; set; } = new();
    public DesktopConfig Desktop { get; set; } = new();
    public StageConfig Stage { get; set; } = new();
    public PotionGraphics? PotionGraphics { get; set; }
    public Dictionary<int, SpriteFrame> Sprites { get; set; } = [];
    public Dictionary<string, AnimationClip> Clips { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];

    public static AnimationConfig Load()
    {
        using var json = AnimationResources.OpenConfiguration();
        return Load(json);
    }

    public static AnimationConfig Load(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var config = JsonSerializer.Deserialize(json, AnimationJsonContext.Default.AnimationConfig)
            ?? throw new InvalidDataException("The configuration is empty.");
        config.Validate();
        return config;
    }

    private void Validate()
    {
        static void Require(bool condition, string error)
        {
            if (!condition) throw new InvalidDataException(error);
        }
        Require(double.IsFinite(Clock.TicksPerSecond) && Clock.TicksPerSecond is > 0 and <= 240,
            "clock.ticksPerSecond must be between 0 and 240.");
        Require(Clock.MaxCatchUpSeconds is > 0 and <= 10 && double.IsFinite(Clock.MaxCatchUpSeconds), "Invalid clock.maxCatchUpSeconds.");
        Require(double.IsFinite(Desktop.SpriteScale) && Desktop.SpriteScale is >= 0.5 and <= 8,
            "desktop.spriteScale must be between 0.5 and 8.");
        Require(Desktop.TimerIntervalMilliseconds is >= 1 and <= 1000,
            "desktop.timerIntervalMilliseconds must be between 1 and 1000.");
        Require(Desktop.TaskbarGapPixels is >= 0 and <= 256,
            "desktop.taskbarGapPixels must be between 0 and 256 physical pixels.");
        Require(double.IsFinite(Desktop.ActionEdgeFraction) && Desktop.ActionEdgeFraction is > 0 and <= 0.5,
            "desktop.actionEdgeFraction must be greater than zero and at most 0.5.");
        Require(Stage.Width is >= 100 and <= 4096 && Stage.Height is >= 64 and <= 2160, "Invalid stage dimensions.");
        Require(Stage.FloorY > 45 && Stage.FloorY < Stage.Height, "stage.floorY must be inside the stage.");
        Require(double.IsFinite(Stage.MovementUnitPixels) && Stage.MovementUnitPixels is > 0 and <= 100,
            "Invalid stage.movementUnitPixels.");
        Require(double.IsFinite(Stage.PixelAspectY) && Stage.PixelAspectY is >= 0.5 and <= 3, "Invalid stage.pixelAspectY.");
        Require(double.IsFinite(Stage.PreStretchPixelSize) && Stage.PreStretchPixelSize is > 0 and <= 10, "Invalid stage.preStretchPixelSize.");
        Require(Scenarios.Count > 0, "At least one scenario is required.");
        Require(Scenarios.Select(s => s.Id).Distinct().Count() == Scenarios.Count, "Scenario IDs must be unique.");
        Require(Desktop.RandomScenarioIds is { Length: > 0 }, "desktop.randomScenarioIds must contain at least one scenario ID.");
        Require(Desktop.RandomScenarioIds.Distinct(StringComparer.Ordinal).Count() == Desktop.RandomScenarioIds.Length &&
            Desktop.RandomScenarioIds.All(id => Scenarios.Any(s => s.Id == id)),
            "desktop.randomScenarioIds must contain unique, existing scenario IDs.");
        foreach (var (frame, sprite) in Sprites)
        {
            Require(!string.IsNullOrWhiteSpace(sprite.Image), $"Sprite {frame} needs an image.");
            Require(AnimationResources.ContainsImage(sprite.Image), $"Sprite {frame}: missing embedded image {sprite.Image}.");
            Require(double.IsFinite(sprite.AnchorX) && double.IsFinite(sprite.AnchorY), $"Invalid sprite {frame} anchor.");
        }
        Require(!Scenarios.Any(s => s.HasPotion) || PotionGraphics != null,
            "potionGraphics is required for scenarios with hasPotion.");
        if (PotionGraphics is { } potion)
        {
            Require(Sprites.ContainsKey(potion.BodyFrame), "potionGraphics.bodyFrame refers to a missing sprite.");
            Require(potion.BubbleFrames.Length > 0 && potion.BubbleFrames.All(Sprites.ContainsKey),
                "potionGraphics.bubbleFrames must contain existing sprite IDs.");
            Require(potion.TicksPerFrame is > 0 and <= 10000, "potionGraphics.ticksPerFrame must be between 1 and 10000.");
            Require(potion.InitialBubbleFrame >= 0 && potion.InitialBubbleFrame < potion.BubbleFrames.Length,
                "potionGraphics.initialBubbleFrame must index bubbleFrames.");
            Require(double.IsFinite(potion.BodyOffsetX) && double.IsFinite(potion.BodyOffsetY) &&
                double.IsFinite(potion.BubbleOffsetX) && double.IsFinite(potion.BubbleOffsetY),
                "potionGraphics offsets must be finite native-pixel values.");
        }
        foreach (var (name, clip) in Clips)
        {
            Require(clip.Frames.Count > 0, $"Clip '{name}' has no frames.");
            Require(double.IsFinite(clip.ExitDx), $"Clip '{name}' has an invalid exitDx.");
            foreach (var step in clip.Frames)
            {
                Require(Sprites.ContainsKey(step.Frame), $"Clip '{name}' refers to missing frame {step.Frame}.");
                Require(step.Ticks is > 0 and <= 10000 && double.IsFinite(step.Dx), $"Invalid frame in '{name}'.");
            }
        }
        void ValidateSteps(List<ScenarioStep> steps, string owner)
        {
            Require(steps.Count > 0, $"{owner} has no steps.");
            foreach (var step in steps)
            {
                Require(Clips.ContainsKey(step.Clip), $"Unknown clip '{step.Clip}' in {owner}.");
                var clip = Clips[step.Clip];
                Require(step.StartFrameIndex >= 0 && step.StartFrameIndex < clip.Frames.Count, $"Invalid startFrameIndex in {owner}.");
                Require(step.Repeat is > 0 and <= 10000, $"Invalid repeat in {owner}.");
                Require(step.UntilX == null || double.IsFinite(step.UntilX.Value), $"Invalid untilX in {owner}.");
                Require(step.UntilX == null || clip.Loop, $"untilX needs a looping clip in {owner}.");
                Require(step.ExitOnFrames.All(f => clip.Frames.Any(s => s.Frame == f)), $"Invalid exitOnFrames in {owner}.");
                if (step.UntilX != null)
                    Require(clip.Frames.Sum(f => f.Dx) > 0 && !clip.ExitFlip && !clip.Frames.Any(f => f.Flip),
                        $"The untilX clip '{step.Clip}' must advance in the facing direction.");
            }
        }
        foreach (var scenario in Scenarios)
        {
            Require(scenario.Direction is -1 or 1, $"Scenario '{scenario.Id}' direction must be -1 or 1.");
            Require(double.IsFinite(scenario.StartX) && double.IsFinite(scenario.PotionX), $"Invalid scenario '{scenario.Id}' position.");
            Require(scenario.TicksPerSecond == null || (double.IsFinite(scenario.TicksPerSecond.Value) && scenario.TicksPerSecond is > 0 and <= 240),
                $"Invalid ticksPerSecond in scenario '{scenario.Id}'.");
            Require(scenario.StartDelayTicks is >= 0 and <= 10000,
                $"Invalid startDelayTicks in scenario '{scenario.Id}'; expected between 0 and 10000.");
            Require(!scenario.DesktopEdgeEntry || scenario.Steps.All(step => step.UntilX == null),
                $"Scenario '{scenario.Id}' with desktopEdgeEntry must use finite steps without untilX targets.");
            ValidateSteps(scenario.Steps, $"scenario '{scenario.Id}'");
            Require(scenario.Actors.Select(a => a.Name).Distinct().Count() == scenario.Actors.Count,
                $"Supporting actor names must be unique in '{scenario.Id}'.");
            foreach (var actor in scenario.Actors)
            {
                Require(!string.IsNullOrWhiteSpace(actor.Name), $"A supporting actor in '{scenario.Id}' has no name.");
                Require(actor.Direction is -1 or 1 && double.IsFinite(actor.StartX), $"Invalid actor position or direction in '{scenario.Id}'.");
                ValidateSteps(actor.Steps, $"actor '{actor.Name}' in '{scenario.Id}'");
            }
        }
        // Reject unreachable movement targets and runaway scripts before replacing a working configuration.
        foreach (var scenario in Scenarios)
        {
            var engine = new AnimationEngine(this, scenario);
            for (var tick = 0; tick < 100000 && !engine.Completed; tick++) engine.Tick();
            Require(engine.Completed, $"Scenario '{scenario.Id}' cannot finish within 100000 ticks; check untilX and dx signs.");
        }
    }
}

public sealed class ClockConfig
{
    public double TicksPerSecond { get; set; } = 12;
    public double MaxCatchUpSeconds { get; set; } = 0.5;
}

/// <summary>Win32 desktop presentation; the animation's DOS displacement and tick rate stay unchanged.</summary>
public sealed class DesktopConfig
{
    public string[] RandomScenarioIds { get; set; } =
    [
        "across-right", "across-left", "turn-from-left", "turn-from-right",
        "greet-princess", "potion-direct-return-right", "potion-direct-return-left", "greet-princess-left",
        "careful-step-right", "careful-step-left"
    ];
    public double ActionEdgeFraction { get; set; } = 1.0 / 3;
    /// <summary>Source-pixel size at 96 DPI; multiplied by the primary monitor's DPI scale.</summary>
    public double SpriteScale { get; set; } = 2;
    /// <summary>Native message timer wake-up interval. AnimationClock still advances at the configured fixed tick rate.</summary>
    public int TimerIntervalMilliseconds { get; set; } = 15;
    /// <summary>Physical screen pixels between the sprite floor and the primary monitor's work-area bottom.</summary>
    public int TaskbarGapPixels { get; set; }
}

public sealed class StageConfig
{
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 100;
    public int FloorY { get; set; } = 78;
    public double PixelAspectY { get; set; } = 1.2;
    public double MovementUnitPixels { get; set; } = 16.0 / 7;
    public double PreStretchPixelSize { get; set; } = 8.0 / 7;
    public bool QuantizeToPixels { get; set; } = true;
    public bool ShowGuides { get; set; }
    public bool AutoAdvance { get; set; } = true;
}

public sealed class SpriteFrame
{
    public string Image { get; set; } = "";
    public double AnchorX { get; set; }
    public double AnchorY { get; set; }
    public bool OddPixel { get; set; }
}

public sealed class PotionGraphics
{
    public int BodyFrame { get; set; }
    public int[] BubbleFrames { get; set; } = [];
    public int TicksPerFrame { get; set; } = 1;
    public int InitialBubbleFrame { get; set; }
    /// <summary>Align the vessel bottom with the actor's floor while preserving the bubbles' relative position.</summary>
    public bool AlignWithFeet { get; set; }
    /// <summary>Presentation-only shift in source pixels; the original configured offsets remain unchanged.</summary>
    [JsonIgnore]
    public double VerticalOffsetY => AlignWithFeet ? -BodyOffsetY : 0;
    public double BodyOffsetX { get; set; }
    public double BodyOffsetY { get; set; }
    public double BubbleOffsetX { get; set; }
    public double BubbleOffsetY { get; set; }
}

public sealed class AnimationClip
{
    public string Label { get; set; } = "";
    public bool Loop { get; set; }
    public double ExitDx { get; set; }
    public bool ExitFlip { get; set; }
    public List<FrameStep> Frames { get; set; } = [];
}

public sealed class FrameStep
{
    public int Frame { get; set; }
    public int Ticks { get; set; } = 1;
    public double Dx { get; set; }
    public bool Flip { get; set; }
    public bool HidePotion { get; set; }
    public bool ConsumePotion { get; set; }
    public bool HidePrimaryActor { get; set; }
}

public sealed class Scenario
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Route { get; set; } = "";
    public int Direction { get; set; } = 1;
    public double StartX { get; set; }
    public bool HasPotion { get; set; }
    public double PotionX { get; set; }
    public double? TicksPerSecond { get; set; }
    /// <summary>Hold all actor scripts before the entrance; potion bubbles still animate on the scenario clock.</summary>
    public int StartDelayTicks { get; set; }
    /// <summary>A finite route placed at its incoming primary-monitor edge by the desktop presentation.</summary>
    public bool DesktopEdgeEntry { get; set; }
    public List<ScenarioStep> Steps { get; set; } = [];
    public List<SupportingActor> Actors { get; set; } = [];
}

public sealed class SupportingActor
{
    public string Name { get; set; } = "";
    public int Direction { get; set; } = -1;
    public double StartX { get; set; }
    public List<ScenarioStep> Steps { get; set; } = [];
}

public sealed class ScenarioStep
{
    public string Clip { get; set; } = "";
    public int StartFrameIndex { get; set; }
    public int Repeat { get; set; } = 1;
    public double? UntilX { get; set; }
    public int[] ExitOnFrames { get; set; } = [];
}

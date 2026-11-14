namespace PopRunner.Core;

/// <summary>One deterministic DOS animation tick. Presentation speed never changes displacement per tick.</summary>
public sealed class AnimationEngine
{
    private readonly AnimationConfig config;
    private int stepIndex;
    private int frameIndex;
    private int remainingTicks;
    private int repetitions;
    private bool primaryCompleted;
    private readonly List<AnimationEngine> supportingActors = [];

    public Scenario Scenario { get; }
    public double X { get; private set; }
    public int Direction { get; private set; }
    public int Frame { get; private set; }
    public long ElapsedTicks { get; private set; }
    /// <summary>Entrance lead-in ticks remaining; elapsed time and ambient potion bubbles continue during this hold.</summary>
    public int StartDelayTicksRemaining => ElapsedTicks >= Scenario.StartDelayTicks
        ? 0 : Scenario.StartDelayTicks - (int)ElapsedTicks;
    public bool Completed => primaryCompleted && supportingActors.All(a => a.Completed);
    public IReadOnlyList<AnimationEngine> SupportingActors => supportingActors;
    public bool HidesPrimaryActor { get; private set; }
    public bool PrimaryVisible => !supportingActors.Any(a => a.HidesPrimaryActor);
    public bool PotionVisible { get; private set; }
    public bool PotionConsumed { get; private set; }
    public int? PotionBubbleFrame => config.PotionGraphics is { } potion
        ? potion.BubbleFrames[(int)((potion.InitialBubbleFrame + ElapsedTicks / potion.TicksPerFrame) % potion.BubbleFrames.Length)]
        : null;
    public int StepIndex => stepIndex;
    public string ClipName => Scenario.Steps[stepIndex].Clip;
    public string Phase => StartDelayTicksRemaining > 0 ? "Waiting to enter" : config.Clips[ClipName].Label;
    public double TicksPerSecond => Scenario.TicksPerSecond ?? config.Clock.TicksPerSecond;
    public double ElapsedSeconds => ElapsedTicks / TicksPerSecond;
    public SpriteFrame Sprite => config.Sprites[Frame];
    private ScenarioStep Step => Scenario.Steps[stepIndex];
    private AnimationClip Clip => config.Clips[Step.Clip];

    public AnimationEngine(AnimationConfig config, Scenario scenario)
    {
        this.config = config;
        Scenario = scenario;
        X = scenario.StartX;
        Direction = scenario.Direction;
        PotionVisible = scenario.HasPotion;
        frameIndex = Step.StartFrameIndex;
        // Make the first pose available for rendering, but defer its movement and commands until the entrance starts.
        Frame = Clip.Frames[frameIndex].Frame;
        if (scenario.StartDelayTicks == 0) EnterFrame();
        foreach (var actor in scenario.Actors)
        {
            // Actor scripts share the entrance hold and clock; ambient potion bubbles use total elapsed time instead.
            supportingActors.Add(new AnimationEngine(config, new Scenario
            {
                Id = actor.Name,
                Title = actor.Name,
                Direction = actor.Direction,
                StartX = actor.StartX,
                TicksPerSecond = scenario.TicksPerSecond,
                StartDelayTicks = scenario.StartDelayTicks,
                Steps = actor.Steps
            }));
        }
    }

    private void EnterFrame()
    {
        var frame = Clip.Frames[frameIndex];
        // Sequence commands are applied before the sprite frame, in original bytecode order.
        X += Direction * frame.Dx;
        if (frame.Flip) Direction = -Direction;
        Frame = frame.Frame;
        remainingTicks = frame.Ticks;
        if (frame.HidePotion) PotionVisible = false;
        if (frame.ConsumePotion) PotionConsumed = true;
        if (frame.HidePrimaryActor) HidesPrimaryActor = true;
    }

    public void Tick()
    {
        if (Completed) return;
        ElapsedTicks++;
        foreach (var actor in supportingActors) actor.Tick();
        if (primaryCompleted) return;
        if (ElapsedTicks <= Scenario.StartDelayTicks)
        {
            if (ElapsedTicks == Scenario.StartDelayTicks) EnterFrame();
            return;
        }
        if (--remainingTicks > 0) return;

        var targetReached = Step.UntilX is double target &&
            (Direction * Clip.Frames.Sum(f => f.Dx) > 0 ? X >= target : X <= target) &&
            (Step.ExitOnFrames.Length == 0 || Step.ExitOnFrames.Contains(Frame));

        if (targetReached)
        {
            ExitClip();
            NextStep();
            return;
        }
        frameIndex++;
        if (frameIndex < Clip.Frames.Count)
        {
            EnterFrame();
            return;
        }
        ExitClip();
        repetitions++;
        if (Step.UntilX != null || repetitions < Step.Repeat)
        {
            frameIndex = 0;
            EnterFrame();
        }
        else NextStep();
    }

    private void ExitClip()
    {
        X += Direction * Clip.ExitDx;
        if (Clip.ExitFlip) Direction = -Direction;
    }

    private void NextStep()
    {
        if (stepIndex == Scenario.Steps.Count - 1)
        {
            primaryCompleted = true;
            return;
        }
        stepIndex++;
        frameIndex = Step.StartFrameIndex;
        repetitions = 0;
        EnterFrame();
    }

    public SpritePlacement GetPlacement(int width, int height)
    {
        // DOS: ((Char.x + facing * frame.dx) * 2 - 116 + parity) * 320 / 280.
        // X is Char.x minus 58, so the room origin is zero. Right-facing images end at this anchor.
        var parity = Sprite.OddPixel == (Direction < 0) ? 1 : 0;
        var unitsToPreStretchPixels = config.Stage.MovementUnitPixels / config.Stage.PreStretchPixelSize;
        var anchor = ((X + Direction * Sprite.AnchorX) * unitsToPreStretchPixels + parity) * config.Stage.PreStretchPixelSize;
        if (config.Stage.QuantizeToPixels) anchor = Math.Truncate(anchor);
        return new SpritePlacement(anchor - (Direction > 0 ? width : 0),
            config.Stage.FloorY + Sprite.AnchorY - height + 1, width, height, Direction > 0);
    }

    public SpritePlacement GetPotionPlacement(int width, int height, bool bubbles = false)
    {
        var potion = config.PotionGraphics ?? throw new InvalidOperationException("No potionGraphics is configured.");
        var left = Scenario.PotionX * config.Stage.MovementUnitPixels + (bubbles ? potion.BubbleOffsetX : potion.BodyOffsetX);
        var offsetY = (bubbles ? potion.BubbleOffsetY : potion.BodyOffsetY) + potion.VerticalOffsetY;
        var bottom = config.Stage.FloorY + 1 + offsetY;
        if (config.Stage.QuantizeToPixels)
        {
            left = Math.Truncate(left);
            bottom = Math.Truncate(bottom);
        }
        return new SpritePlacement(left, bottom - height, width, height, false);
    }
}

public readonly record struct SpritePlacement(double Left, double Top, int Width, int Height, bool Mirrored);

/// <summary>A fixed simulation clock independent of display refresh, with bounded UI-stall catch-up.</summary>
public sealed class AnimationClock(double ticksPerSecond, double maxCatchUpSeconds)
{
    private double accumulated;
    public void Reset() => accumulated = 0;
    public int Advance(double elapsedSeconds, double speed, Action tick)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0 || !double.IsFinite(speed) || speed <= 0) return 0;
        accumulated += Math.Min(elapsedSeconds, maxCatchUpSeconds) * speed;
        var count = 0;
        var tickSeconds = 1 / ticksPerSecond;
        while (accumulated + 1e-10 >= tickSeconds)
        {
            accumulated -= tickSeconds;
            tick();
            count++;
        }
        return count;
    }
}

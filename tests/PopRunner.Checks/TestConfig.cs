using PopRunner.Core;

namespace PopRunner.Checks;

/// <summary>Small test inputs, deliberately independent of the shipped animation data.</summary>
internal static class TestConfig
{
    internal static AnimationConfig Create(params FrameStep[] frames) => new()
    {
        Clock = new ClockConfig { TicksPerSecond = 10, MaxCatchUpSeconds = 0.25 },
        Desktop = new DesktopConfig { RandomScenarioIds = ["fixture"], SpriteScale = 1 },
        Stage = new StageConfig
        {
            Width = 200, Height = 100, FloorY = 75,
            MovementUnitPixels = 3, PreStretchPixelSize = 1,
            PixelAspectY = 1, QuantizeToPixels = false
        },
        // Engine tests need metadata only. Configuration tests supply an embedded image path.
        Sprites = Enumerable.Range(1, 8).ToDictionary(id => id, _ => new SpriteFrame { Image = "unused.png" }),
        Clips = new Dictionary<string, AnimationClip>
        {
            ["test"] = new() { Label = "Test clip", Frames = frames.Length == 0 ? [new() { Frame = 1 }] : frames.ToList() }
        },
        Scenarios = [new() { Id = "fixture", StartX = 10, Direction = 1, Steps = [new() { Clip = "test" }] }]
    };
}

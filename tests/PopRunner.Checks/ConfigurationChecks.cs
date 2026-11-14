using System.Text;
using System.Text.Json;
using PopRunner.Core;

namespace PopRunner.Checks;

internal static class ConfigurationChecks
{
    public static void Run()
    {
        Check.Run("Configuration: comments, trailing commas, and case-insensitive names", () =>
        {
            var json = Serialize(ValidConfig()).Replace("\"Clock\"", "\"CLOCK\"", StringComparison.Ordinal);
            var loaded = Load("/* fixture */" + json[..^1] + ",}");
            Check.Near(loaded.Clock.TicksPerSecond, 10, "The customized clock must survive permissive JSON parsing.");
            Check.That(loaded.Scenarios[0].Id == "fixture", "The fixture scenario must survive parsing.");
        });

        Check.Run("Configuration: unknown root and nested properties are rejected", () =>
        {
            var json = Serialize(ValidConfig());
            Check.Throws<JsonException>(() => Load("{\"UnknownSetting\":1," + json[1..]),
                "An unknown root property must not silently load.");
            var nestedTypo = json.Replace("\"Clock\":{", "\"Clock\":{\"UnknownSetting\":1,", StringComparison.Ordinal);
            Check.That(nestedTypo != json, "The nested-property fixture must insert its typo.");
            Check.Throws<JsonException>(() => Load(nestedTypo), "An unknown nested property must not silently load.");
        });

        Check.Run("Configuration: null and malformed documents are rejected", () =>
        {
            Check.Throws<InvalidDataException>(() => Load("null"), "A null configuration must be rejected.");
            Check.Throws<JsonException>(() => Load("{"), "A malformed document must be rejected.");
            Check.Throws<ArgumentNullException>(() => AnimationConfig.Load(null!), "A null stream must be rejected.");
        });

        Check.Run("Configuration: invalid clock and desktop settings are rejected", () =>
        {
            Reject(c => c.Clock.TicksPerSecond = 0, "A zero tick rate");
            Reject(c => c.Clock.MaxCatchUpSeconds = -1, "A negative catch-up interval");
            Reject(c => c.Scenarios[0].TicksPerSecond = 0, "A zero scenario tick rate");
            Reject(c => c.Desktop.SpriteScale = 0, "A zero desktop sprite scale");
            Reject(c => c.Desktop.TimerIntervalMilliseconds = 0, "A zero desktop timer interval");
            Reject(c => c.Desktop.TaskbarGapPixels = -1, "A negative taskbar gap");
            Reject(c => c.Desktop.ActionEdgeFraction = 0.75, "An action beyond the nearer half of the monitor");
        });

        Check.Run("Configuration: invalid stage geometry is rejected", () =>
        {
            Reject(c => c.Stage.Width = 0, "An empty stage");
            Reject(c => c.Stage.FloorY = c.Stage.Height, "A floor outside the stage");
            Reject(c => c.Stage.MovementUnitPixels = 0, "A zero movement unit");
            Reject(c => c.Stage.PixelAspectY = 0, "A zero pixel aspect ratio");
            Reject(c => c.Stage.PreStretchPixelSize = 0, "A zero prestretch pixel size");
        });

        Check.Run("Configuration: missing images, frames, and clips are rejected", () =>
        {
            Reject(c => c.Sprites[1].Image = "missing-image.png", "An unavailable image");
            Reject(c => c.Clips["test"].Frames.Clear(), "An empty clip");
            Reject(c => c.Clips["test"].Frames[0].Frame = int.MaxValue, "An unavailable frame");
            Reject(c => c.Clips["test"].Frames[0].Ticks = 0, "A zero frame duration");
            Reject(c => c.Scenarios[0].Steps[0].Clip = "missing-clip", "An unavailable clip");
        });

        Check.Run("Configuration: invalid scenario steps and travel targets are rejected", () =>
        {
            Reject(c => c.Scenarios.Clear(), "A configuration without scenarios");
            Reject(c => c.Scenarios.Add(c.Scenarios[0]), "Duplicate scenario IDs");
            Reject(c => c.Scenarios[0].Direction = 0, "An invalid facing direction");
            Reject(c => c.Scenarios[0].StartDelayTicks = -1, "A negative start delay");
            Reject(c => c.Scenarios[0].StartDelayTicks = 10001, "A start delay beyond the supported limit");
            Reject(c => c.Scenarios[0].Steps.Clear(), "An empty scenario");
            Reject(c => c.Scenarios[0].Steps[0].StartFrameIndex = 1, "An out-of-range starting frame");
            Reject(c => c.Scenarios[0].Steps[0].Repeat = 0, "A zero repeat count");
            Reject(c => c.Scenarios[0].Steps[0].ExitOnFrames = [int.MaxValue], "An unavailable exit frame");
            Reject(c =>
            {
                c.Scenarios[0].DesktopEdgeEntry = true;
                c.Clips["test"].Loop = true;
                c.Scenarios[0].Steps[0].UntilX = 20;
            }, "A finite desktop edge route with an unbounded travel step");
            Reject(c =>
            {
                c.Clips["test"].Loop = false;
                c.Scenarios[0].Steps[0].UntilX = 20;
            }, "A travel target on a non-looping clip");
            Reject(c =>
            {
                c.Clips["test"].Loop = true;
                c.Clips["test"].Frames[0].Dx = -1;
                c.Scenarios[0].Steps[0].UntilX = 20;
            }, "A travel clip that moves against its facing direction");
            Reject(c =>
            {
                c.Clips["test"].Loop = true;
                c.Scenarios[0].Steps[0].UntilX = 1_000_000;
            }, "A travel target that cannot finish within the validation budget");
        });

        Check.Run("Configuration: incomplete potion definitions are rejected", () =>
        {
            Reject(c => c.Scenarios[0].HasPotion = true, "A potion scenario without potion graphics");
            Reject(c => c.PotionGraphics = new PotionGraphics { BodyFrame = 1, BubbleFrames = [] },
                "A potion without bubble frames");
            Reject(c => c.PotionGraphics = new PotionGraphics { BodyFrame = int.MaxValue, BubbleFrames = [2] },
                "An unavailable potion body");
            Reject(c => c.PotionGraphics = new PotionGraphics { BodyFrame = 1, BubbleFrames = [int.MaxValue] },
                "An unavailable potion bubble");
            Reject(c => c.PotionGraphics = new PotionGraphics { BodyFrame = 1, BubbleFrames = [2], TicksPerFrame = 0 },
                "A zero bubble duration");
            Reject(c => c.PotionGraphics = new PotionGraphics { BodyFrame = 1, BubbleFrames = [2], InitialBubbleFrame = 1 },
                "An out-of-range initial bubble");
        });

        Check.Run("Configuration: invalid supporting actors are rejected", () =>
        {
            Reject(c => AddActor(c, name: ""), "An unnamed supporting actor");
            Reject(c =>
            {
                AddActor(c);
                AddActor(c);
            }, "Duplicate supporting actor names");
            Reject(c => AddActor(c, direction: 0), "An invalid supporting actor facing direction");
            Reject(c => AddActor(c, clip: "missing-clip"), "An unavailable supporting actor clip");
        });

        Check.Run("Configuration: invalid random-play selections are rejected", () =>
        {
            Reject(c => c.Desktop.RandomScenarioIds = [], "An empty random-play selection");
            Reject(c => c.Desktop.RandomScenarioIds = ["missing-scenario"], "An unavailable random-play scenario");
            Reject(c => c.Desktop.RandomScenarioIds = ["fixture", "fixture"], "Duplicate random-play scenario IDs");
        });

        Check.Run("Configuration: valid custom settings, potion, and actors round-trip", () =>
        {
            var config = ValidConfig();
            config.Clock.TicksPerSecond = 24;
            config.Scenarios[0].TicksPerSecond = 8;
            config.Scenarios[0].StartDelayTicks = 3;
            config.Scenarios[0].DesktopEdgeEntry = true;
            config.Desktop.SpriteScale = 3;
            config.Desktop.ActionEdgeFraction = 0.4;
            config.Scenarios[0].HasPotion = true;
            config.PotionGraphics = new PotionGraphics
            {
                BodyFrame = 1, BubbleFrames = [2, 3], TicksPerFrame = 2, InitialBubbleFrame = 1,
                BodyOffsetX = 0.5, BodyOffsetY = 2.5, BubbleOffsetY = -3, AlignWithFeet = true
            };
            AddActor(config);
            var json = Serialize(config);
            var loaded = Load(json);
            Check.Near(loaded.Clock.TicksPerSecond, 24, "The global tick rate must round-trip.");
            Check.Near(loaded.Scenarios[0].TicksPerSecond!.Value, 8, "The scenario clock override must round-trip.");
            Check.That(loaded.Scenarios[0].StartDelayTicks == 3, "The scenario start delay must round-trip.");
            Check.That(loaded.Scenarios[0].DesktopEdgeEntry, "The finite desktop edge placement must round-trip.");
            Check.Near(loaded.Desktop.SpriteScale, 3, "The desktop scale must round-trip.");
            Check.Near(loaded.Desktop.ActionEdgeFraction, 0.4, "The action position must round-trip.");
            Check.That(loaded.PotionGraphics!.BubbleFrames.SequenceEqual(new[] { 2, 3 }), "The custom bubble sequence must round-trip.");
            Check.That(loaded.PotionGraphics.AlignWithFeet, "The foot-alignment setting must round-trip.");
            Check.Near(loaded.PotionGraphics.VerticalOffsetY, -2.5, "The presentation offset is computed from the loaded settings.");
            Check.That(!json.Contains("VerticalOffsetY", StringComparison.OrdinalIgnoreCase),
                "The computed presentation offset must not be serialized.");
            var omittedAlignment = json.Replace("\"AlignWithFeet\":true,", "", StringComparison.Ordinal);
            Check.That(omittedAlignment != json, "The legacy fixture must omit the alignment setting.");
            var legacy = Load(omittedAlignment);
            Check.That(!legacy.PotionGraphics!.AlignWithFeet && legacy.PotionGraphics.VerticalOffsetY == 0,
                "Omitting the alignment setting preserves the original placement behavior.");
            var omittedDelay = json.Replace(",\"StartDelayTicks\":3", "", StringComparison.Ordinal);
            Check.That(omittedDelay != json, "The legacy fixture must omit the start delay.");
            Check.That(Load(omittedDelay).Scenarios[0].StartDelayTicks == 0,
                "Omitting the start delay preserves immediate animation startup.");
            Check.That(loaded.Scenarios[0].Actors[0].Name == "companion", "The custom actor must round-trip.");
        });

        UnionJsonChecks.Run();
    }

    private static AnimationConfig ValidConfig()
    {
        var config = TestConfig.Create(new FrameStep { Frame = 1, Ticks = 1, Dx = 2 });
        var image = AnimationResources.ImagePaths.First();
        foreach (var sprite in config.Sprites.Values) sprite.Image = image;
        return config;
    }

    private static void AddActor(AnimationConfig config, string name = "companion", int direction = -1, string clip = "test") =>
        config.Scenarios[0].Actors.Add(new SupportingActor
        {
            Name = name, Direction = direction, StartX = 40, Steps = [new ScenarioStep { Clip = clip }]
        });

    private static void Reject(Action<AnimationConfig> change, string description)
    {
        var config = ValidConfig();
        change(config);
        Check.Throws<InvalidDataException>(() => Load(Serialize(config)), description + " must be rejected.");
    }

    private static string Serialize(AnimationConfig config) => JsonSerializer.Serialize(config, ChecksJsonContext.Default.AnimationConfig);

    private static AnimationConfig Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AnimationConfig.Load(stream);
    }
}

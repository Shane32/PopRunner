using System.Text;
using System.Text.Json;
using PopRunner.Core;

namespace PopRunner.Checks;

/// <summary>JSON shorthand contracts tested with small scripts independent of the production choreography.</summary>
internal static class UnionJsonChecks
{
    internal static void Run()
    {
        Check.Run("Configuration: mixed shorthand and objects resolve to equivalent playback", () =>
        {
            var image = AnimationResources.ImagePaths.First();
            var shortImage = Quote(Path.GetFileName(image));
            var expanded = Load(Fixture(
                sprite: "{\"image\":" + Quote(image) + "}",
                frames: "{\"frame\":1}," + CustomFrame + ",{\"frame\":3}",
                steps: "{\"clip\":\"plain\"}," + CustomStep + "," + TravelStep + ",{\"clip\":\"plain\"}",
                actorSteps: "{\"clip\":\"plain\"},{\"clip\":\"test\",\"repeat\":2},{\"clip\":\"plain\"}"));
            var mixed = Load(Fixture(
                sprite: shortImage,
                frames: "1," + CustomFrame + ",3",
                steps: "\"plain\"," + CustomStep + "," + TravelStep + ",\"plain\"",
                actorSteps: "\"plain\",{\"clip\":\"test\",\"repeat\":2},\"plain\"",
                objectImage: shortImage));

            Check.That(mixed.Sprites[1].Image == image && mixed.Sprites[2].Image == image,
                "Bare filenames normalize to the embedded path in both sprite representations.");
            Check.That(Serialize(expanded) == Serialize(mixed),
                "Shorthand and explicit objects must resolve to the same complete configuration.");
            var sprite = mixed.Sprites[2];
            Check.That(sprite.AnchorX == 1.25 && sprite.AnchorY == -0.5 && sprite.OddPixel,
                "The sprite object retains anchor and parity overrides.");
            var frame = mixed.Clips["test"].Frames[1];
            Check.That(frame.Frame == 2 && frame.Ticks == 3 && frame.Dx == 2 && frame.Flip &&
                frame.HidePotion && frame.ConsumePotion && frame.HidePrimaryActor,
                "The frame object retains movement, hold duration, flip, and visibility events.");
            var step = mixed.Scenarios[0].Steps[1];
            Check.That(step.StartFrameIndex == 1 && step.Repeat == 2 && step.UntilX == null && step.ExitOnFrames.Length == 0,
                "The step object retains overrides and supplies defaults for omitted settings.");
            var travel = mixed.Scenarios[0].Steps[2];
            Check.That(travel.UntilX == 20 && travel.ExitOnFrames.SequenceEqual(new[] { 1 }),
                "The step object retains travel targets and allowed exit poses.");
            var expectedTrace = Trace(expanded);
            var actualTrace = Trace(mixed);
            Check.That(actualTrace.SequenceEqual(expectedTrace),
                "Both forms must produce identical primary/supporting frame, movement, placement, and event states at every tick.");
        });

        Check.Run("Configuration: serialization chooses shorthand only for default objects", () =>
        {
            var loaded = Load(Fixture(
                frames: "1," + CustomFrame + ",3",
                steps: "\"plain\"," + CustomStep + "," + TravelStep,
                actorSteps: "\"plain\",{\"clip\":\"test\",\"repeat\":2}"));
            var json = Serialize(loaded);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            Check.That(root.GetProperty("Sprites").GetProperty("1").ValueKind == JsonValueKind.String &&
                root.GetProperty("Sprites").GetProperty("2").ValueKind == JsonValueKind.Object,
                "Only a sprite with default metadata is written as an image string.");
            var frames = root.GetProperty("Clips").GetProperty("test").GetProperty("Frames");
            Check.That(frames[0].ValueKind == JsonValueKind.Number && frames[1].ValueKind == JsonValueKind.Object,
                "Only a frame with default timing and commands is written as a number.");
            var scenario = root.GetProperty("Scenarios")[0];
            Check.That(scenario.GetProperty("Steps")[0].ValueKind == JsonValueKind.String &&
                scenario.GetProperty("Steps")[1].ValueKind == JsonValueKind.Object &&
                scenario.GetProperty("Actors")[0].GetProperty("Steps")[0].ValueKind == JsonValueKind.String &&
                scenario.GetProperty("Actors")[0].GetProperty("Steps")[1].ValueKind == JsonValueKind.Object,
                "Primary and supporting step lists use strings only when all step options are defaults.");
            var reloaded = Load(json);
            Check.That(Serialize(reloaded) == json && Trace(reloaded).SequenceEqual(Trace(loaded)),
                "Canonical serialization must preserve the configuration and its full playback trace.");
        });

        Check.Run("Configuration: union object property names stay case-insensitive and strict", () =>
        {
            var image = Quote(Path.GetFileName(AnimationResources.ImagePaths.First()));
            var loaded = Load(Fixture(
                sprite: "{\"IMAGE\":" + image + ",\"ANCHORX\":2}",
                frames: "{\"FRAME\":1,\"TICKS\":2,\"DX\":1}",
                steps: "{\"CLIP\":\"test\",\"REPEAT\":2}",
                actorSteps: "{\"CLIP\":\"test\",\"REPEAT\":3}"));
            Check.That(loaded.Sprites[1].AnchorX == 2 && loaded.Clips["test"].Frames[0].Ticks == 2 &&
                loaded.Clips["test"].Frames[0].Dx == 1 && loaded.Scenarios[0].Steps[0].Repeat == 2 &&
                loaded.Scenarios[0].Actors[0].Steps[0].Repeat == 3,
                "Object union branches use the same case-insensitive source-generated metadata as the full document.");

            foreach (var json in new[]
            {
                Fixture(sprite: "{\"image\":" + image + ",\"unknown\":1}"),
                Fixture(frames: "{\"frame\":1,\"unknown\":1}"),
                Fixture(steps: "{\"clip\":\"test\",\"unknown\":1}"),
                Fixture(actorSteps: "{\"clip\":\"test\",\"unknown\":1}")
            })
                Check.Throws<JsonException>(() => Load(json), "Unknown properties inside every union object branch must be rejected.");
        });

        Check.Run("Configuration: invalid union token types and frame numbers are rejected", () =>
        {
            foreach (var token in new[] { "null", "true", "7", "[]", "[\"image.png\"]" })
                Check.Throws<JsonException>(() => Load(Fixture(sprite: token)),
                    "A sprite entry must be an image string or a sprite object: " + token);
            foreach (var token in new[] { "null", "true", "\"1\"", "[]", "[1]", "1.5", "2147483648", "-2147483649" })
                Check.Throws<JsonException>(() => Load(Fixture(frames: token)),
                    "A frame entry must be an object or an Int32 frame number: " + token);
            foreach (var token in new[] { "null", "true", "7", "[]", "[\"test\"]" })
            {
                Check.Throws<JsonException>(() => Load(Fixture(steps: token)),
                    "A primary step must be a clip string or a step object: " + token);
                Check.Throws<JsonException>(() => Load(Fixture(actorSteps: token)),
                    "A supporting step must be a clip string or a step object: " + token);
            }
        });

        Check.Run("Configuration: shorthand still validates image, frame, and clip references", () =>
        {
            foreach (var json in new[]
            {
                Fixture(sprite: "\"missing-image.png\""),
                Fixture(sprite: "\"\""),
                Fixture(sprite: "{}"),
                Fixture(frames: "2147483647"),
                Fixture(frames: "{}"),
                Fixture(steps: "\"missing-clip\""),
                Fixture(steps: "\"\""),
                Fixture(steps: "{}"),
                Fixture(actorSteps: "\"missing-clip\"")
            })
                Check.Throws<InvalidDataException>(() => Load(json),
                    "Syntactically valid shorthand and objects must still pass configuration reference validation.");
        });
    }

    private const string CustomFrame = "{\"frame\":2,\"ticks\":3,\"dx\":2,\"flip\":true,\"hidePotion\":true,\"consumePotion\":true,\"hidePrimaryActor\":true}";
    private const string CustomStep = "{\"clip\":\"test\",\"startFrameIndex\":1,\"repeat\":2}";
    private const string TravelStep = "{\"clip\":\"travel\",\"untilX\":20,\"exitOnFrames\":[1]}";

    private static string Fixture(string? sprite = null, string frames = "1", string steps = "\"test\"",
        string actorSteps = "\"test\"", string? objectImage = null)
    {
        var image = Quote(AnimationResources.ImagePaths.First());
        return $$"""
            {
              "desktop": { "randomScenarioIds": ["fixture"] },
              "sprites": {
                "1": {{sprite ?? image}},
                "2": { "image": {{objectImage ?? image}}, "anchorX": 1.25, "anchorY": -0.5, "oddPixel": true },
                "3": {{image}}
              },
              "potionGraphics": { "bodyFrame": 3, "bubbleFrames": [1, 2], "ticksPerFrame": 2 },
              "clips": {
                "plain": { "frames": [1] },
                "test": { "exitDx": 0.5, "exitFlip": true, "frames": [{{frames}}] },
                "travel": { "loop": true, "frames": [{ "frame": 1, "dx": 1 }, 3] }
              },
              "scenarios": [{
                "id": "fixture", "startX": 10, "hasPotion": true, "startDelayTicks": 2,
                "steps": [{{steps}}],
                "actors": [{ "name": "companion", "startX": 30, "steps": [{{actorSteps}}] }]
              }]
            }
            """;
    }

    private static List<string> Trace(AnimationConfig config)
    {
        var engine = new AnimationEngine(config, config.Scenarios[0]);
        var trace = new List<string>();
        for (var tick = 0; tick < 1000; tick++)
        {
            trace.Add(State(engine) + ";" + string.Join(";", engine.SupportingActors.Select(State)) +
                ";" + engine.GetPotionPlacement(5, 9) + ";" + engine.GetPotionPlacement(3, 4, bubbles: true));
            if (engine.Completed) return trace;
            engine.Tick();
        }
        throw new InvalidOperationException("The synthetic union fixture must complete within its test budget.");
    }

    private static string State(AnimationEngine engine) => FormattableString.Invariant(
        $"{engine.ElapsedTicks}|{engine.StartDelayTicksRemaining}|{engine.Frame}|{engine.X}|{engine.Direction}|{engine.StepIndex}|{engine.ClipName}|{engine.Completed}|{engine.PotionVisible}|{engine.PotionConsumed}|{engine.PotionBubbleFrame}|{engine.HidesPrimaryActor}|{engine.PrimaryVisible}|{engine.Sprite.Image}|{engine.GetPlacement(7, 11)}");

    private static string Quote(string text) => "\"" + JsonEncodedText.Encode(text) + "\"";
    private static string Serialize(AnimationConfig config) => JsonSerializer.Serialize(config, ChecksJsonContext.Default.AnimationConfig);
    private static AnimationConfig Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AnimationConfig.Load(stream);
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace PopRunner.Core;

// JSON shorthand is expanded at the boundary so playback uses the same model for either spelling.
internal union SpriteEntry(SpriteFrame, string);
internal union FrameEntry(FrameStep, int);
internal union StepEntry(ScenarioStep, string);

/// <summary>Reads sprite objects or image filenames and writes shorthand when sprite metadata is default.</summary>
public sealed class SpriteEntriesJsonConverter : JsonConverter<Dictionary<int, SpriteFrame>>
{
    public override bool HandleNull => true;

    public override Dictionary<int, SpriteFrame> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Sprites must be an object keyed by frame number.");

        var metadata = (JsonTypeInfo<SpriteFrame>)options.GetTypeInfo(typeof(SpriteFrame));
        var sprites = new Dictionary<int, SpriteFrame>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return sprites;
            if (reader.TokenType != JsonTokenType.PropertyName ||
                !int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame))
                throw new JsonException("A sprite key must be an Int32 frame number.");
            if (!reader.Read()) throw new JsonException("Missing sprite entry.");

            SpriteEntry entry = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString()!,
                JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, metadata)
                    ?? throw new JsonException("A sprite entry cannot be null."),
                _ => throw new JsonException("A sprite entry must be an image filename or a sprite object.")
            };
            sprites[frame] = entry switch
            {
                SpriteFrame sprite => sprite,
                string image => new SpriteFrame { Image = image }
            };
        }
        throw new JsonException("Unterminated sprites object.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<int, SpriteFrame> value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("Sprites cannot be null.");
        var metadata = (JsonTypeInfo<SpriteFrame>)options.GetTypeInfo(typeof(SpriteFrame));
        writer.WriteStartObject();
        foreach (var (frame, sprite) in value)
        {
            if (sprite is null) throw new JsonException("A sprite entry cannot be null.");
            writer.WritePropertyName(frame.ToString(CultureInfo.InvariantCulture));
            SpriteEntry entry = sprite.AnchorX == 0 && sprite.AnchorY == 0 && !sprite.OddPixel
                ? sprite.Image : sprite;
            switch (entry)
            {
                case string image: writer.WriteStringValue(image); break;
                case SpriteFrame expanded: JsonSerializer.Serialize(writer, expanded, metadata); break;
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>Reads frame objects or frame numbers and writes shorthand when frame commands are default.</summary>
public sealed class FrameEntriesJsonConverter : JsonConverter<List<FrameStep>>
{
    public override bool HandleNull => true;

    public override List<FrameStep> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Frames must be an array.");

        var metadata = (JsonTypeInfo<FrameStep>)options.GetTypeInfo(typeof(FrameStep));
        var frames = new List<FrameStep>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return frames;
            FrameEntry entry = reader.TokenType switch
            {
                JsonTokenType.Number when reader.TryGetInt32(out var frame) => frame,
                JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, metadata)
                    ?? throw new JsonException("A frame entry cannot be null."),
                _ => throw new JsonException("A frame entry must be an Int32 frame number or a frame object.")
            };
            frames.Add(entry switch
            {
                FrameStep step => step,
                int frame => new FrameStep { Frame = frame }
            });
        }
        throw new JsonException("Unterminated frames array.");
    }

    public override void Write(Utf8JsonWriter writer, List<FrameStep> value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("Frames cannot be null.");
        var metadata = (JsonTypeInfo<FrameStep>)options.GetTypeInfo(typeof(FrameStep));
        writer.WriteStartArray();
        foreach (var step in value)
        {
            if (step is null) throw new JsonException("A frame entry cannot be null.");
            FrameEntry entry = step.Ticks == 1 && step.Dx == 0 && !step.Flip && !step.HidePotion &&
                !step.ConsumePotion && !step.HidePrimaryActor ? step.Frame : step;
            switch (entry)
            {
                case int frame: writer.WriteNumberValue(frame); break;
                case FrameStep expanded: JsonSerializer.Serialize(writer, expanded, metadata); break;
            }
        }
        writer.WriteEndArray();
    }
}

/// <summary>Reads step objects or clip names and writes shorthand when step options are default.</summary>
public sealed class StepEntriesJsonConverter : JsonConverter<List<ScenarioStep>>
{
    public override bool HandleNull => true;

    public override List<ScenarioStep> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Steps must be an array.");

        var metadata = (JsonTypeInfo<ScenarioStep>)options.GetTypeInfo(typeof(ScenarioStep));
        var steps = new List<ScenarioStep>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return steps;
            StepEntry entry = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString()!,
                JsonTokenType.StartObject => JsonSerializer.Deserialize(ref reader, metadata)
                    ?? throw new JsonException("A step entry cannot be null."),
                _ => throw new JsonException("A step entry must be a clip name or a step object.")
            };
            steps.Add(entry switch
            {
                ScenarioStep step => step,
                string clip => new ScenarioStep { Clip = clip }
            });
        }
        throw new JsonException("Unterminated steps array.");
    }

    public override void Write(Utf8JsonWriter writer, List<ScenarioStep> value, JsonSerializerOptions options)
    {
        if (value is null) throw new JsonException("Steps cannot be null.");
        var metadata = (JsonTypeInfo<ScenarioStep>)options.GetTypeInfo(typeof(ScenarioStep));
        writer.WriteStartArray();
        foreach (var step in value)
        {
            if (step is null) throw new JsonException("A step entry cannot be null.");
            StepEntry entry = step.StartFrameIndex == 0 && step.Repeat == 1 && step.UntilX is null &&
                step.ExitOnFrames is { Length: 0 } ? step.Clip : step;
            switch (entry)
            {
                case string clip: writer.WriteStringValue(clip); break;
                case ScenarioStep expanded: JsonSerializer.Serialize(writer, expanded, metadata); break;
            }
        }
        writer.WriteEndArray();
    }
}

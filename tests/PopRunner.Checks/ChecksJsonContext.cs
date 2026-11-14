using System.Text.Json.Serialization;
using PopRunner.Core;

namespace PopRunner.Checks;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AnimationConfig))]
[JsonSerializable(typeof(SpriteFrame))]
[JsonSerializable(typeof(FrameStep))]
[JsonSerializable(typeof(ScenarioStep))]
internal partial class ChecksJsonContext : JsonSerializerContext
{
}

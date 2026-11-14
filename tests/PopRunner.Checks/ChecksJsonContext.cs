using System.Text.Json.Serialization;
using PopRunner.Core;

namespace PopRunner.Checks;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AnimationConfig))]
internal partial class ChecksJsonContext : JsonSerializerContext
{
}

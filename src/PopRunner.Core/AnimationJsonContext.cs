using System.Text.Json;
using System.Text.Json.Serialization;

namespace PopRunner.Core;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AnimationConfig))]
internal partial class AnimationJsonContext : JsonSerializerContext
{
}

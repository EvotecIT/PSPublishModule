using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ReleaseValidationSpec))]
[JsonSerializable(typeof(ReleaseValidationReport))]
internal sealed partial class ReleaseValidationJsonContext : JsonSerializerContext
{
}

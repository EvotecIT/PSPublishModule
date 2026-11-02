using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(NuGetReleaseManifest))]
internal sealed partial class NuGetReleaseJsonContext : JsonSerializerContext
{
}

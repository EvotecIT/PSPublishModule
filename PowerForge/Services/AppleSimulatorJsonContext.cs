using System.Text.Json.Serialization;

namespace PowerForge;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AppleSimulatorSessionReceipt))]
internal sealed partial class AppleSimulatorJsonContext : JsonSerializerContext
{
}

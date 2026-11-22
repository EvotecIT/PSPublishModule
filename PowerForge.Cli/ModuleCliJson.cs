using System.Text.Json.Serialization;
using PowerForge;

namespace PowerForge.Cli;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(BufferedLogEntry[]))]
[JsonSerializable(typeof(ManagedModuleInstallPlan))]
[JsonSerializable(typeof(ManagedModuleInstallResult))]
[JsonSerializable(typeof(ManagedModuleRestoreResult))]
[JsonSerializable(typeof(ModuleManifestMetadata))]
[JsonSerializable(typeof(CliVersionResult))]
internal sealed partial class ModuleCliJson : JsonSerializerContext;

internal sealed record CliVersionResult(string Version);

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
[JsonSerializable(typeof(ManagedModuleVersionInfo[]))]
[JsonSerializable(typeof(ProjectVersionInfo[]))]
[JsonSerializable(typeof(ManagedModuleUpdatePlan))]
[JsonSerializable(typeof(ManagedModuleUpdateResult))]
[JsonSerializable(typeof(ManagedModuleUninstallPlan))]
[JsonSerializable(typeof(ManagedModuleUninstallResult[]))]
[JsonSerializable(typeof(ManagedModulePackResult))]
[JsonSerializable(typeof(ManagedModuleInstalledInfo[]))]
[JsonSerializable(typeof(ManagedModuleRepositoryProfileInfo[]))]
internal sealed partial class ModuleCliJson : JsonSerializerContext;

internal sealed record CliVersionResult(string Version);

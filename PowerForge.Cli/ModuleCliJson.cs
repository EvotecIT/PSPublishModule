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
internal sealed partial class ModuleCliJson : JsonSerializerContext;

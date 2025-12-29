using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PowerForge.Cli;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ModulePipelineSpec))]
[JsonSerializable(typeof(ModuleBuildSpec))]
[JsonSerializable(typeof(ModuleInstallSpec))]
[JsonSerializable(typeof(ModuleTestSuiteSpec))]
[JsonSerializable(typeof(ModulePipelinePlan))]
[JsonSerializable(typeof(ModuleBuildResult))]
[JsonSerializable(typeof(ModuleInstallerResult))]
[JsonSerializable(typeof(ModuleTestSuiteResult))]
[JsonSerializable(typeof(ModulePipelineResult))]
[JsonSerializable(typeof(DocumentationBuildResult))]
[JsonSerializable(typeof(ArtefactBuildResult[]))]
[JsonSerializable(typeof(NormalizationResult[]))]
[JsonSerializable(typeof(FormatterResult[]))]
[JsonSerializable(typeof(global::LogEntry[]))]
[JsonSerializable(typeof(PSResourceInfo[]))]
[JsonSerializable(typeof(ConfigurationArtefactSegment))]
[JsonSerializable(typeof(ConfigurationBuildDocumentationSegment))]
[JsonSerializable(typeof(ConfigurationBuildLibrariesSegment))]
[JsonSerializable(typeof(ConfigurationBuildSegment))]
[JsonSerializable(typeof(ConfigurationCommandSegment))]
[JsonSerializable(typeof(ConfigurationCompatibilitySegment))]
[JsonSerializable(typeof(ConfigurationDocumentationSegment))]
[JsonSerializable(typeof(ConfigurationFileConsistencySegment))]
[JsonSerializable(typeof(ConfigurationFormattingSegment))]
[JsonSerializable(typeof(ConfigurationImportModulesSegment))]
[JsonSerializable(typeof(ConfigurationInformationSegment))]
[JsonSerializable(typeof(ConfigurationManifestSegment))]
[JsonSerializable(typeof(ConfigurationModuleSegment))]
[JsonSerializable(typeof(ConfigurationModuleSkipSegment))]
[JsonSerializable(typeof(ConfigurationOptionsSegment))]
[JsonSerializable(typeof(ConfigurationPlaceHolderOptionSegment))]
[JsonSerializable(typeof(ConfigurationPlaceHolderSegment))]
[JsonSerializable(typeof(ConfigurationPublishSegment))]
[JsonSerializable(typeof(ConfigurationTestSegment))]
internal partial class PowerForgeCliJsonContext : JsonSerializerContext
{
}

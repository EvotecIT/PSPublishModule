using System;

namespace PowerForge;

internal sealed class ValidationConfigurationRequest
{
    public bool Enable { get; set; }
    public ValidationSeverity StructureSeverity { get; set; } = ValidationSeverity.Warning;
    public ValidationSeverity DocumentationSeverity { get; set; } = ValidationSeverity.Warning;
    public ValidationSeverity ScriptAnalyzerSeverity { get; set; } = ValidationSeverity.Warning;
    public ValidationSeverity FileIntegritySeverity { get; set; } = ValidationSeverity.Warning;
    public ValidationSeverity TestsSeverity { get; set; } = ValidationSeverity.Warning;
    public ValidationSeverity BinarySeverity { get; set; } = ValidationSeverity.Off;
    public ValidationSeverity CsprojSeverity { get; set; } = ValidationSeverity.Off;
    public string[] PublicFunctionPaths { get; set; } = new[] { "functions" };
    public string[] InternalFunctionPaths { get; set; } = new[] { @"internal\functions" };
    public bool ValidateManifestFiles { get; set; } = true;
    public bool ValidateExports { get; set; } = true;
    public bool ValidateInternalNotExported { get; set; } = true;
    public bool AllowWildcardExports { get; set; } = true;
    public int MinSynopsisPercent { get; set; } = 100;
    public int MinDescriptionPercent { get; set; } = 100;
    public int MinExamplesPerCommand { get; set; } = 1;
    public string[] ExcludeCommands { get; set; } = Array.Empty<string>();
    public bool EnableScriptAnalyzer { get; set; }
    public string[] ScriptAnalyzerExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };
    public string[] ScriptAnalyzerExcludeRules { get; set; } = new[] { "PSAvoidTrailingWhitespace", "PSShouldProcess" };
    public bool ScriptAnalyzerSkipIfUnavailable { get; set; } = true;
    public bool ScriptAnalyzerInstallIfUnavailable { get; set; }
    public int ScriptAnalyzerTimeoutSeconds { get; set; } = 300;
    public string[] FileIntegrityExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };
    public bool FileIntegrityCheckTrailingWhitespace { get; set; } = true;
    public bool FileIntegrityCheckSyntax { get; set; } = true;
    public string[] BannedCommands { get; set; } = Array.Empty<string>();
    public string[] AllowBannedCommandsIn { get; set; } = Array.Empty<string>();
    public bool EnableTests { get; set; }
    public string? TestsPath { get; set; }
    public string[] TestAdditionalModules { get; set; } = new[] { "Pester", "PSWriteColor" };
    public string[] TestSkipModules { get; set; } = Array.Empty<string>();
    public bool TestSkipDependencies { get; set; }
    public bool TestSkipImport { get; set; }
    public bool TestForce { get; set; }
    public int TestTimeoutSeconds { get; set; } = 600;
    public bool ValidateBinaryAssemblies { get; set; } = true;
    public bool ValidateBinaryExports { get; set; } = true;
    public bool AllowBinaryWildcardExports { get; set; } = true;
    public bool RequireTargetFramework { get; set; } = true;
    public bool RequireLibraryOutput { get; set; } = true;
}

using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for module validation checks during build.
/// </summary>
/// <remarks>
/// <para>
/// Adds a single validation segment that can run structure, documentation, test, binary, and csproj checks.
/// Each check can be configured as Off/Warning/Error to control whether it is informational or blocking.
/// </para>
/// <para>
/// Encoding and line-ending enforcement is handled by New-ConfigurationFileConsistency.
/// </para>
/// </remarks>
/// <example>
/// <summary>Enable validation with warnings for docs and errors for structure</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationValidation -Enable -StructureSeverity Error -DocumentationSeverity Warning</code>
/// </example>
/// <example>
/// <summary>Run tests and fail the build on test failures</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationValidation -Enable -EnableTests -TestsSeverity Error -TestsPath 'Tests'</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationValidation")]
public sealed class NewConfigurationValidationCommand : PSCmdlet
{
    /// <summary>Enable module validation checks during build.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Severity for module structure checks.</summary>
    [Parameter] public ValidationSeverity StructureSeverity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Severity for documentation checks.</summary>
    [Parameter] public ValidationSeverity DocumentationSeverity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Severity for PSScriptAnalyzer checks.</summary>
    [Parameter] public ValidationSeverity ScriptAnalyzerSeverity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Severity for file integrity checks.</summary>
    [Parameter] public ValidationSeverity FileIntegritySeverity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Severity for test failures.</summary>
    [Parameter] public ValidationSeverity TestsSeverity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Severity for binary export checks.</summary>
    [Parameter] public ValidationSeverity BinarySeverity { get; set; } = ValidationSeverity.Off;

    /// <summary>Severity for csproj validation checks.</summary>
    [Parameter] public ValidationSeverity CsprojSeverity { get; set; } = ValidationSeverity.Off;

    /// <summary>Relative paths to public function files (default: "functions").</summary>
    [Parameter] public string[] PublicFunctionPaths { get; set; } = new[] { "functions" };

    /// <summary>Relative paths to internal function files (default: "internal\\functions").</summary>
    [Parameter] public string[] InternalFunctionPaths { get; set; } = new[] { @"internal\functions" };

    /// <summary>Validate manifest file references (RootModule/Formats/Types/RequiredAssemblies).</summary>
    [Parameter] public bool ValidateManifestFiles { get; set; } = true;

    /// <summary>Validate that FunctionsToExport matches public functions.</summary>
    [Parameter] public bool ValidateExports { get; set; } = true;

    /// <summary>Validate that internal functions are not exported.</summary>
    [Parameter] public bool ValidateInternalNotExported { get; set; } = true;

    /// <summary>Allow wildcard exports (skip export validation if FunctionsToExport='*').</summary>
    [Parameter] public bool AllowWildcardExports { get; set; } = true;

    /// <summary>Minimum synopsis coverage percentage (default 100).</summary>
    [Parameter] public int MinSynopsisPercent { get; set; } = 100;

    /// <summary>Minimum description coverage percentage (default 100).</summary>
    [Parameter] public int MinDescriptionPercent { get; set; } = 100;

    /// <summary>Minimum examples per command (default 1).</summary>
    [Parameter] public int MinExamplesPerCommand { get; set; } = 1;

    /// <summary>Command names to exclude from documentation checks.</summary>
    [Parameter] public string[] ExcludeCommands { get; set; } = Array.Empty<string>();

    /// <summary>Enable PSScriptAnalyzer checks during validation.</summary>
    [Parameter] public SwitchParameter EnableScriptAnalyzer { get; set; }

    /// <summary>Directories to exclude from PSScriptAnalyzer checks.</summary>
    [Parameter] public string[] ScriptAnalyzerExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };

    /// <summary>PSScriptAnalyzer rules to exclude.</summary>
    [Parameter] public string[] ScriptAnalyzerExcludeRules { get; set; } = new[] { "PSAvoidTrailingWhitespace", "PSShouldProcess" };

    /// <summary>Skip PSScriptAnalyzer checks if the module is not installed.</summary>
    [Parameter] public bool ScriptAnalyzerSkipIfUnavailable { get; set; } = true;

    /// <summary>ScriptAnalyzer timeout, in seconds (default 300).</summary>
    [Parameter] public int ScriptAnalyzerTimeoutSeconds { get; set; } = 300;

    /// <summary>Directories to exclude from file integrity checks.</summary>
    [Parameter] public string[] FileIntegrityExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };

    /// <summary>Check for trailing whitespace in scripts.</summary>
    [Parameter] public bool FileIntegrityCheckTrailingWhitespace { get; set; } = true;

    /// <summary>Check for PowerShell syntax errors.</summary>
    [Parameter] public bool FileIntegrityCheckSyntax { get; set; } = true;

    /// <summary>Commands that should not appear in scripts.</summary>
    [Parameter] public string[] BannedCommands { get; set; } = Array.Empty<string>();

    /// <summary>File names allowed to use banned commands.</summary>
    [Parameter] public string[] AllowBannedCommandsIn { get; set; } = Array.Empty<string>();

    /// <summary>Enable test execution during validation.</summary>
    [Parameter] public SwitchParameter EnableTests { get; set; }

    /// <summary>Path to tests (defaults to Tests under project root).</summary>
    [Parameter] public string? TestsPath { get; set; }

    /// <summary>Additional modules to install for tests.</summary>
    [Parameter] public string[] TestAdditionalModules { get; set; } = new[] { "Pester", "PSWriteColor" };

    /// <summary>Module names to skip during test dependency installation.</summary>
    [Parameter] public string[] TestSkipModules { get; set; } = Array.Empty<string>();

    /// <summary>Skip dependency installation during tests.</summary>
    [Parameter] public SwitchParameter TestSkipDependencies { get; set; }

    /// <summary>Skip importing the module during tests.</summary>
    [Parameter] public SwitchParameter TestSkipImport { get; set; }

    /// <summary>Force dependency reinstall and module reimport during tests.</summary>
    [Parameter] public SwitchParameter TestForce { get; set; }

    /// <summary>Test timeout, in seconds (default 600).</summary>
    [Parameter] public int TestTimeoutSeconds { get; set; } = 600;

    /// <summary>Validate that binary assemblies exist.</summary>
    [Parameter] public bool ValidateBinaryAssemblies { get; set; } = true;

    /// <summary>Validate binary exports against CmdletsToExport/AliasesToExport.</summary>
    [Parameter] public bool ValidateBinaryExports { get; set; } = true;

    /// <summary>Allow wildcard exports for binary checks.</summary>
    [Parameter] public bool AllowBinaryWildcardExports { get; set; } = true;

    /// <summary>Require TargetFramework/TargetFrameworks in csproj.</summary>
    [Parameter] public bool RequireTargetFramework { get; set; } = true;

    /// <summary>Require OutputType=Library in csproj (when specified).</summary>
    [Parameter] public bool RequireLibraryOutput { get; set; } = true;

    /// <summary>Emits validation configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var settings = new ModuleValidationSettings
        {
            Enable = Enable.IsPresent,
            Structure = new ModuleStructureValidationSettings
            {
                Severity = StructureSeverity,
                PublicFunctionPaths = PublicFunctionPaths ?? Array.Empty<string>(),
                InternalFunctionPaths = InternalFunctionPaths ?? Array.Empty<string>(),
                ValidateManifestFiles = ValidateManifestFiles,
                ValidateExports = ValidateExports,
                ValidateInternalNotExported = ValidateInternalNotExported,
                AllowWildcardExports = AllowWildcardExports
            },
            Documentation = new DocumentationValidationSettings
            {
                Severity = DocumentationSeverity,
                MinSynopsisPercent = MinSynopsisPercent,
                MinDescriptionPercent = MinDescriptionPercent,
                MinExampleCountPerCommand = MinExamplesPerCommand,
                ExcludeCommands = ExcludeCommands ?? Array.Empty<string>()
            },
            ScriptAnalyzer = new ScriptAnalyzerValidationSettings
            {
                Severity = ScriptAnalyzerSeverity,
                Enable = EnableScriptAnalyzer.IsPresent,
                ExcludeDirectories = ScriptAnalyzerExcludeDirectories ?? Array.Empty<string>(),
                ExcludeRules = ScriptAnalyzerExcludeRules ?? Array.Empty<string>(),
                SkipIfUnavailable = ScriptAnalyzerSkipIfUnavailable,
                TimeoutSeconds = Math.Max(1, ScriptAnalyzerTimeoutSeconds)
            },
            FileIntegrity = new FileIntegrityValidationSettings
            {
                Severity = FileIntegritySeverity,
                ExcludeDirectories = FileIntegrityExcludeDirectories ?? Array.Empty<string>(),
                CheckTrailingWhitespace = FileIntegrityCheckTrailingWhitespace,
                CheckSyntax = FileIntegrityCheckSyntax,
                BannedCommands = BannedCommands ?? Array.Empty<string>(),
                AllowBannedCommandsIn = AllowBannedCommandsIn ?? Array.Empty<string>()
            },
            Tests = new TestSuiteValidationSettings
            {
                Severity = TestsSeverity,
                Enable = EnableTests.IsPresent,
                TestPath = TestsPath,
                AdditionalModules = TestAdditionalModules ?? Array.Empty<string>(),
                SkipModules = TestSkipModules ?? Array.Empty<string>(),
                SkipDependencies = TestSkipDependencies.IsPresent,
                SkipImport = TestSkipImport.IsPresent,
                Force = TestForce.IsPresent,
                TimeoutSeconds = Math.Max(1, TestTimeoutSeconds)
            },
            Binary = new BinaryModuleValidationSettings
            {
                Severity = BinarySeverity,
                ValidateAssembliesExist = ValidateBinaryAssemblies,
                ValidateManifestExports = ValidateBinaryExports,
                AllowWildcardExports = AllowBinaryWildcardExports
            },
            Csproj = new CsprojValidationSettings
            {
                Severity = CsprojSeverity,
                RequireTargetFramework = RequireTargetFramework,
                RequireLibraryOutput = RequireLibraryOutput
            }
        };

        var cfg = new ConfigurationValidationSegment
        {
            Settings = settings
        };

        WriteObject(cfg);
    }
}

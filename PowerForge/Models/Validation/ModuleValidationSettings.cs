namespace PowerForge;

/// <summary>
/// Severity level used by module validation checks.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>Disable the check.</summary>
    Off = 0,
    /// <summary>Treat issues as warnings.</summary>
    Warning = 1,
    /// <summary>Treat issues as errors.</summary>
    Error = 2
}

/// <summary>
/// Validation settings for module quality checks.
/// </summary>
public sealed class ModuleValidationSettings
{
    /// <summary>Enable module validation checks.</summary>
    public bool Enable { get; set; }

    /// <summary>Module structure checks (manifest, exports, file presence).</summary>
    public ModuleStructureValidationSettings Structure { get; set; } = new();

    /// <summary>Documentation coverage checks (synopsis/description/examples).</summary>
    public DocumentationValidationSettings Documentation { get; set; } = new();

    /// <summary>PSScriptAnalyzer checks.</summary>
    public ScriptAnalyzerValidationSettings ScriptAnalyzer { get; set; } = new();

    /// <summary>File integrity checks (whitespace, syntax, banned commands).</summary>
    public FileIntegrityValidationSettings FileIntegrity { get; set; } = new();

    /// <summary>Optional test suite checks (Pester).</summary>
    public TestSuiteValidationSettings Tests { get; set; } = new();

    /// <summary>Binary cmdlet checks (assembly exports vs manifest).</summary>
    public BinaryModuleValidationSettings Binary { get; set; } = new();

    /// <summary>Basic csproj checks when a project file is configured.</summary>
    public CsprojValidationSettings Csproj { get; set; } = new();
}

/// <summary>
/// Module structure validation options.
/// </summary>
public sealed class ModuleStructureValidationSettings
{
    /// <summary>Severity for module structure issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Relative paths to public functions (default: "functions").</summary>
    public string[] PublicFunctionPaths { get; set; } = new[] { "functions" };

    /// <summary>Relative paths to internal/private functions (default: "internal\\functions").</summary>
    public string[] InternalFunctionPaths { get; set; } = new[] { @"internal\functions" };

    /// <summary>Validate manifest file references (RootModule/Formats/Types/RequiredAssemblies).</summary>
    public bool ValidateManifestFiles { get; set; } = true;

    /// <summary>Validate that FunctionsToExport matches public functions.</summary>
    public bool ValidateExports { get; set; } = true;

    /// <summary>Validate that internal functions are not exported.</summary>
    public bool ValidateInternalNotExported { get; set; } = true;

    /// <summary>When true, skip export validation if FunctionsToExport contains '*'.</summary>
    public bool AllowWildcardExports { get; set; } = true;
}

/// <summary>
/// Documentation validation options.
/// </summary>
public sealed class DocumentationValidationSettings
{
    /// <summary>Severity for documentation issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Minimum percentage of commands that must have synopsis (default 100).</summary>
    public int MinSynopsisPercent { get; set; } = 100;

    /// <summary>Minimum percentage of commands that must have description (default 100).</summary>
    public int MinDescriptionPercent { get; set; } = 100;

    /// <summary>Minimum examples per command (default 1).</summary>
    public int MinExampleCountPerCommand { get; set; } = 1;

    /// <summary>Command names to exclude from documentation checks.</summary>
    public string[] ExcludeCommands { get; set; } = System.Array.Empty<string>();

    /// <summary>Timeout for help extraction, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// PSScriptAnalyzer validation options.
/// </summary>
public sealed class ScriptAnalyzerValidationSettings
{
    /// <summary>Severity for PSScriptAnalyzer issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Enable PSScriptAnalyzer checks.</summary>
    public bool Enable { get; set; }

    /// <summary>Directory names to exclude from analysis.</summary>
    public string[] ExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };

    /// <summary>PSScriptAnalyzer rule names to exclude.</summary>
    public string[] ExcludeRules { get; set; } = new[] { "PSAvoidTrailingWhitespace", "PSShouldProcess" };

    /// <summary>Skip the check if PSScriptAnalyzer is not available.</summary>
    public bool SkipIfUnavailable { get; set; } = true;

    /// <summary>Timeout for analysis, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// File integrity validation options.
/// </summary>
public sealed class FileIntegrityValidationSettings
{
    /// <summary>Severity for file integrity issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Directory names to exclude from checks.</summary>
    public string[] ExcludeDirectories { get; set; } = new[] { "tests", "TestResults", ".git", ".vs", "bin", "obj", "packages", "node_modules", "Artefacts", "Ignore" };

    /// <summary>Check for trailing whitespace.</summary>
    public bool CheckTrailingWhitespace { get; set; } = true;

    /// <summary>Check for PowerShell syntax errors.</summary>
    public bool CheckSyntax { get; set; } = true;

    /// <summary>Commands that should not appear in scripts.</summary>
    public string[] BannedCommands { get; set; } = Array.Empty<string>();

    /// <summary>File names that are allowed to use banned commands.</summary>
    public string[] AllowBannedCommandsIn { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Test suite validation options.
/// </summary>
public sealed class TestSuiteValidationSettings
{
    /// <summary>Severity for test failures.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Enable running the test suite during validation.</summary>
    public bool Enable { get; set; }

    /// <summary>Optional path to a test file/folder (defaults to Tests under project root).</summary>
    public string? TestPath { get; set; }

    /// <summary>Additional modules to install (e.g., Pester).</summary>
    public string[] AdditionalModules { get; set; } = new[] { "Pester", "PSWriteColor" };

    /// <summary>Module names to skip during dependency installation.</summary>
    public string[] SkipModules { get; set; } = System.Array.Empty<string>();

    /// <summary>Skip dependency checking and installation.</summary>
    public bool SkipDependencies { get; set; }

    /// <summary>Skip importing the module under test.</summary>
    public bool SkipImport { get; set; }

    /// <summary>Force reinstall of dependencies and reimport.</summary>
    public bool Force { get; set; }

    /// <summary>Timeout for tests, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 600;
}

/// <summary>
/// Binary module validation options.
/// </summary>
public sealed class BinaryModuleValidationSettings
{
    /// <summary>Severity for binary module issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Off;

    /// <summary>Validate that referenced assemblies exist.</summary>
    public bool ValidateAssembliesExist { get; set; } = true;

    /// <summary>Validate CmdletsToExport/AliasesToExport against detected binary exports.</summary>
    public bool ValidateManifestExports { get; set; } = true;

    /// <summary>When true, skip export validation if manifest uses '*'.</summary>
    public bool AllowWildcardExports { get; set; } = true;
}

/// <summary>
/// Basic csproj validation options.
/// </summary>
public sealed class CsprojValidationSettings
{
    /// <summary>Severity for csproj issues.</summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Off;

    /// <summary>Require a TargetFramework/TargetFrameworks entry.</summary>
    public bool RequireTargetFramework { get; set; } = true;

    /// <summary>Require OutputType to be Library (when present).</summary>
    public bool RequireLibraryOutput { get; set; } = true;
}

namespace PowerForge;

/// <summary>
/// Configuration segment that defines placeholder replacements.
/// </summary>
public sealed class ConfigurationPlaceHolderSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "PlaceHolder";

    /// <summary>Placeholder replacement configuration payload.</summary>
    public PlaceHolderReplacement Configuration { get; set; } = new();
}

/// <summary>
/// Placeholder replacement configuration payload.
/// </summary>
public sealed class PlaceHolderReplacement
{
    /// <summary>The string to find in script/module content.</summary>
    public string Find { get; set; } = string.Empty;

    /// <summary>The string to replace the Find string with.</summary>
    public string Replace { get; set; } = string.Empty;
}

/// <summary>
/// Configuration segment that defines placeholder build options.
/// </summary>
public sealed class ConfigurationPlaceHolderOptionSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "PlaceHolderOption";

    /// <summary>Placeholder option payload.</summary>
    public PlaceHolderOptionConfiguration PlaceHolderOption { get; set; } = new();
}

/// <summary>
/// Placeholder option payload for <see cref="ConfigurationPlaceHolderOptionSegment"/>.
/// </summary>
public sealed class PlaceHolderOptionConfiguration
{
    /// <summary>When true, skips built-in replacements during build.</summary>
    public bool SkipBuiltinReplacements { get; set; }
}

/// <summary>
/// Configuration segment that describes import-module behavior.
/// </summary>
public sealed class ConfigurationImportModulesSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "ImportModules";

    /// <summary>Import configuration payload.</summary>
    public ImportModulesConfiguration ImportModules { get; set; } = new();
}

/// <summary>
/// ImportModules configuration payload for <see cref="ConfigurationImportModulesSegment"/>.
/// </summary>
public sealed class ImportModulesConfiguration
{
    /// <summary>Import the module under test/build itself.</summary>
    public bool? Self { get; set; }

    /// <summary>Import required modules from the manifest.</summary>
    public bool? RequiredModules { get; set; }

    /// <summary>Enable verbose output.</summary>
    public bool? Verbose { get; set; }
}

/// <summary>
/// Configuration segment that specifies module-skip behavior (ignore modules/commands).
/// </summary>
public sealed class ConfigurationModuleSkipSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "ModuleSkip";

    /// <summary>Module skip configuration payload.</summary>
    public ModuleSkipConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// ModuleSkip configuration payload for <see cref="ConfigurationModuleSkipSegment"/>.
/// </summary>
public sealed class ModuleSkipConfiguration
{
    /// <summary>Ignore module name(s). If the module is not available it will be ignored.</summary>
    public string[]? IgnoreModuleName { get; set; }

    /// <summary>Ignore function name(s). If the function is not available it will be ignored.</summary>
    public string[]? IgnoreFunctionName { get; set; }

    /// <summary>Continue build even if modules/commands are not available.</summary>
    public bool Force { get; set; }

    /// <summary>Fail build when unresolved commands are detected during merge.</summary>
    public bool FailOnMissingCommands { get; set; }
}

/// <summary>
/// Configuration segment that describes command reference imports.
/// </summary>
public sealed class ConfigurationCommandSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Command";

    /// <summary>Command configuration payload.</summary>
    public CommandConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Command configuration payload for <see cref="ConfigurationCommandSegment"/>.
/// </summary>
public sealed class CommandConfiguration
{
    /// <summary>Name of the module that contains the commands.</summary>
    public string? ModuleName { get; set; }

    /// <summary>One or more command names to reference from the module.</summary>
    public string[]? CommandName { get; set; }
}

/// <summary>
/// Configuration segment that describes general build information (include/exclude patterns).
/// </summary>
public sealed class ConfigurationInformationSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Information";

    /// <summary>Information configuration payload.</summary>
    public InformationConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Information configuration payload for <see cref="ConfigurationInformationSegment"/>.
/// </summary>
public sealed class InformationConfiguration
{
    /// <summary>Folder name containing public functions to export (e.g., Public).</summary>
    public string? FunctionsToExportFolder { get; set; }

    /// <summary>Folder name containing public aliases to export (e.g., Public).</summary>
    public string? AliasesToExportFolder { get; set; }

    /// <summary>Paths or patterns to exclude from artefacts.</summary>
    public string[]? ExcludeFromPackage { get; set; }

    /// <summary>File patterns from the root to include.</summary>
    public string[]? IncludeRoot { get; set; }

    /// <summary>Folder names where PS1 files should be included.</summary>
    public string[]? IncludePS1 { get; set; }

    /// <summary>Folder names to include entirely.</summary>
    public string[]? IncludeAll { get; set; }

    /// <summary>Script text executed during staging to add custom files/folders.</summary>
    public string? IncludeCustomCode { get; set; }

    /// <summary>Advanced form for include rules.</summary>
    public IncludeToArrayEntry[]? IncludeToArray { get; set; }

    /// <summary>Relative path to libraries compiled for Core (default Lib/Core).</summary>
    public string? LibrariesCore { get; set; }

    /// <summary>Relative path to libraries for classic .NET (default Lib/Default).</summary>
    public string? LibrariesDefault { get; set; }

    /// <summary>Relative path to libraries for .NET Standard (default Lib/Standard).</summary>
    public string? LibrariesStandard { get; set; }
}

/// <summary>
/// Entry used by <see cref="InformationConfiguration.IncludeToArray"/>.
/// </summary>
public sealed class IncludeToArrayEntry
{
    /// <summary>Include group key (e.g., IncludeRoot).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Include values for the key.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();
}


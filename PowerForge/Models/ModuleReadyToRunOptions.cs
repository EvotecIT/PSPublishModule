namespace PowerForge;

/// <summary>
/// Opt-in ReadyToRun payloads for a binary module: one precompiled PowerShell 7 build per runtime identifier,
/// loaded instead of the portable IL build on a matching host. Every other host keeps the portable build.
/// </summary>
public sealed class ModuleReadyToRunOptions
{
    /// <summary>Target framework used when none is configured.</summary>
    public const string DefaultFramework = "net10.0";

    /// <summary>Runtime identifiers used when none are configured.</summary>
    public static readonly string[] DefaultRuntimeIdentifiers = { "win-x64" };

    /// <summary>When true, the build adds a ReadyToRun payload for each runtime identifier.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Target framework the ReadyToRun payloads are compiled for (for example net10.0). The binary project must target it.
    /// A host loads the payload only when its .NET runtime is at least this version.
    /// </summary>
    public string Framework { get; set; } = DefaultFramework;

    /// <summary>Runtime identifiers that get a precompiled payload (for example win-x64, linux-x64, osx-arm64).</summary>
    public string[] RuntimeIdentifiers { get; set; } = (string[])DefaultRuntimeIdentifiers.Clone();

    /// <summary>Which assemblies are precompiled; the rest of the payload stays IL and is compiled when first used.</summary>
    public ModuleReadyToRunScope Scope { get; set; } = ModuleReadyToRunScope.ModuleAssemblies;
}

/// <summary>Selects the assemblies a ReadyToRun module payload precompiles.</summary>
public enum ModuleReadyToRunScope
{
    /// <summary>Precompile the module project and its project references; NuGet package assemblies stay IL.</summary>
    ModuleAssemblies = 0,

    /// <summary>Precompile every managed assembly in the payload, NuGet package assemblies included.</summary>
    All = 1
}

namespace PowerForge;

/// <summary>
/// High-level publish styles for producing distributable .NET outputs.
/// </summary>
public enum DotNetPublishStyle
{
    /// <summary>Single-file, self-contained publish (IL + JIT). Intended as the default "portable" distribution.</summary>
    Portable,
    /// <summary>Single-file, self-contained publish (IL + JIT) tuned for maximum compatibility (no aggressive trimming).</summary>
    PortableCompat,
    /// <summary>Single-file, self-contained publish (IL + JIT) tuned for size (trimming enabled where supported).</summary>
    PortableSize,
    /// <summary>NativeAOT publish optimized for startup/runtime speed.</summary>
    AotSpeed,
    /// <summary>NativeAOT publish optimized for size.</summary>
    AotSize
}

/// <summary>
/// Broad category of the dotnet publish target.
/// </summary>
public enum DotNetPublishTargetKind
{
    /// <summary>Unknown / not specified.</summary>
    Unknown,
    /// <summary>Command-line application.</summary>
    Cli,
    /// <summary>Long-running service application.</summary>
    Service,
    /// <summary>Library / shared component.</summary>
    Library
}

/// <summary>
/// Kind of step executed by the dotnet publish pipeline.
/// </summary>
public enum DotNetPublishStepKind
{
    /// <summary>Restore NuGet packages.</summary>
    Restore,
    /// <summary>Clean build outputs.</summary>
    Clean,
    /// <summary>Build the solution/project.</summary>
    Build,
    /// <summary>Publish a target for a runtime identifier.</summary>
    Publish,
    /// <summary>Write manifest outputs.</summary>
    Manifest
}


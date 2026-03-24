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
    /// <summary>Framework-dependent publish (runtime required on target machine). Smaller than self-contained outputs.</summary>
    FrameworkDependent,
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
/// Policy mode used by dotnet publish safety/signing decisions.
/// </summary>
public enum DotNetPublishPolicyMode
{
    /// <summary>Emit a warning and continue.</summary>
    Warn,
    /// <summary>Fail the run immediately.</summary>
    Fail,
    /// <summary>Silently skip (verbose-only diagnostics).</summary>
    Skip
}

/// <summary>
/// Execution mode for service lifecycle actions.
/// </summary>
public enum DotNetPublishServiceLifecycleMode
{
    /// <summary>Run lifecycle as a dedicated pipeline step after publish.</summary>
    Step,
    /// <summary>Run lifecycle inline with publish for rebuild flows (stop/delete before deploy, install/start after deploy).</summary>
    InlineRebuild
}

/// <summary>
/// Harvest mode for MSI prepare payload processing.
/// </summary>
public enum DotNetPublishMsiHarvestMode
{
    /// <summary>Do not generate harvest output.</summary>
    None,
    /// <summary>Generate a WiX fragment for all files in prepared payload.</summary>
    Auto
}

/// <summary>
/// Microsoft Store / MSIX packaging build mode.
/// </summary>
public enum DotNetPublishStoreBuildMode
{
    /// <summary>Build Store-upload packages suitable for Partner Center submission flows.</summary>
    StoreUpload,
    /// <summary>Build sideload packages for local/private distribution.</summary>
    SideloadOnly
}

/// <summary>
/// Appx/MSIX bundle generation mode for Store packaging.
/// </summary>
public enum DotNetPublishStoreBundleMode
{
    /// <summary>Let the packaging project decide automatically.</summary>
    Auto,
    /// <summary>Always produce a bundle when the packaging project supports it.</summary>
    Always,
    /// <summary>Never produce a bundle.</summary>
    Never
}

/// <summary>
/// Extraction source type for benchmark metrics.
/// </summary>
public enum DotNetPublishBenchmarkMetricSource
{
    /// <summary>Extract metric from JSON payload using a dot-path.</summary>
    JsonPath,
    /// <summary>Extract metric from text/log content using regular expression.</summary>
    Regex
}

/// <summary>
/// Aggregation mode for extracted benchmark metric values.
/// </summary>
public enum DotNetPublishBenchmarkMetricAggregation
{
    /// <summary>Use the first extracted value.</summary>
    First,
    /// <summary>Use the last extracted value.</summary>
    Last,
    /// <summary>Use the minimum extracted value.</summary>
    Min,
    /// <summary>Use the maximum extracted value.</summary>
    Max,
    /// <summary>Use average of extracted values.</summary>
    Average
}

/// <summary>
/// Baseline behavior mode for benchmark gates.
/// </summary>
public enum DotNetPublishBaselineMode
{
    /// <summary>Verify current metrics against baseline thresholds.</summary>
    Verify,
    /// <summary>Update baseline metrics from current run.</summary>
    Update
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
    /// <summary>Compose a distributable bundle from one or more published target outputs.</summary>
    Bundle,
    /// <summary>Execute Windows service lifecycle actions for a published target.</summary>
    ServiceLifecycle,
    /// <summary>Prepare installer payload and manifest for a published target.</summary>
    MsiPrepare,
    /// <summary>Build installer project (for example wixproj) from prepared payload metadata.</summary>
    MsiBuild,
    /// <summary>Sign MSI build outputs using configured signing policy.</summary>
    MsiSign,
    /// <summary>Build Microsoft Store / MSIX packaging outputs.</summary>
    StorePackage,
    /// <summary>Extract benchmark metrics from JSON/log inputs.</summary>
    BenchmarkExtract,
    /// <summary>Evaluate benchmark metrics against baseline policy.</summary>
    BenchmarkGate,
    /// <summary>Write manifest outputs.</summary>
    Manifest
}

/// <summary>
/// Category of produced artifact in the dotnet publish pipeline.
/// </summary>
public enum DotNetPublishArtefactCategory
{
    /// <summary>Direct publish output from a target.</summary>
    Publish,
    /// <summary>Composed bundle output derived from one or more published targets.</summary>
    Bundle
}

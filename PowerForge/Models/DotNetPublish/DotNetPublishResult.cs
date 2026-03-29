namespace PowerForge;

/// <summary>
/// Result of executing a dotnet publish plan.
/// </summary>
public sealed class DotNetPublishResult
{
    /// <summary>True when the pipeline completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Optional failure message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Optional failure details, including output tail and log file path.
    /// </summary>
    public DotNetPublishFailure? Failure { get; set; }

    /// <summary>Published artefacts.</summary>
    public DotNetPublishArtefactResult[] Artefacts { get; set; } = Array.Empty<DotNetPublishArtefactResult>();

    /// <summary>Path to the JSON manifest written by the pipeline (when enabled).</summary>
    public string? ManifestJsonPath { get; set; }

    /// <summary>Path to the text manifest written by the pipeline (when enabled).</summary>
    public string? ManifestTextPath { get; set; }

    /// <summary>Path to the checksums file written by the pipeline (when enabled).</summary>
    public string? ChecksumsPath { get; set; }

    /// <summary>Prepared installer payloads/manifests (for example MSI prepare outputs).</summary>
    public DotNetPublishMsiPrepareResult[] MsiPrepares { get; set; } = Array.Empty<DotNetPublishMsiPrepareResult>();

    /// <summary>Installer build outputs (for example wixproj-based MSI builds).</summary>
    public DotNetPublishMsiBuildResult[] MsiBuilds { get; set; } = Array.Empty<DotNetPublishMsiBuildResult>();

    /// <summary>Microsoft Store / MSIX package build outputs.</summary>
    public DotNetPublishStorePackageResult[] StorePackages { get; set; } = Array.Empty<DotNetPublishStorePackageResult>();

    /// <summary>Benchmark gate outcomes.</summary>
    public DotNetPublishBenchmarkGateResult[] BenchmarkGates { get; set; } = Array.Empty<DotNetPublishBenchmarkGateResult>();

    /// <summary>Path to run report JSON written by the pipeline (when enabled).</summary>
    public string? RunReportPath { get; set; }
}

/// <summary>
/// Failure details for a dotnet publish run.
/// </summary>
public sealed class DotNetPublishFailure
{
    /// <summary>Failed step key.</summary>
    public string StepKey { get; set; } = string.Empty;

    /// <summary>Failed step kind.</summary>
    public DotNetPublishStepKind StepKind { get; set; }

    /// <summary>Optional target name associated with the failure.</summary>    
    public string? TargetName { get; set; }

    /// <summary>Optional target framework associated with the failure.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional runtime identifier associated with the failure.</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional installer identifier associated with the failure.</summary>
    public string? InstallerId { get; set; }

    /// <summary>Optional Store package identifier associated with the failure.</summary>
    public string? StorePackageId { get; set; }

    /// <summary>Optional gate identifier associated with the failure.</summary>
    public string? GateId { get; set; }

    /// <summary>Exit code of the failed process.</summary>
    public int ExitCode { get; set; }

    /// <summary>Command line used for the failed process.</summary>
    public string CommandLine { get; set; } = string.Empty;

    /// <summary>Working directory used for the failed process.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Tail of the captured standard output.</summary>
    public string? StdOutTail { get; set; }

    /// <summary>Tail of the captured standard error.</summary>
    public string? StdErrTail { get; set; }

    /// <summary>Path to a detailed log file (when written).</summary>
    public string? LogPath { get; set; }
}

/// <summary>
/// Published artefact information for one target/runtime.
/// </summary>
public sealed class DotNetPublishArtefactResult
{
    /// <summary>Artifact category (publish output or composed bundle).</summary>
    public DotNetPublishArtefactCategory Category { get; set; } = DotNetPublishArtefactCategory.Publish;

    /// <summary>Target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Optional bundle identifier for bundle artifacts.</summary>
    public string? BundleId { get; set; }

    /// <summary>Target kind.</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Target framework used for publish.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Publish style used.</summary>
    public DotNetPublishStyle Style { get; set; }

    /// <summary>Resolved publish directory (staging or final).</summary>
    public string PublishDir { get; set; } = string.Empty;

    /// <summary>Final output directory.</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>Optional zip file path.</summary>
    public string? ZipPath { get; set; }

    /// <summary>Total number of files in <see cref="OutputDir"/>.</summary>
    public int Files { get; set; }

    /// <summary>Total bytes of all files in <see cref="OutputDir"/>.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Path to the main executable (when detected).</summary>
    public string? ExePath { get; set; }

    /// <summary>Size of <see cref="ExePath"/> in bytes (when detected).</summary>
    public long? ExeBytes { get; set; }

    /// <summary>Cleanup stats (symbols/docs removed).</summary>
    public DotNetPublishCleanupResult Cleanup { get; set; } = new();

    /// <summary>Optional generated service package metadata and script paths.</summary>
    public DotNetPublishServicePackageResult? ServicePackage { get; set; }

    /// <summary>Optional preserve/restore state transfer summary.</summary>
    public DotNetPublishStateTransferResult? StateTransfer { get; set; }

    /// <summary>Number of publish output files that were signed.</summary>
    public int SignedFiles { get; set; }
}

/// <summary>
/// Cleanup statistics for a published output.
/// </summary>
public sealed class DotNetPublishCleanupResult
{
    /// <summary>Number of .pdb files removed.</summary>
    public int PdbRemoved { get; set; }

    /// <summary>Number of documentation files removed.</summary>
    public int DocsRemoved { get; set; }

    /// <summary>True when the ref/ directory was removed.</summary>
    public bool RefPruned { get; set; }
}

/// <summary>
/// Result of an installer payload preparation step.
/// </summary>
public sealed class DotNetPublishMsiPrepareResult
{
    /// <summary>Installer identifier.</summary>
    public string InstallerId { get; set; } = string.Empty;

    /// <summary>Source target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Source target framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Source runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Source publish style.</summary>
    public DotNetPublishStyle Style { get; set; }

    /// <summary>Source artifact category used for MSI payload preparation.</summary>
    public DotNetPublishArtefactCategory SourceCategory { get; set; } = DotNetPublishArtefactCategory.Publish;

    /// <summary>Optional source bundle identifier when the payload was prepared from a composed bundle.</summary>
    public string? BundleId { get; set; }

    /// <summary>Source publish output directory.</summary>
    public string SourceOutputDir { get; set; } = string.Empty;

    /// <summary>Prepared installer payload staging directory.</summary>
    public string StagingDir { get; set; } = string.Empty;

    /// <summary>Prepared installer manifest path.</summary>
    public string ManifestPath { get; set; } = string.Empty;

    /// <summary>Generated harvest WiX fragment path (when harvest is enabled).</summary>
    public string? HarvestPath { get; set; }

    /// <summary>Resolved WiX DirectoryRef ID used for generated harvest output.</summary>
    public string? HarvestDirectoryRefId { get; set; }

    /// <summary>Resolved WiX ComponentGroup ID used for generated harvest output.</summary>
    public string? HarvestComponentGroupId { get; set; }

    /// <summary>Total payload file count.</summary>
    public int Files { get; set; }

    /// <summary>Total payload bytes.</summary>
    public long TotalBytes { get; set; }
}

/// <summary>
/// Result of an installer build step.
/// </summary>
public sealed class DotNetPublishMsiBuildResult
{
    /// <summary>Installer identifier.</summary>
    public string InstallerId { get; set; } = string.Empty;

    /// <summary>Source target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Source target framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Source runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Source publish style.</summary>
    public DotNetPublishStyle Style { get; set; }

    /// <summary>Installer project path used by the build step.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Detected MSI output files after build.</summary>
    public string[] OutputFiles { get; set; } = Array.Empty<string>();

    /// <summary>MSI files signed by <c>msi.sign</c> step.</summary>
    public string[] SignedFiles { get; set; } = Array.Empty<string>();

    /// <summary>Resolved MSI version used for build (when version policy is enabled).</summary>
    public string? Version { get; set; }

    /// <summary>MSBuild property name used to pass MSI version.</summary>
    public string? VersionPropertyName { get; set; }

    /// <summary>Patch segment of resolved MSI version (when version policy is enabled).</summary>
    public int? VersionPatch { get; set; }

    /// <summary>Version policy state file path (when monotonic mode is enabled).</summary>
    public string? VersionStatePath { get; set; }

    /// <summary>Resolved client-license path passed to MSI build (when enabled and found).</summary>
    public string? ClientLicensePath { get; set; }

    /// <summary>MSBuild property name used to pass client-license path.</summary>
    public string? ClientLicensePropertyName { get; set; }

    /// <summary>Resolved client identifier used for license lookup (when configured).</summary>
    public string? ClientId { get; set; }
}

/// <summary>
/// Result of a Microsoft Store / MSIX packaging build step.
/// </summary>
public sealed class DotNetPublishStorePackageResult
{
    /// <summary>Store package identifier.</summary>
    public string StorePackageId { get; set; } = string.Empty;

    /// <summary>Source target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Source target framework.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Source runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Source publish style.</summary>
    public DotNetPublishStyle Style { get; set; }

    /// <summary>Packaging project path used by the build step.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Resolved Store package output directory.</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>Store/MSIX package files produced by the build.</summary>
    public string[] OutputFiles { get; set; } = Array.Empty<string>();

    /// <summary>Upload artifacts produced by the build (for example <c>*.msixupload</c>).</summary>
    public string[] UploadFiles { get; set; } = Array.Empty<string>();

    /// <summary>Symbol artifacts produced by the build (for example <c>*.appxsym</c>).</summary>
    public string[] SymbolFiles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Generated service package metadata and script paths.
/// </summary>
public sealed class DotNetPublishServicePackageResult
{
    /// <summary>Windows service name.</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Display name for the service.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Service description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Service executable path relative to output folder.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Optional command-line arguments passed to the executable.</summary>
    public string? Arguments { get; set; }

    /// <summary>Path to generated Install-Service.ps1 when created.</summary>
    public string? InstallScriptPath { get; set; }

    /// <summary>Path to generated Uninstall-Service.ps1 when created.</summary>
    public string? UninstallScriptPath { get; set; }

    /// <summary>Path to generated Run-Once.ps1 when created.</summary>
    public string? RunOnceScriptPath { get; set; }

    /// <summary>Path to generated ServicePackage.json metadata file.</summary>
    public string MetadataPath { get; set; } = string.Empty;

    /// <summary>Optional service recovery policy applied during lifecycle install.</summary>
    public DotNetPublishServiceRecoveryOptions? Recovery { get; set; }

    /// <summary>
    /// Files created by config bootstrap rules (paths relative to output folder).
    /// </summary>
    public string[] ConfigBootstrapFiles { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Preserve/restore summary for one publish artifact.
/// </summary>
public sealed class DotNetPublishStateTransferResult
{
    /// <summary>Resolved storage path where preserved state was written.</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>Configured rule-level transfer details.</summary>
    public DotNetPublishStateTransferEntry[] Entries { get; set; } = Array.Empty<DotNetPublishStateTransferEntry>();

    /// <summary>Total files preserved across all rules.</summary>
    public int PreservedFiles { get; set; }

    /// <summary>Total files restored across all rules.</summary>
    public int RestoredFiles { get; set; }

    /// <summary>Policy used when restore encounters copy failures.</summary>
    public DotNetPublishPolicyMode OnRestoreFailure { get; set; } = DotNetPublishPolicyMode.Warn;
}

/// <summary>
/// Rule-level preserve/restore result details.
/// </summary>
public sealed class DotNetPublishStateTransferEntry
{
    /// <summary>Configured source path (relative to output directory).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Configured destination path (relative to output directory).</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Whether restore overwrites existing files.</summary>
    public bool Overwrite { get; set; }

    /// <summary>Number of files preserved for this rule.</summary>
    public int PreservedFiles { get; set; }

    /// <summary>Number of files restored for this rule.</summary>
    public int RestoredFiles { get; set; }
}

/// <summary>
/// Benchmark gate execution result.
/// </summary>
public sealed class DotNetPublishBenchmarkGateResult
{
    /// <summary>Gate identifier.</summary>
    public string GateId { get; set; } = string.Empty;

    /// <summary>Resolved benchmark source input path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Resolved baseline path.</summary>
    public string BaselinePath { get; set; } = string.Empty;

    /// <summary>Verify/update baseline mode.</summary>
    public DotNetPublishBaselineMode BaselineMode { get; set; } = DotNetPublishBaselineMode.Verify;

    /// <summary>True when no enforced gate failures were detected.</summary>
    public bool Passed { get; set; }

    /// <summary>True when baseline file was updated in this run.</summary>
    public bool BaselineUpdated { get; set; }

    /// <summary>Configured relative tolerance used by this gate.</summary>
    public double RelativeTolerance { get; set; }

    /// <summary>Configured absolute tolerance in milliseconds used by this gate.</summary>
    public double AbsoluteToleranceMs { get; set; }

    /// <summary>Metric-level gate outcomes.</summary>
    public DotNetPublishBenchmarkMetricResult[] Metrics { get; set; } = Array.Empty<DotNetPublishBenchmarkMetricResult>();

    /// <summary>Diagnostic messages for missing/regressed/new metrics.</summary>
    public string[] Messages { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Benchmark metric evaluation details.
/// </summary>
public sealed class DotNetPublishBenchmarkMetricResult
{
    /// <summary>Metric identifier.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Extracted current value.</summary>
    public double? Actual { get; set; }

    /// <summary>Baseline value (verify mode).</summary>
    public double? Baseline { get; set; }

    /// <summary>Computed allowed cap (verify mode).</summary>
    public double? Allowed { get; set; }

    /// <summary>True when metric could not be extracted from source input.</summary>
    public bool MissingInSource { get; set; }

    /// <summary>True when metric was not found in baseline map.</summary>
    public bool MissingInBaseline { get; set; }

    /// <summary>True when actual value exceeded allowed cap.</summary>
    public bool Regressed { get; set; }
}

/// <summary>
/// Structured run report written to disk (when enabled).
/// </summary>
public sealed class DotNetPublishRunReport
{
    /// <summary>Run start timestamp in UTC.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>Run finish timestamp in UTC.</summary>
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>Total run duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>Overall success flag.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Optional failure summary.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Step execution timeline.</summary>
    public DotNetPublishRunReportStep[] Steps { get; set; } = Array.Empty<DotNetPublishRunReportStep>();

    /// <summary>Pipeline artifact summary.</summary>
    public DotNetPublishRunReportArtefacts Artefacts { get; set; } = new();

    /// <summary>Signing summary across publish and MSI outputs.</summary>
    public DotNetPublishRunReportSigning Signing { get; set; } = new();

    /// <summary>Benchmark gate outcomes.</summary>
    public DotNetPublishBenchmarkGateResult[] Gates { get; set; } = Array.Empty<DotNetPublishBenchmarkGateResult>();
}

/// <summary>
/// Timeline entry for one executed step.
/// </summary>
public sealed class DotNetPublishRunReportStep
{
    /// <summary>Step key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Step kind.</summary>
    public DotNetPublishStepKind Kind { get; set; }

    /// <summary>Step title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Step start timestamp in UTC.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>Step finish timestamp in UTC.</summary>
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>Step duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    /// <summary>True when step completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Optional step failure message.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Artifact summary for run report.
/// </summary>
public sealed class DotNetPublishRunReportArtefacts
{
    /// <summary>Published artifact count.</summary>
    public int PublishCount { get; set; }

    /// <summary>MSI prepare count.</summary>
    public int MsiPrepareCount { get; set; }

    /// <summary>MSI build count.</summary>
    public int MsiBuildCount { get; set; }

    /// <summary>Total output bytes across publish artifacts.</summary>
    public long TotalPublishBytes { get; set; }
}

/// <summary>
/// Signing summary for run report.
/// </summary>
public sealed class DotNetPublishRunReportSigning
{
    /// <summary>Total signed publish files.</summary>
    public int PublishFilesSigned { get; set; }

    /// <summary>Total signed MSI files.</summary>
    public int MsiFilesSigned { get; set; }
}

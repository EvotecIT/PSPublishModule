namespace PowerForge;

/// <summary>
/// A dotnet publish target entry (project + publish settings).
/// </summary>
public sealed class DotNetPublishTarget
{
    /// <summary>Friendly name used in output folders and summaries.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional project catalog ID. When set, resolves project path from <c>DotNetPublishSpec.Projects</c>.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Path to the project file (*.csproj) to publish.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Target kind (optional; affects heuristics like executable discovery).</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Publish options for this target.</summary>
    public DotNetPublishPublishOptions Publish { get; set; } = new();
}

/// <summary>
/// Publish options for a single target.
/// </summary>
public sealed class DotNetPublishPublishOptions
{
    /// <summary>
    /// Publish style (Portable/AOT etc).
    /// </summary>
    public DotNetPublishStyle Style { get; set; } = DotNetPublishStyle.Portable;

    /// <summary>
    /// Optional publish styles to expand as a matrix dimension.
    /// When provided and non-empty, this takes precedence over <see cref="Style"/>.
    /// </summary>
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();

    /// <summary>
    /// Target framework to publish (e.g. net10.0, net10.0-windows).      
    /// </summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>
    /// Optional target frameworks to publish (e.g. net10.0, net10.0-windows).
    /// When provided and non-empty, this takes precedence over <see cref="Framework"/>.
    /// </summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Runtime identifiers to publish for. When omitted/empty, uses <see cref="DotNetPublishDotNetOptions.Runtimes"/>.
    /// </summary>
    public string[] Runtimes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional output path template. Supports tokens: {target}, {rid}, {framework}, {style}, {configuration}.
    /// When omitted, defaults to <c>Artifacts/DotNetPublish/{target}/{rid}/{framework}/{style}</c>.
    /// </summary>
    public string? OutputPath { get; set; }

    /// <summary>
    /// When true, publishes into a temporary staging directory and then copies the output to the final directory.
    /// </summary>
    public bool UseStaging { get; set; } = true;

    /// <summary>
    /// When true, clears the final output directory before copying new files.
    /// </summary>
    public bool ClearOutput { get; set; } = true;

    /// <summary>
    /// When true, applies aggressive cleanup (recursive removals) to reduce output size.
    /// </summary>
    public bool Slim { get; set; } = true;

    /// <summary>
    /// When true, keeps debug symbols (*.pdb). Default: false.
    /// </summary>
    public bool KeepSymbols { get; set; }

    /// <summary>
    /// When true, keeps documentation files (*.xml, *.pdf). Default: false.
    /// </summary>
    public bool KeepDocs { get; set; }

    /// <summary>
    /// When true, prunes the <c>ref/</c> folder from publish output (where applicable).
    /// </summary>
    public bool PruneReferences { get; set; } = true;

    /// <summary>
    /// When true, creates a zip file next to the output directory.
    /// </summary>
    public bool Zip { get; set; }

    /// <summary>
    /// Optional zip output path. Supports the same tokens as <see cref="OutputPath"/>.
    /// When omitted, a zip is created in the parent directory of the output folder.
    /// </summary>
    public string? ZipPath { get; set; }

    /// <summary>
    /// Optional zip name template (when <see cref="ZipPath"/> is not provided). Supports tokens: {target}, {rid}, {framework}, {style}, {configuration}.
    /// Default: {target}-{framework}-{rid}-{style}.zip
    /// </summary>
    public string? ZipNameTemplate { get; set; }

    /// <summary>
    /// Optional executable rename (applied after publish). For Windows runtimes, <c>.exe</c> is appended when missing.
    /// </summary>
    public string? RenameTo { get; set; }

    /// <summary>
    /// Optional ReadyToRun toggle for non-AOT publish styles. When null, the project default is used.
    /// </summary>
    public bool? ReadyToRun { get; set; }

    /// <summary>
    /// Optional publish-specific MSBuild properties passed as <c>/p:Name=Value</c>.
    /// Values here override matching entries from <see cref="DotNetPublishDotNetOptions.MsBuildProperties"/>.
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }

    /// <summary>
    /// Optional style-specific publish overrides keyed by <see cref="DotNetPublishStyle"/> name.
    /// These overrides are applied after <see cref="MsBuildProperties"/> for the selected style.
    /// </summary>
    public Dictionary<string, DotNetPublishStyleOverride>? StyleOverrides { get; set; }

    /// <summary>
    /// Optional signing configuration (Windows only).
    /// </summary>
    public DotNetPublishSignOptions? Sign { get; set; }

    /// <summary>
    /// Optional named signing profile reference used when <see cref="Sign"/> is not set.
    /// </summary>
    public string? SignProfile { get; set; }

    /// <summary>
    /// Optional partial overrides applied on top of <see cref="SignProfile"/>.
    /// Ignored when <see cref="Sign"/> is set.
    /// </summary>
    public DotNetPublishSignPatch? SignOverrides { get; set; }

    /// <summary>
    /// Optional service package settings (script generation + metadata).
    /// </summary>
    public DotNetPublishServicePackageOptions? Service { get; set; }

    /// <summary>
    /// Optional preserve/restore state rules applied around publish.
    /// Useful for rebuild scenarios where config/data/log/license files should survive deployment.
    /// </summary>
    public DotNetPublishStatePreservationOptions? State { get; set; }
}

/// <summary>
/// Style-specific publish overrides for a single target.
/// </summary>
public sealed class DotNetPublishStyleOverride
{
    /// <summary>
    /// Optional publish-specific MSBuild properties passed as <c>/p:Name=Value</c>.
    /// Values here override matching entries from both global and target-level properties.
    /// </summary>
    public Dictionary<string, string>? MsBuildProperties { get; set; }
}

/// <summary>
/// Windows code-signing options for published outputs.
/// </summary>
public sealed class DotNetPublishSignOptions
{
    /// <summary>Enables Authenticode signing of *.exe and *.dll under the output folder.</summary>
    public bool Enabled { get; set; }

    /// <summary>Optional path to signtool.exe (defaults to "signtool.exe").</summary>
    public string? ToolPath { get; set; } = "signtool.exe";

    /// <summary>
    /// Policy applied when signing is enabled but signtool cannot be resolved or current OS cannot sign.
    /// </summary>
    public DotNetPublishPolicyMode OnMissingTool { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy applied when signing a specific file fails.
    /// </summary>
    public DotNetPublishPolicyMode OnSignFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>Optional certificate thumbprint (SHA1) used for signing (signtool /sha1).</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Optional certificate subject name used for signing (signtool /n).</summary>
    public string? SubjectName { get; set; }

    /// <summary>Optional timestamp URL (signtool /tr). Default: http://timestamp.digicert.com</summary>
    public string? TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    /// <summary>Optional signature description (signtool /d).</summary>
    public string? Description { get; set; }

    /// <summary>Optional URL displayed in signature (signtool /du).</summary>
    public string? Url { get; set; }

    /// <summary>Optional CSP name (signtool /csp).</summary>
    public string? Csp { get; set; }

    /// <summary>Optional key container name (signtool /kc).</summary>
    public string? KeyContainer { get; set; }
}

/// <summary>
/// Partial signing overrides used with named signing profiles.
/// </summary>
public sealed class DotNetPublishSignPatch
{
    /// <summary>Optional enable/disable override.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Optional path to signtool.exe override.</summary>
    public string? ToolPath { get; set; }

    /// <summary>Optional missing-tool policy override.</summary>
    public DotNetPublishPolicyMode? OnMissingTool { get; set; }

    /// <summary>Optional sign-failure policy override.</summary>
    public DotNetPublishPolicyMode? OnSignFailure { get; set; }

    /// <summary>Optional certificate thumbprint override.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Optional certificate subject override.</summary>
    public string? SubjectName { get; set; }

    /// <summary>Optional timestamp server override.</summary>
    public string? TimestampUrl { get; set; }

    /// <summary>Optional signature description override.</summary>
    public string? Description { get; set; }

    /// <summary>Optional signature URL override.</summary>
    public string? Url { get; set; }

    /// <summary>Optional CSP override.</summary>
    public string? Csp { get; set; }

    /// <summary>Optional key container override.</summary>
    public string? KeyContainer { get; set; }
}

/// <summary>
/// Service package settings for published outputs.
/// </summary>
public sealed class DotNetPublishServicePackageOptions
{
    /// <summary>
    /// Optional Windows service name. Defaults to target name when omitted.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Optional display name shown in service manager. Defaults to <see cref="ServiceName"/>.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional service description. Defaults to "&lt;ServiceName&gt; service".
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional executable path relative to output folder.
    /// When omitted, the pipeline attempts to auto-detect the main executable.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Optional command-line arguments appended to the service executable path.
    /// </summary>
    public string? Arguments { get; set; }

    /// <summary>
    /// When true, generates <c>Install-Service.ps1</c>. Also forced when <see cref="GenerateRunOnceScript"/> is true.
    /// </summary>
    public bool GenerateInstallScript { get; set; } = true;

    /// <summary>
    /// When true, generates <c>Uninstall-Service.ps1</c>.
    /// </summary>
    public bool GenerateUninstallScript { get; set; } = true;

    /// <summary>
    /// When true, generates <c>Run-Once.ps1</c> that invokes the install script and starts the service.
    /// </summary>
    public bool GenerateRunOnceScript { get; set; }

    /// <summary>
    /// Optional service lifecycle execution behavior (stop/delete/install/start/verify).
    /// </summary>
    public DotNetPublishServiceLifecycleOptions? Lifecycle { get; set; }

    /// <summary>
    /// Optional service recovery policy (SC failure actions).
    /// </summary>
    public DotNetPublishServiceRecoveryOptions? Recovery { get; set; }

    /// <summary>
    /// Optional config bootstrap copy rules (example -> runtime config when missing).
    /// </summary>
    public DotNetPublishConfigBootstrapRule[] ConfigBootstrap { get; set; } = Array.Empty<DotNetPublishConfigBootstrapRule>();
}

/// <summary>
/// Preserve/restore options for stateful files and folders in publish output.
/// </summary>
public sealed class DotNetPublishStatePreservationOptions
{
    /// <summary>
    /// Enables state preservation around publish.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional storage path template for preserved state.
    /// Supports tokens: {target}, {rid}, {framework}, {style}, {configuration}.
    /// Default: <c>Artifacts/DotNetPublish/State/{target}/{rid}/{framework}/{style}</c>.
    /// </summary>
    public string? StoragePath { get; set; }

    /// <summary>
    /// When true, clears the storage path before preserving state.
    /// </summary>
    public bool ClearStorage { get; set; } = true;

    /// <summary>
    /// Policy applied when a configured source path does not exist during preserve.
    /// </summary>
    public DotNetPublishPolicyMode OnMissingSource { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy applied when restore copy operations fail.
    /// </summary>
    public DotNetPublishPolicyMode OnRestoreFailure { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Source/destination rules to preserve and restore.
    /// </summary>
    public DotNetPublishStateRule[] Rules { get; set; } = Array.Empty<DotNetPublishStateRule>();
}

/// <summary>
/// Single preserve/restore rule for stateful files/folders.
/// </summary>
public sealed class DotNetPublishStateRule
{
    /// <summary>
    /// Source path relative to publish output directory to preserve.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional destination path relative to publish output for restore.
    /// When omitted, <see cref="SourcePath"/> is used.
    /// </summary>
    public string? DestinationPath { get; set; }

    /// <summary>
    /// When true, restore overwrites existing files in destination.
    /// </summary>
    public bool Overwrite { get; set; } = true;
}

/// <summary>
/// Service lifecycle execution options.
/// </summary>
public sealed class DotNetPublishServiceLifecycleOptions
{
    /// <summary>
    /// Enables service lifecycle execution for this target/runtime combination.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Service lifecycle execution mode.
    /// Default: <see cref="DotNetPublishServiceLifecycleMode.Step"/>.
    /// </summary>
    public DotNetPublishServiceLifecycleMode Mode { get; set; } = DotNetPublishServiceLifecycleMode.Step;

    /// <summary>
    /// When true, stops an existing service before delete/install.
    /// </summary>
    public bool StopIfExists { get; set; } = true;

    /// <summary>
    /// When true, deletes an existing service before install.
    /// </summary>
    public bool DeleteIfExists { get; set; } = true;

    /// <summary>
    /// When true, creates/recreates service definition from package metadata.
    /// </summary>
    public bool Install { get; set; } = true;

    /// <summary>
    /// When true, starts the service after install.
    /// </summary>
    public bool Start { get; set; } = true;

    /// <summary>
    /// When true, verifies service state after actions complete.
    /// </summary>
    public bool Verify { get; set; } = true;

    /// <summary>
    /// Stop wait timeout in seconds when waiting for service to stop.
    /// </summary>
    public int StopTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, logs intended actions without changing local services.
    /// </summary>
    public bool WhatIf { get; set; }

    /// <summary>
    /// Policy used when lifecycle is enabled on non-Windows platforms.
    /// </summary>
    public DotNetPublishPolicyMode OnUnsupportedPlatform { get; set; } = DotNetPublishPolicyMode.Warn;

    /// <summary>
    /// Policy used for lifecycle command failures.
    /// </summary>
    public DotNetPublishPolicyMode OnExecutionFailure { get; set; } = DotNetPublishPolicyMode.Fail;
}

/// <summary>
/// Service recovery policy options applied after service install.
/// </summary>
public sealed class DotNetPublishServiceRecoveryOptions
{
    /// <summary>
    /// Enables applying service recovery policy via <c>sc.exe failure</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Reset period in seconds for failure count.
    /// </summary>
    public int ResetPeriodSeconds { get; set; } = 86400;

    /// <summary>
    /// Delay in seconds before restart actions.
    /// </summary>
    public int RestartDelaySeconds { get; set; } = 60;

    /// <summary>
    /// When true, enables recovery actions for non-crash failures.
    /// </summary>
    public bool ApplyToNonCrashFailures { get; set; } = true;

    /// <summary>
    /// Policy used when recovery configuration command fails.
    /// </summary>
    public DotNetPublishPolicyMode OnFailure { get; set; } = DotNetPublishPolicyMode.Warn;
}

/// <summary>
/// Config bootstrap rule for copying template/example files into runtime paths.
/// </summary>
public sealed class DotNetPublishConfigBootstrapRule
{
    /// <summary>
    /// Source path relative to publish output folder.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Destination path relative to publish output folder.
    /// </summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>
    /// When true, allows overwriting an existing destination file. Default: false.
    /// </summary>
    public bool Overwrite { get; set; }

    /// <summary>
    /// Policy used when source file is missing.
    /// </summary>
    public DotNetPublishPolicyMode OnMissingSource { get; set; } = DotNetPublishPolicyMode.Warn;
}

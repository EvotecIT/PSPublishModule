namespace PowerForge.Compiler.App.Engine;

/// <summary>How serious a finding is for the mode it belongs to.</summary>
public enum CompileSeverity
{
    /// <summary>Advice; the mode still builds.</summary>
    Info,
    /// <summary>The mode builds, with changed behaviour or runtime fallback.</summary>
    Warning,
    /// <summary>The mode cannot build until this is fixed.</summary>
    Error
}

/// <summary>What a mode would produce for the checked input.</summary>
public enum CompileOutcome
{
    /// <summary>Everything reachable compiles to typed code; no PowerShell runtime needed.</summary>
    Native,
    /// <summary>Some functions compile to typed code, the rest run on the PowerShell runtime.</summary>
    Mixed,
    /// <summary>The script is embedded and runs on the PowerShell runtime.</summary>
    Packaged,
    /// <summary>The mode cannot build this input.</summary>
    Blocked
}

/// <summary>Where a dependency ends up.</summary>
public enum DependencyPlacement
{
    /// <summary>Compiled or carried inside the output.</summary>
    Bundled,
    /// <summary>Must exist on the machine that runs the output.</summary>
    RequiredOnTarget,
    /// <summary>Deliberately left out of the output by the selected file rules.</summary>
    Excluded,
    /// <summary>Missing or not deliverable; the build cannot include it.</summary>
    Blocked
}

/// <summary>One finding from a check or a build.</summary>
public sealed record CompileDiagnostic(
    CompileSeverity Severity,
    string Code,
    string FeatureId,
    string Message,
    string FilePath,
    int Line,
    int Column,
    string Fix = "")
{
    /// <summary>File and line for display, or an empty string when unknown.</summary>
    public string Location => string.IsNullOrEmpty(FilePath)
        ? string.Empty
        : Line > 0 ? $"{Path.GetFileName(FilePath)}:{Line}" : Path.GetFileName(FilePath);
}

/// <summary>A file, module or library the input depends on.</summary>
public sealed record CompileDependency(string Name, string Kind, DependencyPlacement Placement, string Detail)
{
    /// <summary>Where the dependency ends up, in plain words.</summary>
    public string PlacementText => Placement switch
    {
        DependencyPlacement.Bundled => "Included in the output",
        DependencyPlacement.RequiredOnTarget => "Must be installed on the machine that runs it",
        DependencyPlacement.Excluded => "Left out of the output",
        _ => "Missing or can't be included"
    };
}

/// <summary>The result of checking the input in one compilation mode.</summary>
public sealed record ModeCheck(
    PowerShellCompilationMode Mode,
    CompileOutcome Outcome,
    int TypedUnits,
    int RuntimeUnits,
    int RejectedUnits,
    IReadOnlyList<CompileDiagnostic> Diagnostics,
    IReadOnlyList<CompileDependency> Dependencies);

/// <summary>Everything a host shows before compiling.</summary>
public sealed record CheckResult(
    string InputPath,
    PowerShellCompilationArtifactKind? Kind,
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<ModeCheck> Modes,
    PowerShellCompilationMode? Recommended,
    IReadOnlyList<CompileDiagnostic> InputDiagnostics,
    TimeSpan Elapsed);

/// <summary>A target the engine can build for.</summary>
public sealed record CompileTarget(
    string Id,
    PowerShellCompilationMode Mode,
    string TargetFramework,
    string RuntimeIdentifier,
    PowerShellCompilationDeploymentModel Deployment,
    string SupportLevel,
    bool Advertised)
{
    /// <summary>Label for pickers.</summary>
    public string DisplayName => (IsWindowsPowerShell ? "Windows PowerShell 5.1" : $"{Platform}, {DeploymentLabel}") +
                                 (Advertised ? string.Empty : " (preview)");

    // .NET Framework targets run in Windows PowerShell 5.1; platform and deployment labels don't apply to them.
    private bool IsWindowsPowerShell => TargetFramework.StartsWith("net4", StringComparison.OrdinalIgnoreCase);

    private string Platform
    {
        get
        {
            // Portable profiles have no runtime identifier and run wherever their runtime does.
            if (string.IsNullOrWhiteSpace(RuntimeIdentifier)) return "Any platform";
            var parts = RuntimeIdentifier.Split('-', 2);
            var os = parts[0] switch
            {
                "win" => "Windows",
                "linux" => "Linux",
                "linux-musl" => "Linux (musl)",
                "osx" => "macOS",
                _ => parts[0]
            };
            var architecture = parts.Length > 1 ? parts[1] switch
            {
                "x64" => "x64",
                "arm64" => "Arm64",
                "x86" => "x86",
                "musl-x64" => "x64 (musl)",
                "musl-arm64" => "Arm64 (musl)",
                _ => parts[1]
            } : string.Empty;
            return string.IsNullOrEmpty(architecture) ? os : $"{os} {architecture}";
        }
    }

    private string DeploymentLabel => Deployment switch
    {
        PowerShellCompilationDeploymentModel.FrameworkDependent => "needs .NET installed",
        PowerShellCompilationDeploymentModel.SelfContained => "includes .NET",
        PowerShellCompilationDeploymentModel.Trimmed => "includes trimmed .NET",
        PowerShellCompilationDeploymentModel.ReadyToRun => "precompiled",
        PowerShellCompilationDeploymentModel.NativeAot => "native, no .NET needed",
        _ => Deployment.ToString()
    };
}

/// <summary>Which files besides the script go into the output.</summary>
public sealed record FileRules(
    PowerShellCompilationResourceMode Mode,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude)
{
    /// <summary>The engine default: the script and the files it loads.</summary>
    public static FileRules Default { get; } = new(PowerShellCompilationResourceMode.Declared, Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>One build target stored in a settings file.</summary>
public sealed record SavedTarget(
    string Name,
    PowerShellCompilationMode Mode,
    string TargetFramework,
    string RuntimeIdentifier,
    PowerShellCompilationDeploymentModel Deployment,
    string OutputDirectory);

/// <summary>Build settings read from or written to a <c>powerforge.psproject.json</c> file.</summary>
public sealed record BuildSettings(
    string SettingsPath,
    string InputPath,
    FileRules Files,
    IReadOnlyList<SavedTarget> Targets);

/// <summary>What to store when saving settings.</summary>
public sealed record SaveSettingsRequest(
    string SettingsPath,
    string InputPath,
    FileRules Files,
    PowerShellCompilationMode Mode,
    CompileTarget Target,
    string? OutputDirectory);

/// <summary>The outcome of opening or saving settings.</summary>
public sealed record SettingsResult(BuildSettings? Settings, CompileDiagnostic? Problem);

/// <summary>A code-signing certificate the user can sign with.</summary>
public sealed record SigningCertificate(string Thumbprint, string Subject, DateTime NotAfter)
{
    /// <summary>Label for pickers.</summary>
    public string DisplayName => $"{Subject}, expires {NotAfter:d MMM yyyy}";
}

/// <summary>What to build.</summary>
public sealed record CompileRequest(
    string InputPath,
    PowerShellCompilationMode Mode,
    CompileTarget? Target,
    string? OutputDirectory,
    string? SigningThumbprint = null,
    FileRules? Files = null);

/// <summary>The dependencies and prerequisites shown for a specific build selection.</summary>
public sealed record CompileReviewResult(
    PowerShellCompilationReview? Review,
    IReadOnlyList<CompileDependency> Dependencies,
    IReadOnlyList<CompileDiagnostic> Diagnostics);

/// <summary>What a build produced.</summary>
public sealed record CompileResult(
    bool Succeeded,
    string? ArtifactPath,
    long ArtifactSizeBytes,
    CompileOutcome Summary,
    int TypedUnits,
    int RuntimeUnits,
    IReadOnlyList<CompileDiagnostic> Diagnostics,
    TimeSpan Elapsed,
    bool ContainsReadableSource = false,
    bool Signed = false);

/// <summary>A progress line from the engine.</summary>
public sealed record CompileProgress(string Message, CompileSeverity Severity = CompileSeverity.Info);

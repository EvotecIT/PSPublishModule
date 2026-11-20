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
    int Column)
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
    string RuntimeIdentifier,
    PowerShellCompilationDeploymentModel Deployment,
    string SupportLevel,
    bool Advertised)
{
    /// <summary>Label for pickers.</summary>
    public string DisplayName => $"{Platform}, {DeploymentLabel}{(Advertised ? string.Empty : " (preview)")}";

    private string Platform
    {
        get
        {
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
    string? SigningThumbprint = null);

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

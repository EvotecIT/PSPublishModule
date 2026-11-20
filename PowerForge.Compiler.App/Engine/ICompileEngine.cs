namespace PowerForge.Compiler.App.Engine;

/// <summary>
/// The compiler operations available in the window.
/// </summary>
public interface ICompileEngine
{
    /// <summary>Checks the input in every mode without building or running it. Never throws for input problems.</summary>
    Task<CheckResult> CheckAsync(string inputPath, FileRules files, CancellationToken cancellationToken);

    /// <summary>Targets the engine can build for the given mode and input kind.</summary>
    IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind);

    /// <summary>Valid code-signing certificates in the current user's store; empty where signing is unsupported.</summary>
    IReadOnlyList<SigningCertificate> GetSigningCertificates();

    /// <summary>Checks the exact selection and proposes the dependency lock to show before compilation.</summary>
    Task<CompileReviewResult> ReviewAsync(CompileRequest request, CancellationToken cancellationToken);

    /// <summary>Builds one artifact from the confirmed dependency review.</summary>
    Task<CompileResult> CompileAsync(CompileRequest request, PowerShellCompilationReview review,
        IProgress<CompileProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Reads a <c>powerforge.psproject.json</c> settings file. Never throws for file problems.</summary>
    SettingsResult OpenSettings(string settingsPath);

    /// <summary>Writes the current selection into a settings file, keeping other saved targets. Never throws for file problems.</summary>
    SettingsResult SaveSettings(SaveSettingsRequest request);
}

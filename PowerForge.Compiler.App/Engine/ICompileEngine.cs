namespace PowerForge.Compiler.App.Engine;

/// <summary>
/// The only surface the window uses. It mirrors Check and Compile from
/// Docs/PowerForge.PowerShellCompilation.HostApi.md so the app can move to the engine's host service unchanged.
/// </summary>
public interface ICompileEngine
{
    /// <summary>Checks the input in every mode without building or running it. Never throws for input problems.</summary>
    Task<CheckResult> CheckAsync(string inputPath, CancellationToken cancellationToken);

    /// <summary>Targets the engine can build for the given mode and input kind.</summary>
    IReadOnlyList<CompileTarget> GetTargets(PowerShellCompilationMode mode, PowerShellCompilationArtifactKind kind);

    /// <summary>Valid code-signing certificates in the current user's store; empty where signing is unsupported.</summary>
    IReadOnlyList<SigningCertificate> GetSigningCertificates();

    /// <summary>Builds one artifact. Never throws for input or build problems; only cancellation throws.</summary>
    Task<CompileResult> CompileAsync(CompileRequest request, IProgress<CompileProgress>? progress, CancellationToken cancellationToken);
}

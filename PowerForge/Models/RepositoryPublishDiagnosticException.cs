namespace PowerForge;

/// <summary>A publish failure accompanied by a secret-free report from the publishing process.</summary>
public sealed class RepositoryPublishDiagnosticException : InvalidOperationException
{
    /// <summary>Diagnostic metadata from the attempted operation; contains no supplied secret values.</summary>
    public RepositoryDiagnosticReport Report { get; }

    /// <summary>Creates a publish failure without retaining a potentially secret-bearing inner exception.</summary>
    public RepositoryPublishDiagnosticException(string message, RepositoryDiagnosticReport report) : base(message)
        => Report = report ?? throw new ArgumentNullException(nameof(report));
}

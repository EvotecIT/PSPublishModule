using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Captures clean source state before an external benchmark writes into a fresh artifact directory.
/// </summary>
/// <example>
/// <summary>Reserve a BenchmarkDotNet artifact directory before measurement</summary>
/// <code>$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts</code>
/// </example>
[Cmdlet(VerbsLifecycle.Start, "BenchmarkProvenanceCapture")]
[OutputType(typeof(BenchmarkProvenanceCaptureSession))]
public sealed class StartBenchmarkProvenanceCaptureCommand : PSCmdlet
{
    /// <summary>Git repository root whose source is being measured.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string SourceRoot { get; set; } = string.Empty;

    /// <summary>Fresh directory where the external benchmark will write its artifacts.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ArtifactRoot { get; set; } = string.Empty;

    /// <summary>Starts the source-bound capture.</summary>
    protected override void ProcessRecord()
    {
        string sourceRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(SourceRoot);
        string artifactRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArtifactRoot);
        WriteObject(new BenchmarkArtifactProvenanceService().Start(sourceRoot, artifactRoot));
    }
}

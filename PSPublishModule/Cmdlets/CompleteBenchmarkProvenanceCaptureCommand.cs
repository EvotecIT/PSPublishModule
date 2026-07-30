using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Verifies unchanged source after an external benchmark and writes a hash-bound artifact sidecar.
/// </summary>
/// <example>
/// <summary>Complete a capture after the benchmark process exits</summary>
/// <code>$capture | Complete-BenchmarkProvenanceCapture</code>
/// </example>
[Cmdlet(VerbsLifecycle.Complete, "BenchmarkProvenanceCapture")]
[OutputType(typeof(string))]
public sealed class CompleteBenchmarkProvenanceCaptureCommand : PSCmdlet
{
    /// <summary>Capture session returned before measurement.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [ValidateNotNull]
    public BenchmarkProvenanceCaptureSession InputObject { get; set; } = null!;

    /// <summary>Completes the source-bound capture and emits the sidecar path.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new BenchmarkArtifactProvenanceService().Complete(InputObject));
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Captures clean source state before an external benchmark writes into a fresh artifact directory.
/// </summary>
/// <example>
/// <summary>Reserve a BenchmarkDotNet artifact directory before measurement</summary>
/// <code>$capture = Start-BenchmarkProvenanceCapture -SourceRoot . -ArtifactRoot .\Build\BenchmarkDotNet.Artifacts -Metadata @{ 'benchmark.workload.id' = 'tabular-65k-v1' } -RunMode full</code>
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

    /// <summary>Optional workload metadata to bind before measurement. Publishable evidence requires <c>benchmark.workload.id</c>.</summary>
    [Parameter]
    public Hashtable? Metadata { get; set; }

    /// <summary>Optional for diagnostic captures; publishable evidence requires a run mode bound before measurement.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? RunMode { get; set; }

    /// <summary>Starts the source-bound capture.</summary>
    protected override void ProcessRecord()
    {
        string sourceRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(SourceRoot);
        string artifactRoot = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ArtifactRoot);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Metadata is not null)
        {
            foreach (DictionaryEntry item in Metadata)
            {
                string key = LanguagePrimitives.ConvertTo<string>(item.Key);
                string value = LanguagePrimitives.ConvertTo<string>(item.Value);
                if (metadata.ContainsKey(key))
                    ThrowTerminatingError(new ErrorRecord(
                        new ArgumentException(
                            $"Benchmark provenance metadata contains duplicate key '{key}'.",
                            nameof(Metadata)),
                        "DuplicateBenchmarkProvenanceMetadata",
                        ErrorCategory.InvalidArgument,
                        Metadata));
                metadata.Add(key, value);
            }
        }
        WriteObject(new BenchmarkArtifactProvenanceService().Start(
            sourceRoot,
            artifactRoot,
            metadata,
            RunMode));
    }
}

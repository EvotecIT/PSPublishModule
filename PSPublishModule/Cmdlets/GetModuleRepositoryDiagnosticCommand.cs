using System;
using System.Management.Automation;
using System.Threading;
using PowerForge;

namespace PSPublishModule;

/// <summary>Inspects native repository registrations and authentication inputs without contacting the feed.</summary>
/// <remarks>Reports contain presence and origin metadata. Secret values are never exported or decrypted.</remarks>
/// <example>
/// <summary>Inspect a PSResourceGet repository</summary>
/// <code>Get-ModuleRepositoryDiagnostic -RepositoryName CompanyFeed -Tool PSResourceGet</code>
/// </example>
/// <example>
/// <summary>Export a secret-free report</summary>
/// <code>Get-ModuleRepositoryDiagnostic -RepositoryName CompanyFeed | ConvertTo-Json -Depth 10 | Set-Content .\repository-diagnostics.json</code>
/// </example>
[Cmdlet(VerbsCommon.Get, "ModuleRepositoryDiagnostic")]
[OutputType(typeof(RepositoryDiagnosticReport))]
public sealed class GetModuleRepositoryDiagnosticCommand : PSCmdlet
{
    private readonly CancellationTokenSource _cancellation = new();
    /// <summary>Name registered with the selected native backend.</summary>
    [Parameter(Position = 0)]
    [Alias("Name")]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = "PSGallery";

    /// <summary>Native backend to inspect. Inspection does not register repositories or acquire credentials.</summary>
    [Parameter]
    [ValidateSet("PSResourceGet", "PowerShellGet")]
    public PublishTool Tool { get; set; } = PublishTool.PSResourceGet;

    /// <summary>Optional expected endpoint for comparison with the registered repository.</summary>
    [Parameter]
    public string? RepositoryUri { get; set; }

    /// <summary>Collects one structured, secret-free repository report.</summary>
    protected override void ProcessRecord()
    {
        try { WriteObject(new RepositoryDiagnosticService().Inspect(new RepositoryDiagnosticRequest
        {
            RepositoryName = RepositoryName,
            Tool = Tool,
            RepositoryUri = RepositoryUri,
            WorkingDirectory = SessionState.Path.CurrentFileSystemLocation.Path
        }, _cancellation.Token)); }
        finally { _cancellation.Dispose(); }
    }

    /// <summary>Stops the owned inspection process when the pipeline is interrupted.</summary>
    protected override void StopProcessing()
    {
        try { _cancellation.Cancel(); } catch (ObjectDisposedException) { }
    }
}

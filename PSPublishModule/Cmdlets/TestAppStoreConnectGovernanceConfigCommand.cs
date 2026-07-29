using System.Management.Automation;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

/// <summary>Validates declarative App Store commercial and compliance state without contacting Apple.</summary>
[Cmdlet(VerbsDiagnostic.Test, "AppStoreConnectGovernanceConfig")]
[OutputType(typeof(bool), typeof(AppStoreConnectGovernanceFinding))]
public sealed class TestAppStoreConnectGovernanceConfigCommand : PSCmdlet
{
    /// <summary>Path to the governance JSON configuration.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Returns structured findings instead of a Boolean.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Validates the declaration.</summary>
    protected override void ProcessRecord()
    {
        var path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ConfigPath);
        var configuration = new AppStoreConnectGovernanceConfiguration();
        var findings = configuration.Validate(configuration.Load(path));
        if (PassThru.IsPresent) WriteObject(findings, enumerateCollection: true);
        else WriteObject(findings.All(finding => !finding.IsError));
    }
}

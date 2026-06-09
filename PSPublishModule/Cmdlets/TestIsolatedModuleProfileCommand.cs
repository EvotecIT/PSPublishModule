using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Validates a curated isolated module profile without importing it.
/// </summary>
/// <remarks>
/// <para>
/// Test-IsolatedModuleProfile resolves the selected profile and module source, validates the
/// profile's minimum version, checks the expected script, manifest, binary, dependency, and
/// support files, and returns a detailed validation result without copying or importing the
/// generated wrapper.
/// </para>
/// </remarks>
/// <example>
/// <summary>Validate the newest visible ExchangeOnlineManagement profile source</summary>
/// <code>Test-IsolatedModuleProfile -Profile ExchangeOnlineManagement</code>
/// <para>
/// Resolves ExchangeOnlineManagement from PSModulePath and returns validation details.
/// </para>
/// </example>
/// <example>
/// <summary>Validate a specific Graph Authentication manifest</summary>
/// <code>Test-IsolatedModuleProfile -Profile MicrosoftGraphAuthentication -Path 'C:\Modules\Microsoft.Graph.Authentication\2.37.0\Microsoft.Graph.Authentication.psd1' | Format-List *</code>
/// <para>
/// Uses the supplied manifest path as the source manifest and its parent as the module base.
/// </para>
/// </example>
/// <example>
/// <summary>Return only a Boolean result</summary>
/// <code>Test-IsolatedModuleProfile -Profile MicrosoftTeams -Quiet</code>
/// <para>
/// Returns True when the resolved profile source passes validation; otherwise returns False.
/// </para>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "IsolatedModuleProfile")]
[OutputType(typeof(IsolatedModuleProfileValidationResult))]
[OutputType(typeof(bool))]
public sealed class TestIsolatedModuleProfileCommand : PSCmdlet
{
    /// <summary>Name of the built-in isolation profile to validate.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Profile { get; set; } = string.Empty;

    /// <summary>Optional module name override. When omitted, the profile's module name is used.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Optional module base path or manifest path. When omitted, the profile module is resolved from PSModulePath.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Return only a Boolean validation result.</summary>
    [Parameter]
    public SwitchParameter Quiet { get; set; }

    /// <summary>Validates the isolated module profile.</summary>
    protected override void ProcessRecord()
    {
        var request = new IsolatedModuleImportRequest
        {
            ProfileName = Profile,
            ModuleName = Name,
            Path = ResolveOptionalPath(Path)
        };

        var result = new IsolatedModuleImportService().Validate(request);
        WriteObject(Quiet ? result.IsValid : result);
    }

    private string? ResolveOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : SessionState.Path.GetUnresolvedProviderPathFromPSPath(path);
}

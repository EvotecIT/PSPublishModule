// ReSharper disable All
using System;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// <para type="synopsis">Configures repository access for documentation (stores/revokes tokens).</para>
/// <para type="description">Stores Personal Access Tokens for GitHub and/or Azure DevOps under the current user profile so that PSMaintenance can access private repositories when rendering documentation from a repository. On Windows, tokens are protected with DPAPI; on other platforms they are stored as Base64 (best effort).</para>
/// <example>
///   <code>Set-ModuleDocumentation -GitHubToken 'ghp_xxx'</code>
/// </example>
/// <example>
///   <code>Set-ModuleDocumentation -AzureDevOpsPat 'azdopat_xxx'</code>
/// </example>
/// <example>
///   <code>Set-ModuleDocumentation -FromEnvironment</code>
/// </example>
/// <example>
///   <code>Set-ModuleDocumentation -Clear</code>
/// </example>
/// </summary>
/// <example>
///   <code>$env:PG_GITHUB_TOKEN='ghp_xxx'; $env:PG_AZDO_PAT='azdopat_xxx'; Set-ModuleDocumentation -FromEnvironment</code>
/// </example>
[Cmdlet(VerbsCommon.Set, "ModuleDocumentation", SupportsShouldProcess = true)]
[Alias("Set-Documentation")]
public sealed class SetModuleDocumentationCommand : PSCmdlet
{
    /// <summary>GitHub token (scope: repo for private repositories).</summary>
    [Parameter] public string? GitHubToken { get; set; }
    /// <summary>Azure DevOps Personal Access Token (scope: Code (Read)).</summary>
    [Parameter] public string? AzureDevOpsPat { get; set; }
    /// <summary>Read tokens from environment variables (PG_GITHUB_TOKEN/GITHUB_TOKEN and PG_AZDO_PAT/AZURE_DEVOPS_EXT_PAT).</summary>
    [Parameter] public SwitchParameter FromEnvironment { get; set; }
    /// <summary>Remove any stored tokens.</summary>
    [Parameter] public SwitchParameter Clear { get; set; }

    /// <summary>
    /// Saves or clears stored tokens used by PSMaintenance to access private repositories when fetching documentation.
    /// </summary>
    protected override void ProcessRecord()
    {
        if (Clear)
        {
            if (ShouldProcess("PSMaintenance token store", "Clear"))
            {
                TokenStore.Clear();
                WriteVerbose("Stored tokens cleared.");
            }
            return;
        }

        string? gh = GitHubToken;
        string? az = AzureDevOpsPat;
        if (FromEnvironment)
        {
            gh = gh ?? (Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN"));
            az = az ?? (Environment.GetEnvironmentVariable("PG_AZDO_PAT") ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT"));
        }
        if (string.IsNullOrEmpty(gh) && string.IsNullOrEmpty(az))
        {
            ThrowTerminatingError(new ErrorRecord(new ArgumentException("Provide -GitHubToken and/or -AzureDevOpsPat, or use -FromEnvironment."), "NoTokensProvided", ErrorCategory.InvalidArgument, this));
            return;
        }

        if (ShouldProcess("PSMaintenance token store", "Save"))
        {
            TokenStore.Save(gh, az);
            if (!string.IsNullOrEmpty(gh)) WriteVerbose("GitHub token stored.");
            if (!string.IsNullOrEmpty(az)) WriteVerbose("Azure DevOps PAT stored.");
        }
    }
}

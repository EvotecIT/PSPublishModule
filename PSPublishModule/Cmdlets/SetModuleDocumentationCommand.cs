// ReSharper disable All
using System;
using PowerForge;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// <para type="synopsis">Configures repository access for documentation (stores/revokes tokens).</para>
/// <para type="description">Stores Personal Access Tokens for GitHub and/or Azure DevOps under the current user profile so module documentation commands can access private repositories. On Windows, tokens are protected with DPAPI; on other platforms they are stored as Base64 (best effort).</para>
/// <example>
///   <summary>Store a GitHub token for private repositories</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Set-ModuleDocumentation -GitHubToken 'ghp_xxx'</code>
///   <para>Saves the token under the current user profile (DPAPI on Windows) so -Online can access private GitHub repos.</para>
/// </example>
/// <example>
///   <summary>Store an Azure DevOps PAT</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Set-ModuleDocumentation -AzureDevOpsPat 'azdopat_xxx'</code>
///   <para>Persists a PAT with Code (Read) scope for accessing private Azure DevOps repositories.</para>
/// </example>
/// <example>
///   <summary>Read tokens from environment variables (CI-friendly)</summary>
///   <prefix>PS&gt; </prefix>
///   <code>$env:PG_GITHUB_TOKEN='ghp_xxx'; $env:PG_AZDO_PAT='azdopat_xxx'; Set-ModuleDocumentation -FromEnvironment</code>
///   <para>Loads tokens from environment and stores them in the local token store for subsequent runs.</para>
/// </example>
/// <example>
///   <summary>Clear any stored tokens</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Set-ModuleDocumentation -Clear</code>
///   <para>Removes persisted tokens from the local token store.</para>
/// </example>
/// </summary>
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
    /// Saves or clears stored tokens used to access private repositories when fetching documentation.
    /// </summary>
    protected override void ProcessRecord()
    {
        if (Clear)
        {
            if (ShouldProcess("module documentation token store", "Clear"))
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

        if (ShouldProcess("module documentation token store", "Save"))
        {
            TokenStore.Save(gh, az);
            if (!string.IsNullOrEmpty(gh)) WriteVerbose("GitHub token stored.");
            if (!string.IsNullOrEmpty(az)) WriteVerbose("Azure DevOps PAT stored.");
        }
    }
}

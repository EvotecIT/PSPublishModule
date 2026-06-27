using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Finds module versions from a managed module repository.
/// </summary>
/// <remarks>
/// <para>
/// This command queries NuGet v3 or local-folder repositories through the managed C# repository client.
/// </para>
/// </remarks>
/// <example>
/// <summary>Find the latest stable version of a module</summary>
/// <code>Find-ManagedModule -Name Company.Tools</code>
/// </example>
/// <example>
/// <summary>Find modules using a wildcard package id</summary>
/// <code>Find-ManagedModule -Name Company.* -Repository C:\Packages</code>
/// </example>
/// <example>
/// <summary>Find all versions from a local folder feed</summary>
/// <code>Find-ManagedModule -Name Company.Tools -Repository C:\Packages -AllVersions -AllowPrerelease</code>
/// </example>
[Cmdlet(VerbsCommon.Find, "ManagedModule")]
[OutputType(typeof(ManagedModuleVersionInfo))]
public sealed class FindManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to find.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Return all matching versions instead of only the latest selected version.</summary>
    [Parameter]
    public SwitchParameter AllVersions { get; set; }

    /// <summary>Maximum search results returned for wildcard name queries.</summary>
    [Parameter]
    [ValidateRange(1, 1000)]
    public int First { get; set; } = 100;

    /// <summary>Include prerelease versions.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Optional repository credential.</summary>
    [Parameter]
    public PSCredential? Credential { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Finds matching module versions.</summary>
    protected override void ProcessRecord()
    {
        var repository = ManagedModuleCommandSupport.CreateRepository(this, RepositoryName, Repository);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var client = new ManagedModuleRepositoryClient(logger);

        foreach (var moduleName in Name)
        {
            var output = ManagedModuleCommandSupport.HasWildcard(moduleName)
                ? client.SearchPackagesAsync(repository, moduleName, Prerelease.IsPresent, credential, First)
                    .GetAwaiter()
                    .GetResult()
                : SelectVersions(client.GetVersionsAsync(repository, moduleName, Prerelease.IsPresent, credential)
                    .GetAwaiter()
                    .GetResult());

            foreach (var version in output)
                WriteObject(version);
        }
    }

    private IReadOnlyList<ManagedModuleVersionInfo> SelectVersions(IReadOnlyList<ManagedModuleVersionInfo> versions)
    {
        if (AllVersions.IsPresent || versions.Count == 0)
            return versions;

        return new[] { versions[versions.Count - 1] };
    }
}

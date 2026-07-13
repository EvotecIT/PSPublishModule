using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Refreshes package metadata in a local managed module catalog.
/// </summary>
/// <example>
/// <summary>Refresh selected PowerShell Gallery packages</summary>
/// <code>Update-ManagedModuleCatalog -Name PSGallery -PackageName Pester, Microsoft.Graph.Authentication</code>
/// <para>Queries live metadata and stores the known versions, dependency metadata, hashes, sizes, and package URLs locally.</para>
/// </example>
[Cmdlet(VerbsData.Update, "ManagedModuleCatalog", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleCatalogUpdateResult))]
public sealed class UpdateManagedModuleCatalogCommand : AsyncPSCmdlet
{
    /// <summary>Catalog name. Defaults to PSGallery.</summary>
    [Parameter(Position = 0)]
    [Alias("Repository", "RepositoryName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = "PSGallery";

    /// <summary>Package/module names to refresh. When omitted, existing catalog packages are refreshed.</summary>
    [Parameter(Position = 1, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("Package", "Module", "ModuleName")]
    public string[]? PackageName { get; set; }

    /// <summary>Override the catalog's prerelease refresh setting for this run.</summary>
    [Parameter]
    public bool? IncludePrerelease { get; set; }

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

    /// <summary>Catalog storage scope.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    private readonly List<string> _packageNames = new();

    /// <summary>Collects package names from the pipeline.</summary>
    protected override Task ProcessRecordAsync()
    {
        if (PackageName is null)
            return Task.CompletedTask;

        _packageNames.AddRange(PackageName.Where(static name => !string.IsNullOrWhiteSpace(name)));
        return Task.CompletedTask;
    }

    /// <summary>Refreshes the catalog.</summary>
    protected override async Task EndProcessingAsync()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Update-ManagedModuleCatalog requires User or Machine scope.", nameof(Scope));

        var store = ManagedModuleCatalogCommandSupport.CreateStore(Scope);
        if (!ShouldProcess(store.Path, $"Update managed module catalog '{Name}'"))
            return;

        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var result = await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = Name,
            PackageNames = _packageNames.ToArray(),
            IncludePrerelease = IncludePrerelease,
            Credential = credential
        }, CancelToken);
        WriteObject(result);
    }
}

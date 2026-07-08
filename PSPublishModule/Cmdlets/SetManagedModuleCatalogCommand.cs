using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates or updates local managed module catalog cache settings.
/// </summary>
/// <remarks>
/// <para>
/// The catalog stores local repository metadata only. It does not mirror package blobs unless a later package cache
/// feature is explicitly enabled. Managed module commands can use this metadata as an opt-in fallback when live
/// repository metadata is unavailable.
/// </para>
/// </remarks>
/// <example>
/// <summary>Enable a fallback catalog for PowerShell Gallery metadata</summary>
/// <code>Set-ManagedModuleCatalog -Name PSGallery -Mode Fallback -MaxStaleness 14.00:00:00</code>
/// <para>Stores user-local catalog settings for the canonical PowerShell Gallery source.</para>
/// </example>
[Cmdlet(VerbsCommon.Set, "ManagedModuleCatalog", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleCatalog))]
public sealed class SetManagedModuleCatalogCommand : PSCmdlet
{
    /// <summary>Catalog name, usually the repository name. Defaults to PSGallery.</summary>
    [Parameter(Position = 0)]
    [Alias("Repository", "RepositoryName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = "PSGallery";

    /// <summary>Repository source URL used to refresh metadata.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Source { get; set; } = ManagedModuleCatalogDefaults.PowerShellGalleryV3;

    /// <summary>Repository kind used by the catalog refresh path.</summary>
    [Parameter]
    public ManagedModuleRepositoryKind RepositoryKind { get; set; } = ManagedModuleRepositoryKind.NuGetV3;

    /// <summary>Local catalog cache mode.</summary>
    [Parameter]
    public ManagedModuleCatalogCacheMode Mode { get; set; } = ManagedModuleCatalogCacheMode.Fallback;

    /// <summary>Maximum age accepted for stale catalog fallback decisions.</summary>
    [Parameter]
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Include prerelease versions during catalog refresh.</summary>
    [Parameter]
    public bool IncludePrerelease { get; set; } = true;

    /// <summary>Catalog storage scope.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Saves catalog settings.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Set-ManagedModuleCatalog requires User or Machine scope.", nameof(Scope));

        var store = ManagedModuleCatalogCommandSupport.CreateStore(Scope);
        if (!ShouldProcess(store.Path, $"Set managed module catalog '{Name}'"))
            return;

        var existing = store.GetCatalog(Name);
        var bound = MyInvocation.BoundParameters;
        var source = bound.ContainsKey(nameof(Source))
            ? Source
            : existing?.Source ?? Source;
        var repositoryKind = bound.ContainsKey(nameof(RepositoryKind))
            ? RepositoryKind
            : bound.ContainsKey(nameof(Source))
                ? new ManagedModuleRepository(Name, source, ManagedModuleRepositoryKind.Auto).Kind
                : existing?.RepositoryKind ?? new ManagedModuleRepository(Name, source, ManagedModuleRepositoryKind.Auto).Kind;
        var mode = bound.ContainsKey(nameof(Mode))
            ? Mode
            : existing?.Mode ?? Mode;
        var maxStaleness = bound.ContainsKey(nameof(MaxStaleness))
            ? MaxStaleness
            : existing?.MaxStaleness ?? MaxStaleness;
        var includePrerelease = bound.ContainsKey(nameof(IncludePrerelease))
            ? IncludePrerelease
            : existing?.IncludePrerelease ?? IncludePrerelease;

        var catalog = store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = Name,
            Source = source,
            RepositoryKind = repositoryKind,
            Mode = mode,
            MaxStaleness = maxStaleness,
            IncludePrerelease = includePrerelease
        });
        WriteObject(catalog);
    }
}

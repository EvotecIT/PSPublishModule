using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets local managed module catalog settings or package metadata.
/// </summary>
/// <example>
/// <summary>Show configured catalogs</summary>
/// <code>Get-ManagedModuleCatalog</code>
/// <para>Returns catalog settings and cached package counts.</para>
/// </example>
/// <example>
/// <summary>Show cached package metadata</summary>
/// <code>Get-ManagedModuleCatalog -Name PSGallery -PackageName Pester</code>
/// <para>Returns the cached package entry for Pester from the PSGallery catalog.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ManagedModuleCatalog")]
[OutputType(typeof(ManagedModuleCatalog))]
[OutputType(typeof(ManagedModuleCatalogPackage))]
public sealed class GetManagedModuleCatalogCommand : PSCmdlet
{
    /// <summary>Optional catalog names. When omitted, all visible catalogs are returned.</summary>
    [Parameter(Position = 0)]
    [Alias("Repository", "RepositoryName")]
    public string[]? Name { get; set; }

    /// <summary>Optional package names to return from the selected catalogs.</summary>
    [Parameter]
    [Alias("Package", "Module", "ModuleName")]
    public string[]? PackageName { get; set; }

    /// <summary>Catalog storage scope. The default reads user catalogs before machine-wide catalogs.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.All;

    /// <summary>Gets catalog settings or package metadata.</summary>
    protected override void ProcessRecord()
    {
        var catalogs = ManagedModuleCatalogCommandSupport.CreateStores(Scope)
            .SelectMany(static store => store.GetCatalogs())
            .GroupBy(static catalog => catalog.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static catalog => catalog.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (Name is { Length: > 0 })
        {
            var names = Name
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            catalogs = catalogs
                .Where(catalog => names.Contains(catalog.Name))
                .ToArray();
        }

        if (PackageName is null || PackageName.Length == 0)
        {
            WriteObject(catalogs, enumerateCollection: true);
            return;
        }

        var packageNames = PackageName
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packages = catalogs
            .SelectMany(static catalog => catalog.Packages)
            .Where(package => packageNames.Contains(package.Id))
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        WriteObject(packages, enumerateCollection: true);
    }
}

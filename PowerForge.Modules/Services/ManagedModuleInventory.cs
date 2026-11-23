namespace PowerForge;

/// <summary>Lists modules available in a filesystem module root without loading PowerShell.</summary>
public static class ManagedModuleInventory
{
    /// <summary>Lists matching modules with their exact version and installed path.</summary>
    /// <param name="moduleRoot">Module search directory.</param>
    /// <param name="name">Optional module name or wildcard.</param>
    /// <param name="version">Optional exact version or version range.</param>
    /// <returns>The modules found under the supplied root.</returns>
    public static ManagedModuleInstalledInfo[] List(string moduleRoot, string? name = null, string? version = null)
    {
        var root = Path.GetFullPath(moduleRoot);
        var inventory = new ModuleStateInventoryService().Collect(new ModuleStateInventoryRequest(
            [new ModuleStateModulePath(root)], name is null ? null : [name], version));
        if (inventory.Diagnostics.Length != 0)
            throw new IOException(inventory.Diagnostics[0].Message);

        return inventory.InstalledModules.Select(static item => new ManagedModuleInstalledInfo(
            item.Name, item.Version, item.Path, item.SourceRepository, item.IsEffectiveImportCandidate)).ToArray();
    }
}

/// <summary>An installed module found under the requested root.</summary>
public sealed class ManagedModuleInstalledInfo
{
    internal ManagedModuleInstalledInfo(string name, string version, string? path, string? repository, bool effective)
    {
        Name = name;
        Version = version;
        Path = path;
        Repository = repository;
        Effective = effective;
    }

    /// <summary>Manifest module name.</summary>
    public string Name { get; }

    /// <summary>Installed version.</summary>
    public string Version { get; }

    /// <summary>Installed directory.</summary>
    public string? Path { get; }

    /// <summary>Repository recorded in optional package metadata.</summary>
    public string? Repository { get; }

    /// <summary>Whether this is the preferred version within the scanned root.</summary>
    public bool Effective { get; }
}

namespace PowerForge;

internal sealed class InstalledModuleMetadata
{
    public string Name { get; }
    public string? Version { get; }
    public string? Guid { get; }
    public string? ModuleBasePath { get; }

    public InstalledModuleMetadata(string name, string? version, string? guid, string? moduleBasePath)
    {
        Name = name;
        Version = version;
        Guid = guid;
        ModuleBasePath = moduleBasePath;
    }
}

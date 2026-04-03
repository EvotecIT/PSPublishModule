namespace PowerForge;

internal sealed class ImportModuleEntry
{
    public string Name { get; set; } = string.Empty;
    public string? MinimumVersion { get; set; }
    public string? RequiredVersion { get; set; }
}

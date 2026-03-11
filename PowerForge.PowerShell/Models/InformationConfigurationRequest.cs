namespace PowerForge;

internal sealed class InformationConfigurationRequest
{
    public string? FunctionsToExportFolder { get; set; }
    public string? AliasesToExportFolder { get; set; }
    public string[]? ExcludeFromPackage { get; set; }
    public string[]? IncludeRoot { get; set; }
    public string[]? IncludePS1 { get; set; }
    public string[]? IncludeAll { get; set; }
    public string? IncludeCustomCode { get; set; }
    public IncludeToArrayEntry[]? IncludeToArray { get; set; }
    public string? LibrariesCore { get; set; }
    public string? LibrariesDefault { get; set; }
    public string? LibrariesStandard { get; set; }
}

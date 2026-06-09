namespace PowerForge;

internal sealed class ModuleTestSuitePreparationRequest
{
    public string CurrentPath { get; set; } = string.Empty;
    public string? ProjectPath { get; set; }
    public string[]? AdditionalModules { get; set; }
    public string[]? SkipModules { get; set; }
    public string? TestPath { get; set; }
    public ModuleTestSuiteOutputFormat OutputFormat { get; set; } = ModuleTestSuiteOutputFormat.Detailed;
    public int TimeoutSeconds { get; set; } = 600;
    public bool EnableCodeCoverage { get; set; }
    public bool Force { get; set; }
    public bool ExitOnFailure { get; set; }
    public bool SkipDependencies { get; set; }
    public bool SkipImport { get; set; }
    public bool PassThru { get; set; }
    public bool CICD { get; set; }
}

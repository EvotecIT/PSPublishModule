namespace PowerForge;

internal sealed class PowerShellBenchmarkChildRunnerRequest
{
    public string SpecPath { get; set; } = string.Empty;
    public int SuiteIndex { get; set; }
    public string ResultPath { get; set; } = string.Empty;
    public string PowerForgeAssemblyPath { get; set; } = string.Empty;
    public string PowerForgePowerShellAssemblyPath { get; set; } = string.Empty;
    public string ReadmePathFile { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string OutputRoot { get; set; } = string.Empty;
    public int WarmupCount { get; set; }
    public int IterationCount { get; set; }
    public string RunMode { get; set; } = string.Empty;
    public string RunOrder { get; set; } = string.Empty;
    public int CooldownMilliseconds { get; set; }
    public string OutlierMode { get; set; } = string.Empty;
    public string SuiteName { get; set; } = string.Empty;
    public string PlanningProfile { get; set; } = PowerShellBenchmarkProfileKind.Current.ToString();
    public Dictionary<string, string?> BenchmarkVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PowerShellBenchmarkSelection Selection { get; set; } = new();
    public string[] ModulePaths { get; set; } = Array.Empty<string>();
    public string RunStartedUtc { get; set; } = string.Empty;
    public bool UpdateReadmeBlocks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the child process should enforce comparison gates. Multi-host children defer
    /// enforcement until the parent has merged every host result so a gate failure remains a comparison
    /// failure instead of being converted into an external-host execution failure.
    /// </summary>
    public bool ValidateComparisonGates { get; set; } = true;
}

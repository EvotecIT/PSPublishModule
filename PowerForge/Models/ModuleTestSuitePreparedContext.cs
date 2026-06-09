namespace PowerForge;

internal sealed class ModuleTestSuitePreparedContext
{
    public string ProjectRoot { get; set; } = string.Empty;
    public bool PassThru { get; set; }
    public bool ExitOnFailure { get; set; }
    public ModuleTestSuiteSpec Spec { get; set; } = new();
}

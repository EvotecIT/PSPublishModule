namespace PowerForge;

internal sealed class ModuleTestSuiteWorkflowResult
{
    public ModuleTestSuiteResult Result { get; set; } = null!;
    public string? FailureMessage { get; set; }
    public string[] CiOutputLines { get; set; } = System.Array.Empty<string>();
}

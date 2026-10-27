namespace PowerForge;

internal sealed class ModuleTestSuiteWorkflowResult
{
    public bool Success => Result.FailedCount == 0 && Result.ExitCode == 0;
    public ModuleTestSuiteResult Result { get; set; } = null!;
    public string? FailureMessage { get; set; }
    public string[] CiOutputLines { get; set; } = System.Array.Empty<string>();
}

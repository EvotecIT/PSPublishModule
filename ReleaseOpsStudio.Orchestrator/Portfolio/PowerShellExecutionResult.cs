namespace ReleaseOpsStudio.Orchestrator.Portfolio;

public sealed record PowerShellExecutionResult(
    int ExitCode,
    TimeSpan Duration,
    string StandardOutput,
    string StandardError);

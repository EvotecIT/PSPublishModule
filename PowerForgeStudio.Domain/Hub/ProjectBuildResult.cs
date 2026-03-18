namespace PowerForgeStudio.Domain.Hub;

public sealed record ProjectBuildResult(
    string ProjectName,
    BuildScriptKind ScriptKind,
    string ScriptPath,
    bool Succeeded,
    string Output,
    string? Error,
    double DurationSeconds,
    DateTimeOffset CompletedAtUtc)
{
    public string StatusDisplay => Succeeded ? "Success" : "Failed";

    public string DurationDisplay => DurationSeconds < 60
        ? $"{DurationSeconds:F1}s"
        : $"{DurationSeconds / 60:F1}m";
}

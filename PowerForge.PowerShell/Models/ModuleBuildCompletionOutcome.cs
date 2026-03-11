namespace PowerForge;

internal sealed class ModuleBuildCompletionOutcome
{
    public bool Succeeded { get; set; }
    public bool ShouldSetExitCode { get; set; }
    public int ExitCode { get; set; }
    public bool ShouldEmitErrorRecord { get; set; }
    public string ErrorRecordId { get; set; } = string.Empty;
    public bool ShouldReplayBufferedLogs { get; set; }
    public bool ShouldWriteInteractiveFailureSummary { get; set; }
    public string CompletionMessage { get; set; } = string.Empty;
}

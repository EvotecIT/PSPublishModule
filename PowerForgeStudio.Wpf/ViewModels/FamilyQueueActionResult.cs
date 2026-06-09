using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record FamilyQueueActionResult(
    string StatusMessage,
    ReleaseQueueCommandResult? CommandResult = null);

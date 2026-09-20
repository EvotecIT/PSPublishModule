using PowerForgeStudio.Domain.Automation;

namespace PowerForgeStudio.Orchestrator.Automation;

internal sealed record AutomationSourceResult(
    IReadOnlyList<WorkspaceAutomationEntry> Entries,
    WorkspaceAutomationSourceState State);

using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfileTemplate(
    string TemplateId,
    string DisplayName,
    string Summary,
    string Description,
    string TodayNote,
    IReadOnlyList<WorkspaceProfileLaunchActionKind>? PreferredActionKinds = null,
    RepositoryPortfolioFocusMode? PreferredStartupFocusMode = null,
    string? PreferredStartupSearchText = null,
    string? PreferredStartupFamily = null,
    bool ApplyStartupPreferenceAfterSavedView = false,
    bool PreferCurrentFamilyForQueueScope = false,
    bool IsBuiltIn = false)
{
    public string ActionChainDisplay => WorkspaceProfileActionChainFormatting.Format(PreferredActionKinds);

    public string StartupViewDisplay => WorkspaceProfileStartupPreferenceFormatting.Format(
        PreferredStartupFocusMode,
        PreferredStartupSearchText,
        PreferredStartupFamily);

    public string QueueScopeBehaviorDisplay => PreferCurrentFamilyForQueueScope
        ? "Queue scope: use the current family selection"
        : "Queue scope: keep the current draft selection";

    public string SourceLabel => IsBuiltIn ? "Built-in template" : "Custom template";
}

public static class WorkspaceProfileTemplateCatalog
{
    public static IReadOnlyList<WorkspaceProfileTemplate> CreateDefaultTemplates()
        => [
            new WorkspaceProfileTemplate(
                TemplateId: "daily-modules",
                DisplayName: "Daily Modules",
                Summary: "Ready-first desk for the current module family.",
                Description: "Daily module release desk",
                TodayNote: "Refresh the workspace, prepare the current family queue, and verify receipts before lunch.",
                PreferredActionKinds: [
                    WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                    WorkspaceProfileLaunchActionKind.PrepareQueue,
                    WorkspaceProfileLaunchActionKind.RetryFailedFamily
                ],
                PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Ready,
                PreferCurrentFamilyForQueueScope: true,
                IsBuiltIn: true),
            new WorkspaceProfileTemplate(
                TemplateId: "attention-triage",
                DisplayName: "Attention Triage",
                Summary: "Attention-first desk for git, release, and queue remediation.",
                Description: "Attention-first triage desk",
                TodayNote: "Open the attention lane, clear git or release blockers, and retry the affected family once the desk is stable.",
                PreferredActionKinds: [
                    WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                    WorkspaceProfileLaunchActionKind.OpenAttentionView,
                    WorkspaceProfileLaunchActionKind.RetryFailedFamily
                ],
                PreferredStartupFocusMode: RepositoryPortfolioFocusMode.Attention,
                ApplyStartupPreferenceAfterSavedView: true,
                IsBuiltIn: true),
            new WorkspaceProfileTemplate(
                TemplateId: "publish-desk",
                DisplayName: "Publish Desk",
                Summary: "Queue-active desk for publish and verification follow-through.",
                Description: "Publish and verification desk",
                TodayNote: "Restore the current family, prepare the queue, and validate publish plus verification receipts before closing the desk.",
                PreferredActionKinds: [
                    WorkspaceProfileLaunchActionKind.RefreshWorkspace,
                    WorkspaceProfileLaunchActionKind.PrepareQueue
                ],
                PreferredStartupFocusMode: RepositoryPortfolioFocusMode.QueueActive,
                ApplyStartupPreferenceAfterSavedView: true,
                PreferCurrentFamilyForQueueScope: true,
                IsBuiltIn: true)
        ];
}

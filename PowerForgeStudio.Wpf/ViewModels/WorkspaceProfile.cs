using System.Linq;
using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record WorkspaceProfile(
    string ProfileId,
    string DisplayName,
    string? Description,
    string? TodayNote,
    WorkspaceProfileLaunchResult? LastLaunchResult,
    IReadOnlyList<WorkspaceProfileLaunchResult> LaunchHistory,
    string WorkspaceRoot,
    string? SavedViewId,
    string? QueueScopeKey,
    string? QueueScopeDisplayName,
    IReadOnlyList<WorkspaceProfileLaunchActionKind>? PreferredActionKinds = null,
    RepositoryPortfolioFocusMode? PreferredStartupFocusMode = null,
    string? PreferredStartupSearchText = null,
    string? PreferredStartupFamilyKey = null,
    string? PreferredStartupFamilyDisplayName = null,
    bool ApplyStartupPreferenceAfterSavedView = false,
    DateTimeOffset UpdatedAtUtc = default)
{
    public string PreferredActionChainDisplay => WorkspaceProfileActionChainFormatting.Format(PreferredActionKinds);

    public string PreferredStartupViewDisplay => WorkspaceProfileStartupPreferenceFormatting.Format(
        PreferredStartupFocusMode,
        PreferredStartupSearchText,
        PreferredStartupFamilyDisplayName ?? PreferredStartupFamilyKey);

    public string StartupBehaviorDisplay => WorkspaceProfileStartupPreferenceFormatting.FormatBehavior(
        hasSavedView: !string.IsNullOrWhiteSpace(SavedViewId),
        applyAfterSavedView: ApplyStartupPreferenceAfterSavedView,
        PreferredStartupFocusMode,
        PreferredStartupSearchText,
        PreferredStartupFamilyDisplayName ?? PreferredStartupFamilyKey);
}

public static class WorkspaceProfileActionChainFormatting
{
    public static string Format(IReadOnlyList<WorkspaceProfileLaunchActionKind>? actionKinds)
    {
        if (actionKinds is null || actionKinds.Count == 0)
        {
            return "Sequence: automatic";
        }

        var labels = actionKinds
            .Distinct()
            .Select(FormatKind);
        return $"Sequence: {string.Join(" -> ", labels)}";
    }

    public static string FormatKind(WorkspaceProfileLaunchActionKind actionKind)
        => actionKind switch
        {
            WorkspaceProfileLaunchActionKind.RefreshWorkspace => "Refresh Workspace",
            WorkspaceProfileLaunchActionKind.ApplySavedView => "Apply Saved View",
            WorkspaceProfileLaunchActionKind.PrepareQueue => "Prepare Queue",
            WorkspaceProfileLaunchActionKind.OpenAttentionView => "Open Attention View",
            WorkspaceProfileLaunchActionKind.RetryFailedFamily => "Retry Failed Family",
            _ => actionKind.ToString()
        };
}

public static class WorkspaceProfileStartupPreferenceFormatting
{
    public static bool HasPreference(
        RepositoryPortfolioFocusMode? focusMode,
        string? searchText,
        string? familyDisplay)
        => focusMode is not null
           || !string.IsNullOrWhiteSpace(searchText)
           || !string.IsNullOrWhiteSpace(familyDisplay);

    public static string Format(
        RepositoryPortfolioFocusMode? focusMode,
        string? searchText,
        string? familyDisplay)
    {
        if (!HasPreference(focusMode, searchText, familyDisplay))
        {
            return "Startup view: automatic";
        }

        return $"Startup view: {FormatDetails(focusMode, searchText, familyDisplay)}";
    }

    public static string FormatBehavior(
        bool hasSavedView,
        bool applyAfterSavedView,
        RepositoryPortfolioFocusMode? focusMode,
        string? searchText,
        string? familyDisplay)
    {
        var hasPreference = HasPreference(focusMode, searchText, familyDisplay);
        if (hasSavedView)
        {
            if (!hasPreference)
            {
                return "Startup strategy: saved view pinned";
            }

            var details = FormatDetails(focusMode, searchText, familyDisplay);
            return applyAfterSavedView
                ? $"Startup strategy: saved view + startup emphasis ({details})"
                : $"Startup strategy: saved view pinned, startup fallback ({details})";
        }

        if (!hasPreference)
        {
            return "Startup strategy: automatic";
        }

        return $"Startup strategy: startup emphasis ({FormatDetails(focusMode, searchText, familyDisplay)})";
    }

    public static string FormatDetails(
        RepositoryPortfolioFocusMode? focusMode,
        string? searchText,
        string? familyDisplay)
    {
        var parts = new List<string>();
        if (focusMode is not null)
        {
            parts.Add($"focus {FormatFocusMode(focusMode.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            parts.Add($"search '{searchText.Trim()}'");
        }

        if (!string.IsNullOrWhiteSpace(familyDisplay))
        {
            parts.Add($"family '{familyDisplay.Trim()}'");
        }

        return string.Join(", ", parts);
    }

    public static string FormatFocusMode(RepositoryPortfolioFocusMode focusMode)
        => focusMode switch
        {
            RepositoryPortfolioFocusMode.All => "All",
            RepositoryPortfolioFocusMode.Attention => "Attention",
            RepositoryPortfolioFocusMode.Ready => "Ready Today",
            RepositoryPortfolioFocusMode.QueueActive => "Queue Active",
            RepositoryPortfolioFocusMode.Blocked => "Blocked",
            RepositoryPortfolioFocusMode.WaitingUsb => "USB Waiting",
            RepositoryPortfolioFocusMode.PublishReady => "Publish Ready",
            RepositoryPortfolioFocusMode.VerifyReady => "Verify Ready",
            RepositoryPortfolioFocusMode.Failed => "Failed",
            _ => focusMode.ToString()
        };
}

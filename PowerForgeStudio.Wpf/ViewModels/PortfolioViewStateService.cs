using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed class PortfolioViewStateService
{
    public const string DefaultViewId = "default";

    public PortfolioViewRestoreResult Restore(
        RepositoryPortfolioViewState? viewState,
        IReadOnlyList<PortfolioFocusOption> focusModes,
        Func<string?, RepositoryPortfolioFocusMode, string, PortfolioQuickPreset?> resolvePreset)
    {
        ArgumentNullException.ThrowIfNull(focusModes);
        ArgumentNullException.ThrowIfNull(resolvePreset);

        var defaultFocus = focusModes.First();
        if (viewState is null)
        {
            return new PortfolioViewRestoreResult(
                defaultFocus,
                string.Empty,
                null,
                null,
                "This triage view is saved automatically to local state once a portfolio snapshot exists.");
        }

        var selectedFocus = focusModes.FirstOrDefault(option => option.Mode == viewState.FocusMode) ?? defaultFocus;
        var selectedPreset = resolvePreset(viewState.PresetKey, viewState.FocusMode, viewState.SearchText);
        var familySelection = string.IsNullOrWhiteSpace(viewState.FamilyKey)
            ? string.Empty
            : $" and family '{viewState.FamilyKey}'";
        var viewMemory = string.IsNullOrWhiteSpace(viewState.SearchText)
            ? $"Restored saved triage view: {selectedFocus.DisplayName}{familySelection}."
            : $"Restored saved triage view: {selectedFocus.DisplayName}{familySelection} with search '{viewState.SearchText}'.";

        return new PortfolioViewRestoreResult(
            selectedFocus,
            viewState.SearchText,
            viewState.FamilyKey,
            selectedPreset,
            viewMemory);
    }

    public RepositoryPortfolioViewState CreateState(
        PortfolioOverviewViewModel portfolioOverview,
        string? familyKey)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);

        return new RepositoryPortfolioViewState(
            PresetKey: portfolioOverview.SelectedPreset?.Key,
            FocusMode: portfolioOverview.SelectedFocus.Mode,
            SearchText: portfolioOverview.SearchText,
            FamilyKey: familyKey,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    public string CreateSavedViewId(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Span<char> buffer = stackalloc char[displayName.Length];
        var index = 0;
        var pendingDash = false;

        foreach (var character in displayName.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingDash && index > 0)
                {
                    buffer[index++] = '-';
                }

                buffer[index++] = char.ToLowerInvariant(character);
                pendingDash = false;
            }
            else if (index > 0)
            {
                pendingDash = true;
            }
        }

        return index == 0
            ? "saved-view"
            : new string(buffer[..index]);
    }

    public string BuildSuggestedDisplayName(PortfolioOverviewViewModel portfolioOverview)
    {
        ArgumentNullException.ThrowIfNull(portfolioOverview);

        if (!string.IsNullOrWhiteSpace(portfolioOverview.SavedViewDraftName))
        {
            return portfolioOverview.SavedViewDraftName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(portfolioOverview.SelectedPreset?.DisplayName))
        {
            return portfolioOverview.SelectedPreset.DisplayName;
        }

        var focusName = portfolioOverview.SelectedFocus.DisplayName;
        return string.IsNullOrWhiteSpace(portfolioOverview.SearchText)
            ? focusName
            : $"{focusName} {portfolioOverview.SearchText}".Trim();
    }

    public PortfolioSavedViewItem CreateSavedViewItem(
        RepositoryPortfolioSavedView savedView,
        IReadOnlyList<PortfolioFocusOption> focusModes)
    {
        ArgumentNullException.ThrowIfNull(savedView);
        ArgumentNullException.ThrowIfNull(focusModes);

        var focusDisplayName = focusModes.FirstOrDefault(option => option.Mode == savedView.State.FocusMode)?.DisplayName
            ?? savedView.State.FocusMode.ToString();
        var summaryParts = new List<string> {
            focusDisplayName
        };

        if (!string.IsNullOrWhiteSpace(savedView.State.SearchText))
        {
            summaryParts.Add($"search '{savedView.State.SearchText}'");
        }

        if (!string.IsNullOrWhiteSpace(savedView.State.FamilyKey))
        {
            summaryParts.Add($"family '{savedView.State.FamilyKey}'");
        }

        return new PortfolioSavedViewItem(
            ViewId: savedView.ViewId,
            DisplayName: savedView.DisplayName,
            Summary: string.Join(", ", summaryParts),
            UpdatedAtDisplay: savedView.State.UpdatedAtUtc.ToLocalTime().ToString("g"),
            State: savedView.State);
    }
}

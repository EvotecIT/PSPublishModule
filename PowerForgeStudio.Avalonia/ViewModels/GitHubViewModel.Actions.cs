using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed record GitHubActionOption(
    GitHubProjectActionKind Kind,
    string Caption,
    string Explanation,
    bool RequiresBody = false,
    bool AllowsBody = true);

public sealed partial class GitHubViewModel
{
    public ObservableCollection<GitHubActionOption> AvailableActions { get; } = [];
    [ObservableProperty] private GitHubActionOption? _selectedAction;
    [ObservableProperty] private string _actionBody = "";
    [ObservableProperty] private GitHubProjectActionPlan? _pendingAction;
    [ObservableProperty] private bool _isActionRunning;
    [ObservableProperty] private bool _hasActionOutcome;
    [ObservableProperty] private string _actionStatus = "Select an item to prepare a reviewed GitHub action.";

    public bool CanPrepareAction => !_disposed && HasSelection && !IsDetailLoading && !IsActionRunning &&
        (Selected?.IsPullRequest != true || HeadSha.Length > 0) &&
        SelectedAction is { } option && (!option.RequiresBody || !string.IsNullOrWhiteSpace(ActionBody));
    public bool CanSubmitPendingAction => PendingAction is not null && !IsActionRunning;
    public bool HasActionBody => PendingAction?.HasBody == true;

    partial void OnSelectedActionChanged(GitHubActionOption? value)
    {
        if (value is { AllowsBody: false }) ActionBody = "";
        OnPropertyChanged(nameof(CanPrepareAction));
    }
    partial void OnActionBodyChanged(string value) => OnPropertyChanged(nameof(CanPrepareAction));
    partial void OnPendingActionChanged(GitHubProjectActionPlan? value)
    {
        OnPropertyChanged(nameof(CanSubmitPendingAction));
        OnPropertyChanged(nameof(HasActionBody));
    }
    partial void OnIsActionRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanPrepareAction));
        OnPropertyChanged(nameof(CanSubmitPendingAction));
    }

    private void UpdateActionOptions()
    {
        AvailableActions.Clear();
        if (Selected is not { } selected) return;
        AvailableActions.Add(new(GitHubProjectActionKind.Comment, "Comment", "Post a general discussion comment.", true));
        if (selected.IsPullRequest)
        {
            AvailableActions.Add(new(GitHubProjectActionKind.ApprovePullRequest, "Approve", "Submit approval at the exact displayed PR head."));
            AvailableActions.Add(new(GitHubProjectActionKind.RequestPullRequestChanges, "Request changes", "Request changes at the exact displayed PR head.", true));
        }
        else if (string.Equals(selected.State, "open", StringComparison.OrdinalIgnoreCase))
        {
            AvailableActions.Add(new(GitHubProjectActionKind.CloseIssue, "Close issue", "Close the issue only if its state is still open.", AllowsBody: false));
        }
        else
        {
            AvailableActions.Add(new(GitHubProjectActionKind.ReopenIssue, "Reopen issue", "Reopen the issue only if its state is still closed.", AllowsBody: false));
        }
        SelectedAction = AvailableActions.FirstOrDefault();
        ActionStatus = "Choose an action, add text when required, then review the captured target.";
        OnPropertyChanged(nameof(CanPrepareAction));
    }

    private void ResetActionDraft()
    {
        PendingAction = null;
        ActionBody = "";
        SelectedAction = null;
        HasActionOutcome = false;
        ActionStatus = Selected is null
            ? "Select an item to prepare a reviewed GitHub action."
            : "Loading current item evidence before actions are available.";
    }

    public bool PrepareActionReview()
    {
        PendingAction = null;
        if (!CanPrepareAction || Selected is not { } selected || SelectedAction is not { } action) return false;
        var head = selected.IsPullRequest ? HeadSha : "";
        if (selected.IsPullRequest && string.IsNullOrWhiteSpace(head))
        {
            ActionStatus = "Reload the pull request to capture its exact head before reviewing an action.";
            return false;
        }
        PendingAction = new(
            Slug,
            selected.Number,
            selected.IsPullRequest,
            action.Kind,
            selected.State.ToLowerInvariant(),
            head,
            action.AllowsBody ? ActionBody.Trim() : "");
        ActionStatus = "Action prepared. Confirm the target, revision and text in the review dialog.";
        return true;
    }

    public void CancelPendingAction()
    {
        if (IsActionRunning) return;
        PendingAction = null;
        ActionStatus = "Action review cancelled. The draft remains available.";
    }

    public async Task<bool> SubmitPreparedActionAsync()
    {
        if (!CanSubmitPendingAction || PendingAction is not { } plan) return false;
        var context = _contextVersion;
        var selected = Selected;
        bool OriginalSelection() => Current(context) && ReferenceEquals(selected, Selected);
        IsActionRunning = true;
        ActionStatus = $"Submitting {plan.ActionDisplay.ToLowerInvariant()}…";
        try
        {
            var receipt = await _actionService.ExecuteAsync(plan, _lifetime.Token);
            if (!OriginalSelection()) return true;
            ActionStatus = receipt.Summary + " GitHub evidence is being refreshed.";
            HasActionOutcome = true;
            PendingAction = null;
            ActionBody = "";
            var refreshed = plan.Kind is GitHubProjectActionKind.CloseIssue or GitHubProjectActionKind.ReopenIssue
                ? await RefreshAsync()
                : await LoadSelectionAsync();
            if (plan.Kind is GitHubProjectActionKind.CloseIssue or GitHubProjectActionKind.ReopenIssue)
            {
                // A list refresh invalidates its own selection; keep the receipt only in that original project context.
                if (Current(context + 1))
                {
                    ActionStatus = receipt.Summary + (refreshed
                        ? " Refreshed from GitHub."
                        : " GitHub refresh did not complete; reload manually before another action.");
                    HasActionOutcome = true;
                }
            }
            else if (OriginalSelection())
                ActionStatus = receipt.Summary + (refreshed
                    ? " Refreshed from GitHub."
                    : " GitHub refresh did not complete; reload manually before another action.");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (OriginalSelection()) ActionStatus = "GitHub action was cancelled or timed out. Reload before retrying.";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            if (OriginalSelection()) ActionStatus = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            if (OriginalSelection()) ActionStatus = SafeError(ex);
            return false;
        }
        finally
        {
            IsActionRunning = false;
        }
    }
}

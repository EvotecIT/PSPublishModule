using PowerForgeStudio.Domain.Portfolio;

namespace PowerForgeStudio.Wpf.ViewModels;

public sealed record RepositoryGitQuickActionWorkflowResult(
    string StatusMessage,
    RepositoryGitQuickActionReceipt? Receipt = null,
    bool ShouldRefresh = false);

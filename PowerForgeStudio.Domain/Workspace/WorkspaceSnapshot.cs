using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Verification;

namespace PowerForgeStudio.Domain.Workspace;

public sealed record WorkspaceSnapshot(
    string WorkspaceRoot,
    string DatabasePath,
    PSPublishModuleResolution BuildEngineResolution,
    RepositoryPortfolioSummary Summary,
    IReadOnlyList<RepositoryPortfolioItem> PortfolioItems,
    IReadOnlyList<RepositoryReleaseInboxItem> ReleaseInboxItems,
    IReadOnlyList<PortfolioDashboardSnapshot> DashboardCards,
    IReadOnlyList<RepositoryWorkspaceFamilySnapshot> RepositoryFamilies,
    IReadOnlyList<RepositoryWorkspaceFamilyLaneSnapshot> RepositoryFamilyLanes,
    ReleaseQueueSession QueueSession,
    StationSnapshot<ReleaseSigningArtifact> SigningStation,
    IReadOnlyList<ReleaseSigningReceipt> SigningReceipts,
    ReceiptBatchSnapshot<ReleaseSigningReceipt> SigningReceiptBatch,
    StationSnapshot<ReleasePublishTarget> PublishStation,
    IReadOnlyList<ReleasePublishReceipt> PublishReceipts,
    ReceiptBatchSnapshot<ReleasePublishReceipt> PublishReceiptBatch,
    StationSnapshot<ReleaseVerificationTarget> VerificationStation,
    IReadOnlyList<ReleaseVerificationReceipt> VerificationReceipts,
    ReceiptBatchSnapshot<ReleaseVerificationReceipt> VerificationReceiptBatch,
    IReadOnlyList<RepositoryGitQuickActionReceipt> GitQuickActionReceipts,
    RepositoryPortfolioViewState? SavedPortfolioView);

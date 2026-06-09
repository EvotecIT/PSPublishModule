using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryPortfolioDashboardService
{
    private readonly RepositoryPortfolioFocusService _portfolioFocusService;

    public RepositoryPortfolioDashboardService()
        : this(new RepositoryPortfolioFocusService())
    {
    }

    public RepositoryPortfolioDashboardService(RepositoryPortfolioFocusService portfolioFocusService)
    {
        _portfolioFocusService = portfolioFocusService;
    }

    public IReadOnlyList<PortfolioDashboardSnapshot> BuildCards(
        IReadOnlyList<RepositoryPortfolioItem> portfolioItems,
        ReleaseQueueSession? queueSession)
    {
        ArgumentNullException.ThrowIfNull(portfolioItems);

        var readyToday = _portfolioFocusService.Filter(portfolioItems, queueSession, RepositoryPortfolioFocusMode.Ready, string.Empty).Count;
        var usbWaiting = _portfolioFocusService.Filter(portfolioItems, queueSession, RepositoryPortfolioFocusMode.WaitingUsb, string.Empty).Count;
        var publishReady = _portfolioFocusService.Filter(portfolioItems, queueSession, RepositoryPortfolioFocusMode.PublishReady, string.Empty).Count;
        var verifyReady = _portfolioFocusService.Filter(portfolioItems, queueSession, RepositoryPortfolioFocusMode.VerifyReady, string.Empty).Count;
        var failed = _portfolioFocusService.Filter(portfolioItems, queueSession, RepositoryPortfolioFocusMode.Failed, string.Empty).Count;

        return [
            new PortfolioDashboardSnapshot("ready-today", "Ready Today", readyToday.ToString(), "Repos ready to move into a real build.", RepositoryPortfolioFocusMode.Ready, PresetKey: "ready-today"),
            new PortfolioDashboardSnapshot("usb-waiting", "USB Waiting", usbWaiting.ToString(), "Repos paused at the signing gate.", RepositoryPortfolioFocusMode.WaitingUsb, PresetKey: "usb-waiting"),
            new PortfolioDashboardSnapshot("publish-ready", "Publish Ready", publishReady.ToString(), "Repos with signed outputs ready for publish.", RepositoryPortfolioFocusMode.PublishReady),
            new PortfolioDashboardSnapshot("verify-ready", "Verify Ready", verifyReady.ToString(), "Repos whose publish results are ready to verify.", RepositoryPortfolioFocusMode.VerifyReady),
            new PortfolioDashboardSnapshot("failed", "Failed", failed.ToString(), "Repos with failed queue transitions that likely need intervention.", RepositoryPortfolioFocusMode.Failed)
        ];
    }
}

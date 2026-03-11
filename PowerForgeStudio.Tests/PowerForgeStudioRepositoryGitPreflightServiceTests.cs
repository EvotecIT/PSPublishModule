using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Tests;

public sealed class PowerForgeStudioRepositoryGitPreflightServiceTests
{
    private readonly RepositoryGitPreflightService _service = new();

    [Fact]
    public void Assess_BaseBranchAhead_ReturnsProtectedFlowAttention()
    {
        var diagnostics = _service.Assess(
            CreateRepository(),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "main",
                UpstreamBranch: "origin/main",
                AheadCount: 3,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RepositoryGitDiagnosticCode.ProtectedBaseBranchFlow, diagnostic.Code);
        Assert.Equal(RepositoryGitDiagnosticSeverity.Attention, diagnostic.Severity);
        Assert.Contains("direct push is likely blocked", diagnostic.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assess_DetachedHead_ReturnsBlockedDiagnostic()
    {
        var diagnostics = _service.Assess(
            CreateRepository(),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "(detached)",
                UpstreamBranch: "origin/main",
                AheadCount: 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RepositoryGitDiagnosticCode.DetachedHead, diagnostic.Code);
        Assert.Equal(RepositoryGitDiagnosticSeverity.Blocked, diagnostic.Severity);
    }

    [Fact]
    public void Assess_NoUpstream_ReturnsAttentionDiagnostic()
    {
        var diagnostics = _service.Assess(
            CreateRepository(),
            new RepositoryGitSnapshot(
                IsGitRepository: true,
                BranchName: "codex/release-ops",
                UpstreamBranch: null,
                AheadCount: 1,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RepositoryGitDiagnosticCode.NoUpstream, diagnostic.Code);
        Assert.Equal(RepositoryGitDiagnosticSeverity.Attention, diagnostic.Severity);
    }

    [Fact]
    public void Assess_GitUnavailable_ReturnsBlockedDiagnostic()
    {
        var diagnostics = _service.Assess(
            CreateRepository(),
            new RepositoryGitSnapshot(
                IsGitRepository: false,
                BranchName: null,
                UpstreamBranch: null,
                AheadCount: 0,
                BehindCount: 0,
                TrackedChangeCount: 0,
                UntrackedChangeCount: 0));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RepositoryGitDiagnosticCode.GitUnavailable, diagnostic.Code);
        Assert.Equal(RepositoryGitDiagnosticSeverity.Blocked, diagnostic.Severity);
    }

    private static RepositoryCatalogEntry CreateRepository()
        => new(
            Name: "PSPublishModule",
            RootPath: @"C:\Support\GitHub\PSPublishModule",
            RepositoryKind: ReleaseRepositoryKind.Mixed,
            WorkspaceKind: ReleaseWorkspaceKind.PrimaryRepository,
            ModuleBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Module.ps1",
            ProjectBuildScriptPath: @"C:\Support\GitHub\PSPublishModule\Build\Build-Project.ps1",
            IsWorktree: false,
            HasWebsiteSignals: false);
}

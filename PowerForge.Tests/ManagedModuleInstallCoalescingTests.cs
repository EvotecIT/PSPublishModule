using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallCoalescingTests
{
    [Fact]
    public async Task Context_branches_share_in_flight_install_targets()
    {
        var context = new ManagedModuleInstallContext();
        var branch = context.CreateBranch();

        Assert.True(context.TryBeginInstall("module|1.0.0", out _, out var pending, out var runIndependently));
        Assert.False(runIndependently);
        Assert.False(branch.TryBeginInstall("module|1.0.0", out var existingInstall, out var branchPending, out runIndependently));
        Assert.False(runIndependently);
        Assert.Null(branchPending);

        var result = new ManagedModuleInstallResult
        {
            Name = "Company.Tools",
            Version = "1.0.0",
            Status = ManagedModuleInstallStatus.Installed
        };
        pending.Complete(result);

        Assert.Same(result, await existingInstall);
        pending.Dispose();
        Assert.True(branch.TryBeginInstall("module|1.0.0", out _, out var nextPending, out runIndependently));
        Assert.False(runIndependently);
        nextPending.Dispose();
    }

    [Fact]
    public void Context_skips_waiting_when_coalescing_would_create_a_cycle()
    {
        var root = new ManagedModuleInstallContext();
        var branchA = root.CreateBranch();
        var branchB = root.CreateBranch();

        Assert.True(branchA.TryBeginInstall("A", out _, out var pendingA, out var runIndependently));
        Assert.False(runIndependently);
        Assert.True(branchB.TryBeginInstall("B", out _, out var pendingB, out runIndependently));
        Assert.False(runIndependently);

        using (branchA.EnterInstallWait("B"))
        {
            Assert.False(branchB.TryBeginInstall("A", out _, out var pending, out runIndependently));
            Assert.True(runIndependently);
            Assert.Null(pending);
        }

        pendingA.Dispose();
        pendingB.Dispose();
    }

    [Fact]
    public void Context_branches_share_completed_install_targets()
    {
        var context = new ManagedModuleInstallContext();
        var branch = context.CreateBranch();
        var result = new ManagedModuleInstallResult
        {
            Name = "Company.Tools",
            Version = "1.0.0",
            Status = ManagedModuleInstallStatus.Installed
        };

        context.RecordCompletedInstall("module|1.0.0", result);

        Assert.True(branch.TryGetCompletedInstall("module|1.0.0", out var completed));
        Assert.Same(result, completed);
        Assert.False(branch.TryGetCompletedInstall("module|2.0.0", out _));
    }

    [Fact]
    public void Install_coalescing_key_is_disabled_for_force_or_credentials()
    {
        var request = CreateRequest();

        request.Force = true;
        Assert.Null(ManagedModuleInstallService.TryCreateInstallCoalescingKey(request, "1.0.0", "C:\\Modules"));

        request.Force = false;
        request.Credential = new RepositoryCredential { UserName = "user", Secret = "secret" };
        Assert.Null(ManagedModuleInstallService.TryCreateInstallCoalescingKey(request, "1.0.0", "C:\\Modules"));
    }

    [Fact]
    public void Install_coalescing_key_includes_safety_policy_inputs()
    {
        var baseline = ManagedModuleInstallService.TryCreateInstallCoalescingKey(CreateRequest(), "1.0.0", "C:\\Modules");
        Assert.False(string.IsNullOrWhiteSpace(baseline));

        var clobber = CreateRequest();
        clobber.AllowClobber = true;
        Assert.NotEqual(baseline, ManagedModuleInstallService.TryCreateInstallCoalescingKey(clobber, "1.0.0", "C:\\Modules"));

        var authenticode = CreateRequest();
        authenticode.AuthenticodeCheck = true;
        Assert.NotEqual(baseline, ManagedModuleInstallService.TryCreateInstallCoalescingKey(authenticode, "1.0.0", "C:\\Modules"));

        var trust = CreateRequest();
        trust.TrustPolicy = new ManagedModuleTrustPolicy
        {
            RequireTrustedRepository = true,
            AllowedAuthors = new[] { "Evotec" }
        };
        Assert.NotEqual(baseline, ManagedModuleInstallService.TryCreateInstallCoalescingKey(trust, "1.0.0", "C:\\Modules"));
    }

    private static ManagedModuleInstallRequest CreateRequest()
        => new()
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json"),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = "C:\\Modules",
            AcceptLicense = true
        };
}

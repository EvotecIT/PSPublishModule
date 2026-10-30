using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallCoalescingTests
{
    [Fact]
    public async Task Context_branches_share_in_flight_install_targets()
    {
        var context = new ManagedModuleInstallContext();
        var branch = context.CreateBranch();

        Assert.True(context.TryBeginInstall("module|1.0.0", out _, out var pending));
        Assert.False(branch.TryBeginInstall("module|1.0.0", out var existingInstall, out var branchPending));
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
        Assert.True(branch.TryBeginInstall("module|1.0.0", out _, out var nextPending));
        nextPending.Dispose();
    }

    [Fact]
    public void Context_rejects_cycle_when_both_coalescing_decisions_precede_wait_registration()
    {
        using var root = new ManagedModuleInstallContext();
        var a = root.CreateBranch();
        var b = root.CreateBranch();
        using var scopeA = a.Enter("A");
        using var scopeB = b.Enter("B");
        Assert.True(a.TryBeginInstall("A", out _, out var pendingA));
        Assert.True(b.TryBeginInstall("B", out _, out var pendingB));
        using (pendingA)
        using (pendingB)
        {
            Assert.False(a.TryBeginInstall("B", out _, out _));
            Assert.False(b.TryBeginInstall("A", out _, out _));
            using var wait = a.EnterInstallWait("B");
            var exception = Assert.Throws<InvalidOperationException>(() => b.EnterInstallWait("A"));
            Assert.Equal("Managed module dependency cycle detected: A -> B -> A.", exception.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Context_wait_disposal_preserves_other_sibling_and_duplicate_waits(bool duplicate)
    {
        using var root = new ManagedModuleInstallContext();
        var a = root.CreateBranch();
        var b = root.CreateBranch();
        var c = root.CreateBranch();
        using var scopeA = a.Enter("A");
        using var scopeB = b.Enter("B");
        using var scopeC = c.Enter("C");
        Assert.True(a.TryBeginInstall("A", out _, out var pendingA));
        Assert.True(b.TryBeginInstall("B", out _, out var pendingB));
        Assert.True(c.TryBeginInstall("C", out _, out var pendingC));
        using (pendingA)
        using (pendingB)
        using (pendingC)
        {
            using var first = a.EnterInstallWait("B");
            var other = a.EnterInstallWait(duplicate ? "B" : "C");
            other.Dispose();
            other.Dispose();
            Assert.Throws<InvalidOperationException>(() => b.EnterInstallWait("A"));
            first.Dispose();
            using var validWait = b.EnterInstallWait("A");
        }
    }

    [Fact]
    public void Context_cycle_path_preserves_owned_intermediate_dependencies()
    {
        using var root = new ManagedModuleInstallContext();
        var a = root.CreateBranch();
        var b = root.CreateBranch();
        using var scopeA = a.Enter("A");
        using var scopeB = b.Enter("B");
        Assert.True(a.TryBeginInstall("a-target", out _, out var pendingA));
        Assert.True(b.TryBeginInstall("b-target", out _, out var pendingB));
        using (pendingA)
        using (pendingB)
        {
            var c = a.CreateBranch();
            using var scopeC = c.Enter("C");
            Assert.True(c.TryBeginInstall("c-target", out _, out var pendingC));
            using (pendingC)
            using (c.EnterInstallWait("b-target"))
            {
                var exception = Assert.Throws<InvalidOperationException>(() => b.EnterInstallWait("a-target"));
                Assert.Equal("Managed module dependency cycle detected: A -> C -> B -> A.", exception.Message);
            }
        }
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

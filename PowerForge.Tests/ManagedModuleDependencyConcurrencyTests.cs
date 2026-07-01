using System.Management.Automation;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ManagedModuleDependencyConcurrencyTests
{
    [Fact]
    public void Managed_module_default_concurrency_scales_with_host_without_exceeding_benchmark_cap()
    {
        Assert.Equal(96, ManagedModuleConcurrencyDefaults.MaximumDefaultConcurrency);
        var expected = Math.Min(
            ManagedModuleConcurrencyDefaults.MaximumDefaultConcurrency,
            Math.Max(16, Environment.ProcessorCount * 8));

        Assert.Equal(expected, ManagedModuleConcurrencyDefaults.ResolveDefault());
        Assert.Equal(expected, new ManagedModuleRepositoryClientOptions().MaxConnectionsPerServer);
    }

    [Fact]
    public async Task InstallAsync_rejects_negative_dependency_concurrency()
    {
        var service = new ManagedModuleInstallService(new NullLogger());
        var request = CreateInstallRequest();
        request.DependencyConcurrency = -1;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.InstallAsync(request));
    }

    [Fact]
    public async Task InstallAsync_rejects_dependency_concurrency_above_engine_limit()
    {
        var service = new ManagedModuleInstallService(new NullLogger());
        var request = CreateInstallRequest();
        request.DependencyConcurrency = 257;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.InstallAsync(request));
    }

    [Fact]
    public async Task UpdateAsync_rejects_negative_dependency_concurrency()
    {
        var service = new ManagedModuleUpdateService(new NullLogger());
        var request = new ManagedModuleUpdateRequest
        {
            Repository = new ManagedModuleRepository("Local", Path.GetTempPath()),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = Path.GetTempPath(),
            DependencyConcurrency = -1
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.UpdateAsync(request));
    }

    [Theory]
    [InlineData("Install-ManagedModule")]
    [InlineData("Save-ManagedModule")]
    [InlineData("Update-ManagedModule")]
    public void Managed_module_cmdlets_expose_dependency_concurrency(string commandName)
    {
        using var ps = PowerShell.Create();
        ps.AddCommand("Import-Module")
            .AddParameter("Name", typeof(InstallManagedModuleCommand).Assembly.Location)
            .AddParameter("Force");
        _ = ps.Invoke();
        AssertNoPowerShellErrors(ps);
        ps.Commands.Clear();

        ps.AddCommand("Get-Command").AddParameter("Name", commandName);
        var command = Assert.IsType<CmdletInfo>(Assert.Single(ps.Invoke()).BaseObject);

        AssertNoPowerShellErrors(ps);
        Assert.True(command.Parameters.ContainsKey("DependencyConcurrency"));
    }

    private static ManagedModuleInstallRequest CreateInstallRequest()
        => new()
        {
            Repository = new ManagedModuleRepository("Local", Path.GetTempPath()),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = Path.GetTempPath()
        };

    private static void AssertNoPowerShellErrors(PowerShell ps)
    {
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(error => error.ToString())));
    }
}

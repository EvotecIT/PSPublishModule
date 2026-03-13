using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellRepositoryResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesSharedPowerShellLookupScript()
    {
        PowerShellRunRequest? captured = null;
        var resolver = new PowerShellRepositoryResolver(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(
                0,
                "{\"Name\":\"PrivateGallery\",\"SourceUri\":\"https://packages.contoso.test/powershell/v3/index.json\",\"PublishUri\":\"https://packages.contoso.test/powershell/api/v2/package\"}",
                string.Empty,
                "pwsh");
        }));

        var result = await resolver.ResolveAsync(@"C:\repo", "PrivateGallery");

        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Contains("Get-PSResourceRepository", captured.CommandText!, StringComparison.Ordinal);
        Assert.NotNull(result);
        Assert.Equal("PrivateGallery", result!.Name);
        Assert.Equal("https://packages.contoso.test/powershell/v3/index.json", result.SourceUri);
    }

    [Fact]
    public async Task ResolveAsync_PassesThroughAbsoluteUri()
    {
        var resolver = new PowerShellRepositoryResolver(new StubPowerShellRunner(_ => throw new InvalidOperationException("PowerShell should not be used for direct URIs.")));

        var result = await resolver.ResolveAsync(@"C:\repo", "https://packages.contoso.test/powershell/v3/index.json");

        Assert.NotNull(result);
        Assert.Equal("https://packages.contoso.test/powershell/v3/index.json", result!.SourceUri);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }
}

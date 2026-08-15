using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("sudo -D /tmp/evil npm install safe-package@1.0.0")]
    [InlineData("sudo -D/tmp/evil npm install safe-package@1.0.0")]
    [InlineData("sudo --chdir=/tmp/evil npm install safe-package@1.0.0")]
    [InlineData("sudo -R /tmp/root npm install safe-package@1.0.0")]
    [InlineData("sudo --chroot /tmp/root npm install safe-package@1.0.0")]
    public void Scan_RejectsSudoDirectoryAndChrootWrappers(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);

        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT");
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("python -m pipx install safe-package==1.0.0")]
    [InlineData("python3 -m pipx.__main__ run safe-package==1.0.0")]
    [InlineData("py -m pipx upgrade safe-package==1.0.0")]
    public void Scan_VerifiesPythonModulePipxPackages(string command)
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"releases":{"1.0.0":[{}]}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);

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
    [InlineData("setenv NPM_CONFIG_REGISTRY https://attacker.example")]
    [InlineData("set -gx PIP_INDEX_URL https://attacker.example")]
    [InlineData("setx BUN_INSTALL_REGISTRY https://attacker.example")]
    public void Scan_RejectsShellUtilityPackageSourceEnvironmentWrites(string command)
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

    [Theory]
    [InlineData("setenv NODE_OPTIONS --require /tmp/evil.js")]
    [InlineData("set -gx PYTHONPATH /tmp/evil")]
    [InlineData("setx RUBYOPT -r/tmp/evil.rb")]
    public void Scan_RejectsShellUtilityRuntimeInjectionEnvironmentWrites(string command)
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
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.COMMAND.RUNTIME_INJECTION");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("nuget.exe install safe-package -Version 1.0.0")]
    [InlineData("nuget.cmd install safe-package -Version 1.0.0")]
    public void Scan_VerifiesWindowsNuGetLaunchers(string command)
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":["1.0.0"]}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);

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
}

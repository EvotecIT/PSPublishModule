using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebAgentContentSecurityScannerTests
{
    [Theory]
    [InlineData("dotnet build project.csproj")]
    [InlineData("dotnet build project.csproj --no-restore")]
    [InlineData("dotnet publish project.csproj")]
    [InlineData("dotnet run --project project.csproj")]
    [InlineData("dotnet test project.csproj")]
    [InlineData("dotnet pack project.csproj")]
    [InlineData("dotnet msbuild project.csproj")]
    [InlineData("dotnet vstest tests.dll")]
    [InlineData("dotnet watch run")]
    [InlineData("dotnet format project.csproj")]
    [InlineData("dotnet workload restore")]
    [InlineData("dotnet unknown-command")]
    public void Scan_RejectsDotNetProjectAndUnknownSdkCommands(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyPackages = false
            });

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
    [InlineData("dotnet --info")]
    [InlineData("dotnet --version")]
    [InlineData("dotnet --list-sdks")]
    [InlineData("dotnet --list-runtimes")]
    [InlineData("dotnet --help")]
    public void Scan_AllowsDotNetInformationalCommands(string command)
    {
        using var scanner = new WebAgentContentSecurityScanner();
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions
            {
                SiteRoot = root,
                Files = ["llms.txt"],
                VerifyPackages = false
            });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Empty(result.Findings);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("npm exec --package safe-package@1.0.0 npm install attacker-package@1.0.0")]
    [InlineData("npm exec -p safe-package@1.0.0 npm install attacker-package@1.0.0")]
    [InlineData("npm exec --package safe-package@1.0.0 --package helper@1.0.0 npm install attacker-package@1.0.0")]
    public void Scan_RejectsNestedManagerAfterSpacedNpmExecPackageOptions(string command)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND");
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("npm install safe-package\\evil", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND")]
    [InlineData("npm install safe-package^evil", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND")]
    [InlineData("npm install safe-package`evil", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND")]
    [InlineData("npm install safe-package\\\nevil", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND")]
    public void Scan_RejectsShellConstructionInsidePackageOperands(string command, string expectedCode)
    {
        using var handler = new RegistryHandler(_ => throw new InvalidOperationException("Registry must not be called."));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", command);
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.False(result.Success);
            Assert.Contains(result.Findings, finding => finding.Code == expectedCode);
            Assert.Equal(0, result.PackageReferenceCount);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("--save-exact")]
    [InlineData("--save")]
    [InlineData("--save-prod")]
    [InlineData("-P")]
    [InlineData("--save-optional")]
    [InlineData("-O")]
    [InlineData("--save-peer")]
    [InlineData("--save-bundle")]
    [InlineData("-B")]
    public void Scan_AcceptsNpmSaveFlags(string option)
    {
        using var handler = new RegistryHandler(_ => JsonResponse("""{"versions":{"1.0.0":{}}}"""));
        using var scanner = new WebAgentContentSecurityScanner(new HttpClient(handler));
        var root = CreateArtifact("llms.txt", $"npm install --global {option} safe-package@1.0.0");
        try
        {
            var result = scanner.Scan(new WebAgentContentSecurityOptions { SiteRoot = root, Files = ["llms.txt"] });

            Assert.True(result.Success, string.Join(" | ", result.Findings.Select(static finding => finding.Message)));
            Assert.Equal(1, result.PackageReferenceCount);
            Assert.Equal(1, result.VerifiedPackageCount);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}

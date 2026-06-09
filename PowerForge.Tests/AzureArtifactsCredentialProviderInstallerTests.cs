using System;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class AzureArtifactsCredentialProviderInstallerTests
{
    [Fact]
    public void InstallForCurrentUser_ParsesChangedPathsAndMessages()
    {
        var stdout = string.Join(Environment.NewLine, new[]
        {
            "PFAZART::CHANGED::1",
            "PFAZART::PATH::" + Encode(@"C:\Users\Test\.nuget\plugins\netcore\CredentialProvider.Microsoft.dll"),
            "PFAZART::VERSION::" + Encode("2.0.312"),
            "PFAZART::MESSAGE::" + Encode("Azure Artifacts Credential Provider detected at 1 path(s), version 2.0.312.")
        });

        var installer = new AzureArtifactsCredentialProviderInstaller(
            new StubPowerShellRunner(new PowerShellRunResult(0, stdout, string.Empty, "pwsh.exe")),
            new NullLogger());

        var result = installer.InstallForCurrentUser();

        Assert.True(result.Succeeded);
        Assert.True(result.Changed);
        Assert.Single(result.Paths);
        Assert.Equal("2.0.312", result.Version);
        Assert.Contains("Credential Provider detected", result.Messages.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallForCurrentUser_ThrowsOnFailure()
    {
        var stdout = "PFAZART::ERROR::" + Encode("Download failed.");
        var installer = new AzureArtifactsCredentialProviderInstaller(
            new StubPowerShellRunner(new PowerShellRunResult(1, stdout, string.Empty, "pwsh.exe")),
            new NullLogger());

        var ex = Assert.Throws<InvalidOperationException>(() => installer.InstallForCurrentUser());
        Assert.Contains("Download failed.", ex.Message, StringComparison.Ordinal);
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubPowerShellRunner(PowerShellRunResult result)
        {
            _result = result;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            return _result;
        }
    }
}

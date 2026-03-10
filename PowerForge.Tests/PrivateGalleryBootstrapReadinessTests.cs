using System;
using System.IO;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateGalleryBootstrapReadinessTests
{
    [Fact]
    public void AzureArtifactsCredentialProviderLocator_DetectsProviderFromNuGetPluginPaths()
    {
        var root = CreateTempPath();
        try
        {
            var providerPath = Path.Combine(root, "plugins", "CredentialProvider.Microsoft.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(providerPath)!);
            File.WriteAllText(providerPath, string.Empty);

            var detection = AzureArtifactsCredentialProviderLocator.Detect(
                name => string.Equals(name, "NUGET_PLUGIN_PATHS", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(root, "plugins")
                    : null,
                _ => root);

            Assert.True(detection.IsDetected);
            Assert.Contains(providerPath, detection.Paths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PSResourceGetClient_IsAvailable_ReturnsTrue_WhenToolIsPresent()
    {
        var client = new PSResourceGetClient(
            new StubPowerShellRunner(new PowerShellRunResult(0, "PFPSRG::AVAILABLE::1" + Environment.NewLine + "PFPSRG::VERSION::" + Encode("1.2.0"), string.Empty, "pwsh.exe")),
            new NullLogger());

        var available = client.IsAvailable(out var message);

        Assert.True(available);
        Assert.Null(message);
    }

    [Fact]
    public void PowerShellGetClient_IsAvailable_ReturnsFalse_WhenToolIsMissing()
    {
        var error = Convert.ToBase64String(Encoding.UTF8.GetBytes("PowerShellGet not available."));
        var client = new PowerShellGetClient(
            new StubPowerShellRunner(new PowerShellRunResult(3, "PFPWSGET::ERROR::" + error, string.Empty, "pwsh.exe")),
            new NullLogger());

        var available = client.IsAvailable(out var message);

        Assert.False(available);
        Assert.Equal("PowerShellGet not available.", message);
    }

    [Fact]
    public void PowerShellGetClient_GetAvailability_ReturnsDetectedVersion()
    {
        var client = new PowerShellGetClient(
            new StubPowerShellRunner(new PowerShellRunResult(0, "PFPWSGET::AVAILABLE::1" + Environment.NewLine + "PFPWSGET::VERSION::" + Encode("2.2.5"), string.Empty, "pwsh.exe")),
            new NullLogger());

        var info = client.GetAvailability();

        Assert.True(info.Available);
        Assert.Equal("2.2.5", info.Version);
    }

    private static string CreateTempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateGalleryServiceTests
{
    [Fact]
    public void ResolveCredential_ValidatesMissingUserBeforeReadingSecretFile()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost());

        var ex = Assert.Throws<ArgumentException>(() => service.ResolveCredential(
            repositoryName: "Company",
            bootstrapMode: PrivateGalleryBootstrapMode.CredentialPrompt,
            credentialUserName: null,
            credentialSecret: null,
            credentialSecretFilePath: "missing-secret.txt",
            promptForCredential: false));

        Assert.Contains("CredentialUserName is required", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("credentialUserName", ex.ParamName);
    }

    [Fact]
    public void EnsureAzureArtifactsRepositoryRegistered_PreservesNullPriority()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost(shouldProcess: false));
        var status = new BootstrapPrerequisiteStatus(
            psResourceGetAvailable: true,
            psResourceGetVersion: "1.2.0",
            psResourceGetMeetsMinimumVersion: true,
            psResourceGetSupportsExistingSessionBootstrap: true,
            psResourceGetMessage: null,
            powerShellGetAvailable: true,
            powerShellGetVersion: "2.2.5",
            powerShellGetMessage: null,
            credentialProviderDetection: new AzureArtifactsCredentialProviderDetectionResult(),
            readinessMessages: Array.Empty<string>());

        var result = service.EnsureAzureArtifactsRepositoryRegistered(
            azureDevOpsOrganization: "contoso",
            azureDevOpsProject: "Platform",
            azureArtifactsFeed: "Modules",
            repositoryName: null,
            tool: RepositoryRegistrationTool.PSResourceGet,
            trusted: true,
            priority: null,
            bootstrapModeRequested: PrivateGalleryBootstrapMode.CredentialPrompt,
            bootstrapModeUsed: PrivateGalleryBootstrapMode.CredentialPrompt,
            credentialSource: PrivateGalleryCredentialSource.None,
            credential: null,
            prerequisiteStatus: status,
            shouldProcessAction: "Register module repository");

        Assert.Null(result.Priority);
    }

    [Theory]
    [InlineData("Package with name '__PowerForgePrivateGalleryConnectionProbe__' could not be found in repository 'Company'.")]
    [InlineData("No match was found for __PowerForgePrivateGalleryConnectionProbe__.")]
    [InlineData("No packages found matching __PowerForgePrivateGalleryConnectionProbe__.")]
    public void IsMissingProbePackageMessage_TreatsProbePackageAbsenceAsReachableRepository(string message)
    {
        Assert.True(PrivateGalleryService.IsMissingProbePackageMessage(
            message,
            "__PowerForgePrivateGalleryConnectionProbe__"));
    }

    [Theory]
    [InlineData("Response status code does not indicate success: 401 (Unauthorized).")]
    [InlineData("The source repository 'Company' is not registered.")]
    [InlineData("Package with name 'SomeOtherPackage' could not be found in repository 'Company'.")]
    public void IsMissingProbePackageMessage_DoesNotHideAuthOrRegistrationFailures(string message)
    {
        Assert.False(PrivateGalleryService.IsMissingProbePackageMessage(
            message,
            "__PowerForgePrivateGalleryConnectionProbe__"));
    }

    [Fact]
    public void PrimeAzureArtifactsCredentialProviderSession_FallsBackWhenFirstProviderRequiresMissingDotNetRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var netCoreProvider = CreateCredentialProviderFile(root, "netcore");
            var netFxProvider = CreateCredentialProviderFile(root, "netfx");
            var calls = new List<string>();
            var runner = new StubProcessRunner(request =>
            {
                calls.Add(request.FileName);
                if (string.Equals(request.FileName, netCoreProvider, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessRunResult(
                        -2147450749,
                        string.Empty,
                        "You must install .NET to run this application.",
                        request.FileName,
                        TimeSpan.Zero,
                        timedOut: false);
                }

                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var service = new PrivateGalleryService(
                new FakePrivateGalleryHost(),
                runner,
                netFrameworkReleaseProvider: () => 533320);
            var registration = new ModuleRepositoryRegistrationResult
            {
                RepositoryName = "Company",
                PSResourceGetUri = "https://pkgs.dev.azure.com/contoso/_packaging/Modules/nuget/v3/index.json",
                AzureArtifactsCredentialProviderPaths = new[] { netFxProvider, netCoreProvider }
            };

            var result = service.PrimeAzureArtifactsCredentialProviderSession(registration, TimeSpan.FromSeconds(1));

            Assert.True(result.Succeeded);
            Assert.Equal(netFxProvider, result.ProviderPath);
            Assert.Equal(new[] { netCoreProvider, netFxProvider }, calls);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void PrimeAzureArtifactsCredentialProviderSession_FallsBackWhenFirstProviderFailsWithoutTimingOut()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var netCoreProvider = CreateCredentialProviderFile(root, "netcore");
            var netFxProvider = CreateCredentialProviderFile(root, "netfx");
            var calls = new List<string>();
            var runner = new StubProcessRunner(request =>
            {
                calls.Add(request.FileName);
                if (string.Equals(request.FileName, netCoreProvider, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProcessRunResult(
                        1,
                        string.Empty,
                        string.Empty,
                        request.FileName,
                        TimeSpan.Zero,
                        timedOut: false);
                }

                return new ProcessRunResult(0, string.Empty, string.Empty, request.FileName, TimeSpan.Zero, timedOut: false);
            });
            var service = new PrivateGalleryService(
                new FakePrivateGalleryHost(),
                runner,
                netFrameworkReleaseProvider: () => 533320);
            var registration = new ModuleRepositoryRegistrationResult
            {
                RepositoryName = "Company",
                PSResourceGetUri = "https://pkgs.dev.azure.com/contoso/_packaging/Modules/nuget/v3/index.json",
                AzureArtifactsCredentialProviderPaths = new[] { netFxProvider, netCoreProvider }
            };

            var result = service.PrimeAzureArtifactsCredentialProviderSession(registration, TimeSpan.FromSeconds(1));

            Assert.True(result.Succeeded);
            Assert.Equal(netFxProvider, result.ProviderPath);
            Assert.Equal(new[] { netCoreProvider, netFxProvider }, calls);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void IsMissingDotNetRuntimeFailure_DetectsCredentialProviderAppHostFailure()
    {
        var result = new ProcessRunResult(
            1,
            string.Empty,
            "App: CredentialProvider.Microsoft.exe apphost_version=8.0.26 missing_runtime=true",
            "CredentialProvider.Microsoft.exe",
            TimeSpan.Zero,
            timedOut: false);

        Assert.True(PrivateGalleryService.IsMissingDotNetRuntimeFailure(result));
    }

    [Fact]
    public void GetNetFxCredentialProviderPrerequisiteFailure_RequiresNetFramework481()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = CreateCredentialProviderFile(root, "netfx");
            File.WriteAllText(
                provider + ".config",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <startup>
                    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8.1" />
                  </startup>
                </configuration>
                """);

            var failure = PrivateGalleryService.GetNetFxCredentialProviderPrerequisiteFailure(provider, installedRelease: 528449);

            Assert.NotNull(failure);
            Assert.Contains(".NET Framework 4.8.1", failure, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Microsoft.win-*", failure, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetNetFxCredentialProviderPrerequisiteFailure_AllowsNetFramework481()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = CreateCredentialProviderFile(root, "netfx");

            var failure = PrivateGalleryService.GetNetFxCredentialProviderPrerequisiteFailure(provider, installedRelease: 533320);

            Assert.Null(failure);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetNetFxCredentialProviderPrerequisiteFailure_UsesProviderTargetFramework()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = CreateCredentialProviderFile(root, "netfx");
            File.WriteAllText(
                provider + ".config",
                """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <startup>
                    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
                  </startup>
                </configuration>
                """);

            var failure = PrivateGalleryService.GetNetFxCredentialProviderPrerequisiteFailure(provider, installedRelease: 528449);

            Assert.Null(failure);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void EnsureMicrosoftArtifactRegistryRegistered_ReturnsWhatIfResultWithoutAzureCredentialProvider()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost(shouldProcess: false));
        var status = new BootstrapPrerequisiteStatus(
            psResourceGetAvailable: true,
            psResourceGetVersion: "1.2.0",
            psResourceGetMeetsMinimumVersion: true,
            psResourceGetSupportsExistingSessionBootstrap: true,
            psResourceGetMessage: null,
            powerShellGetAvailable: false,
            powerShellGetVersion: null,
            powerShellGetMessage: null,
            credentialProviderDetection: new AzureArtifactsCredentialProviderDetectionResult(),
            readinessMessages: Array.Empty<string>());

        var result = service.EnsureMicrosoftArtifactRegistryRegistered(
            repositoryName: null,
            tool: RepositoryRegistrationTool.Auto,
            trusted: true,
            priority: 10,
            prerequisiteStatus: status,
            shouldProcessAction: "Register Microsoft Artifact Registry");

        Assert.Equal("MAR", result.RepositoryName);
        Assert.Equal("MicrosoftArtifactRegistry", result.Provider);
        Assert.Equal("https://mcr.microsoft.com", result.PSResourceGetUri);
        Assert.False(result.RegistrationPerformed);
        Assert.True(result.InstallPSResourceReady == false);
        Assert.Equal("Register-ModuleRepository -MicrosoftArtifactRegistry", result.RecommendedBootstrapCommand);
    }

    [Fact]
    public void GetRequiredPSResourceGetVersion_UsesGeneralMinimumWhenAzureCredentialProviderIsNotRequired()
    {
        var requiredVersion = PrivateGalleryService.GetRequiredPSResourceGetVersion(
            PrivateGalleryBootstrapMode.ExistingSession,
            includeAzureArtifactsCredentialProvider: false);

        Assert.Equal("1.1.1", requiredVersion);
    }

    [Fact]
    public void GetRequiredPSResourceGetVersion_UsesExistingSessionMinimumForAzureArtifacts()
    {
        var requiredVersion = PrivateGalleryService.GetRequiredPSResourceGetVersion(
            PrivateGalleryBootstrapMode.ExistingSession,
            includeAzureArtifactsCredentialProvider: true);

        Assert.Equal("1.2.0", requiredVersion);
    }

    [Fact]
    public void ResolveCredential_JFrogCliModeDoesNotCollectCredential()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost());

        var result = service.ResolveCredential(
            repositoryName: "Company",
            bootstrapMode: PrivateGalleryBootstrapMode.JFrogCli,
            credentialUserName: null,
            credentialSecret: null,
            credentialSecretFilePath: null,
            promptForCredential: false,
            provider: PrivateGalleryProvider.JFrog);

        Assert.Null(result.Credential);
        Assert.Equal(PrivateGalleryBootstrapMode.JFrogCli, result.BootstrapModeUsed);
        Assert.Equal(PrivateGalleryCredentialSource.JFrogCli, result.CredentialSource);
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.AzureArtifacts)]
    [InlineData(PrivateGalleryProvider.NuGet)]
    public void ResolveCredential_RejectsJFrogCliModeForNonJFrogProviders(PrivateGalleryProvider provider)
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost());

        var ex = Assert.Throws<ArgumentException>(() => service.ResolveCredential(
            repositoryName: "Company",
            bootstrapMode: PrivateGalleryBootstrapMode.JFrogCli,
            credentialUserName: null,
            credentialSecret: null,
            credentialSecretFilePath: null,
            promptForCredential: false,
            provider: provider));

        Assert.Contains("JFrog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegistrationResult_JFrogCliModeDoesNotTreatPowerShellGetAsReady()
    {
        var result = new ModuleRepositoryRegistrationResult
        {
            Provider = "JFrog",
            BootstrapModeUsed = PrivateGalleryBootstrapMode.JFrogCli,
            PowerShellGetRegistered = true,
            PSResourceGetRegistered = false
        };

        Assert.False(result.InstallModuleReady);
        Assert.Empty(result.ReadyCommands);
        Assert.Equal(string.Empty, result.PreferredInstallCommand);
    }

    [Fact]
    public void RegistrationResult_PrivateGalleryBootstrapCommandIncludesPowerShellGetUris()
    {
        var result = new ModuleRepositoryRegistrationResult
        {
            Provider = "JFrog",
            AzureArtifactsFeed = "powershell-virtual",
            RepositoryName = "JFrogCompany",
            PSResourceGetUri = "https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json",
            PowerShellGetSourceUri = "https://company.jfrog.io/artifactory/api/nuget/powershell-virtual",
            PowerShellGetPublishUri = "https://company.jfrog.io/artifactory/api/nuget/powershell-virtual",
            PSResourceGetAvailable = true,
            PSResourceGetMeetsMinimumVersion = true,
            BootstrapModeRequested = PrivateGalleryBootstrapMode.CredentialPrompt,
            BootstrapModeUsed = PrivateGalleryBootstrapMode.CredentialPrompt
        };

        Assert.Contains("-RepositoryUri 'https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json'", result.RecommendedBootstrapCommand, StringComparison.Ordinal);
        Assert.Contains("-RepositorySourceUri 'https://company.jfrog.io/artifactory/api/nuget/powershell-virtual'", result.RecommendedBootstrapCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-RepositoryPublishUri", result.RecommendedBootstrapCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveCredential_AutoUsesCredentialPromptForNonAzureProviders()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost());
        var status = new BootstrapPrerequisiteStatus(
            psResourceGetAvailable: true,
            psResourceGetVersion: "1.2.0",
            psResourceGetMeetsMinimumVersion: true,
            psResourceGetSupportsExistingSessionBootstrap: true,
            psResourceGetMessage: null,
            powerShellGetAvailable: true,
            powerShellGetVersion: "2.2.5",
            powerShellGetMessage: null,
            credentialProviderDetection: new AzureArtifactsCredentialProviderDetectionResult
            {
                IsDetected = true,
                Paths = new[] { "CredentialProvider.Microsoft.dll" }
            },
            readinessMessages: Array.Empty<string>());

        var result = service.ResolveCredential(
            repositoryName: "Company",
            bootstrapMode: PrivateGalleryBootstrapMode.Auto,
            credentialUserName: null,
            credentialSecret: null,
            credentialSecretFilePath: null,
            promptForCredential: false,
            prerequisiteStatus: status,
            allowInteractivePrompt: false,
            provider: PrivateGalleryProvider.JFrog);

        Assert.Equal(PrivateGalleryBootstrapMode.CredentialPrompt, result.BootstrapModeUsed);
        Assert.Equal(PrivateGalleryCredentialSource.None, result.CredentialSource);
    }

    [Fact]
    public void ResolveCredential_RejectsExplicitCredentialWithJFrogCliMode()
    {
        var service = new PrivateGalleryService(new FakePrivateGalleryHost());

        var ex = Assert.Throws<ArgumentException>(() => service.ResolveCredential(
            repositoryName: "Company",
            bootstrapMode: PrivateGalleryBootstrapMode.JFrogCli,
            credentialUserName: "user@example.com",
            credentialSecret: "secret",
            credentialSecretFilePath: null,
            promptForCredential: false));

        Assert.Contains("BootstrapMode JFrogCli", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePrivateGalleryHost : IPrivateGalleryHost
    {
        private readonly bool _shouldProcess;

        public FakePrivateGalleryHost(bool shouldProcess = true)
        {
            _shouldProcess = shouldProcess;
        }

        public bool ShouldProcess(string target, string action) => _shouldProcess;

        public bool IsWhatIfRequested => false;

        public RepositoryCredential? PromptForCredential(string caption, string message) => null;

        public void WriteVerbose(string message)
        {
        }

        public void WriteWarning(string message)
        {
        }
    }

    private static string CreateCredentialProviderFile(string root, string runtime)
    {
        var path = Path.Combine(root, "plugins", runtime, "CredentialProvider.Microsoft", "CredentialProvider.Microsoft.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Func<ProcessRunRequest, ProcessRunResult> _execute;

        public StubProcessRunner(Func<ProcessRunRequest, ProcessRunResult> execute)
        {
            _execute = execute;
        }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(_execute(request));
    }
}

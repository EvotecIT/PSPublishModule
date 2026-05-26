using System;
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
            promptForCredential: false);

        Assert.Null(result.Credential);
        Assert.Equal(PrivateGalleryBootstrapMode.JFrogCli, result.BootstrapModeUsed);
        Assert.Equal(PrivateGalleryCredentialSource.JFrogCli, result.CredentialSource);
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
}

using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateGalleryPrerequisiteVersionPolicyTests
{
    [Theory]
    [InlineData("1.1.1", "1.1.1", true)]
    [InlineData("1.2.0", "1.1.1", true)]
    [InlineData("1.1.1-preview1", "1.1.1", false)]
    [InlineData("1.2.0-preview2", "1.2.0-preview5", false)]
    [InlineData("1.2.0-preview5", "1.2.0-preview5", true)]
    [InlineData("1.2.0-preview5", "1.2.0", false)]
    [InlineData("1.2.0-preview6", "1.2.0-preview5", true)]
    [InlineData("1.2.0", "1.2.0-preview5", true)]
    [InlineData("1.0.9", "1.1.1", false)]
    [InlineData("", "1.1.1", false)]
    public void VersionMeetsMinimum_EvaluatesExpectedValues(string versionText, string minimumVersion, bool expected)
    {
        var actual = PrivateGalleryVersionPolicy.VersionMeetsMinimum(versionText, minimumVersion);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ShouldInstallPrerequisitesForBootstrap_RecommendsUpgradeForExistingSessionWhenPSResourceGetIsTooOld()
    {
        var status = CreateStatus(
            psResourceGetVersion: "1.1.1",
            psResourceGetMeetsMinimumVersion: true,
            psResourceGetSupportsExistingSessionBootstrap: false,
            credentialProviderDetected: true);

        Assert.True(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.ExistingSession));
        Assert.False(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.Auto));
        Assert.False(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.CredentialPrompt));
        Assert.True(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.ExistingSession, RepositoryRegistrationTool.PSResourceGet));
        Assert.False(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.Auto, RepositoryRegistrationTool.PSResourceGet));
        Assert.False(PrivateGalleryVersionPolicy.ShouldInstallPrerequisitesForBootstrap(status, PrivateGalleryBootstrapMode.Auto, RepositoryRegistrationTool.PowerShellGet));
    }

    [Fact]
    public void IsBootstrapModeReady_HonorsRequestedBootstrapMode()
    {
        var status = CreateStatus(
            psResourceGetVersion: "1.1.1",
            psResourceGetMeetsMinimumVersion: true,
            psResourceGetSupportsExistingSessionBootstrap: false,
            credentialProviderDetected: true);

        Assert.False(PrivateGalleryVersionPolicy.IsBootstrapModeReady(status, PrivateGalleryBootstrapMode.ExistingSession));
        Assert.True(PrivateGalleryVersionPolicy.IsBootstrapModeReady(status, PrivateGalleryBootstrapMode.CredentialPrompt));
        Assert.True(PrivateGalleryVersionPolicy.IsBootstrapModeReady(status, PrivateGalleryBootstrapMode.Auto));
    }

    private static BootstrapPrerequisiteStatus CreateStatus(
        string psResourceGetVersion,
        bool psResourceGetMeetsMinimumVersion,
        bool psResourceGetSupportsExistingSessionBootstrap,
        bool credentialProviderDetected)
    {
        return new BootstrapPrerequisiteStatus(
            psResourceGetAvailable: true,
            psResourceGetVersion: psResourceGetVersion,
            psResourceGetMeetsMinimumVersion: psResourceGetMeetsMinimumVersion,
            psResourceGetSupportsExistingSessionBootstrap: psResourceGetSupportsExistingSessionBootstrap,
            psResourceGetMessage: null,
            powerShellGetAvailable: true,
            powerShellGetVersion: "2.2.5",
            powerShellGetMessage: null,
            credentialProviderDetection: new AzureArtifactsCredentialProviderDetectionResult
            {
                IsDetected = credentialProviderDetected,
                Paths = credentialProviderDetected ? new[] { "CredentialProvider.Microsoft.dll" } : Array.Empty<string>(),
                Version = credentialProviderDetected ? "2.0.0" : null
            },
            readinessMessages: Array.Empty<string>());
    }
}

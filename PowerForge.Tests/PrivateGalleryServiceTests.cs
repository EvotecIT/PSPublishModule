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

    private sealed class FakePrivateGalleryHost : IPrivateGalleryHost
    {
        public bool ShouldProcess(string target, string action) => true;

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

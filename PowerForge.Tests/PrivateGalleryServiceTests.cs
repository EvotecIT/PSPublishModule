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

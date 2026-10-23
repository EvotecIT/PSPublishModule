using PowerForge;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class RepositoryCredentialResolverTests
{
    [Fact]
    public void Configuration_compilation_defers_missing_repository_secret_sources()
    {
        var variable = "POWERFORGE_MISSING_" + Guid.NewGuid().ToString("N");
        foreach (var fromFile in new[] { false, true })
        {
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "secret.txt");
            var segment = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
            {
                ParameterSetName = "JFrog",
                Type = PublishDestination.PowerShellGallery,
                JFrogBaseUri = "https://company.example/artifactory",
                JFrogRepository = "modules-local",
                RepositoryName = "Company",
                Tool = PublishTool.PSResourceGet,
                RepositoryCredentialUserName = "publisher",
                RepositoryCredentialSecretFilePathSpecified = fromFile,
                RepositoryCredentialSecretFilePath = fromFile ? missingPath : null,
                RepositoryCredentialSecretEnvironmentVariableSpecified = !fromFile,
                RepositoryCredentialSecretEnvironmentVariable = fromFile ? null : variable,
                Enabled = true
            });
            var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
            Assert.Contains("company.example", repository.Uri);
            Assert.True(string.IsNullOrEmpty(repository.Credential!.Secret));
            Assert.True(RepositoryCredentialResolver.HasSecretSource(repository.Credential));
            var publisher = new RepositoryPublisher(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => publisher.ResolveCredentialForRepository(repository));
            Assert.DoesNotContain(missingPath, ex.ToString());
        }
    }

    [Fact]
    public void Resolution_reads_current_environment_without_mutating_configuration()
    {
        var name = "POWERFORGE_SECRET_" + Guid.NewGuid().ToString("N");
        var credential = new RepositoryCredential { UserName = "publisher", SecretEnvironmentVariable = name };
        try
        {
            Environment.SetEnvironmentVariable(name, "first-token");
            Assert.Equal("first-token", RepositoryCredentialResolver.Resolve(credential)!.Secret);
            Environment.SetEnvironmentVariable(name, "second-token");
            Assert.Equal("second-token", RepositoryCredentialResolver.Resolve(credential)!.Secret);
            Assert.Null(credential.Secret);
            Assert.Equal("publisher", RepositoryCredentialResolver.Resolve(credential)!.UserName);
            Assert.Equal("Environment variable: " + name, RepositoryCredentialResolver.Resolve(credential)!.Source);
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    [Fact]
    public void Resolution_reads_current_file_and_rejects_bad_values_without_echoing_them()
    {
        var path = Path.GetTempFileName();
        var credential = new RepositoryCredential { UserName = "publisher", SecretFilePath = path };
        try
        {
            File.WriteAllText(path, " first-token \n");
            Assert.Equal("first-token", RepositoryCredentialResolver.Resolve(credential)!.Secret);
            Assert.Equal("Secret file: " + path, RepositoryCredentialResolver.Resolve(credential)!.Source);
            foreach (var invalid in new[] { " ", "sensitive-marker\nsecond-line" })
            {
                File.WriteAllText(path, invalid);
                var ex = Assert.Throws<InvalidOperationException>(() => RepositoryCredentialResolver.Resolve(credential));
                Assert.DoesNotContain("sensitive-marker", ex.ToString());
                Assert.Null(ex.InnerException);
            }
        }
        finally { File.Delete(path); }
    }
}

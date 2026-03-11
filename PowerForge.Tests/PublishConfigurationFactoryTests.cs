using PowerForge;

namespace PowerForge.Tests;

public sealed class PublishConfigurationFactoryTests
{
    [Fact]
    public void Create_reads_publish_api_key_from_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, " api-key ");
        try
        {
            var factory = new PublishConfigurationFactory();

            var segment = factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "ApiFromFile",
                Type = PublishDestination.PowerShellGallery,
                FilePath = path,
                Enabled = true
            });

            Assert.Equal("api-key", segment.Configuration.ApiKey);
            Assert.Equal(PublishDestination.PowerShellGallery, segment.Configuration.Destination);
            Assert.True(segment.Configuration.Enabled);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Create_throws_when_github_username_missing()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.GitHub,
            ApiKey = "token"
        }));

        Assert.Contains("UserName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_builds_azure_artifacts_repository_configuration()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "AzureArtifacts",
            AzureDevOpsOrganization = "contoso",
            AzureDevOpsProject = "Platform",
            AzureArtifactsFeed = "Modules",
            RepositoryCredentialUserName = "user@contoso.com",
            RepositoryCredentialSecret = "pat",
            RepositoryCredentialSecretSpecified = true,
            Enabled = true
        });

        Assert.Equal(PublishDestination.PowerShellGallery, segment.Configuration.Destination);
        Assert.Equal("AzureDevOps", segment.Configuration.ApiKey);
        Assert.Equal("Modules", segment.Configuration.RepositoryName);

        var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
        Assert.Equal("Modules", repository.Name);
        Assert.Equal(RepositoryApiVersion.V3, repository.ApiVersion);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json", repository.Uri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", repository.SourceUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", repository.PublishUri);

        var credential = Assert.IsType<RepositoryCredential>(repository.Credential);
        Assert.Equal("user@contoso.com", credential.UserName);
        Assert.Equal("pat", credential.Secret);
    }

    [Fact]
    public void Create_throws_when_repository_secret_has_no_username()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "token",
            RepositoryCredentialSecret = "secret",
            RepositoryCredentialSecretSpecified = true
        }));

        Assert.Contains("RepositoryCredentialUserName", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_when_custom_repository_uses_psgallery_name()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "token",
            RepositoryName = "PSGallery",
            RepositoryUri = "https://example.test/v3/index.json"
        }));

        Assert.Contains("RepositoryName cannot be 'PSGallery'", ex.Message, StringComparison.Ordinal);
    }
}

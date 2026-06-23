using PowerForge;

namespace PowerForge.Tests;

public sealed class PublishConfigurationFactoryTests
{
    [Fact]
    public void Create_reads_publish_api_key_from_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, " api-key " + Environment.NewLine);
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
    public void Create_defers_disabled_publish_api_key_file_until_runtime()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiFromFile",
            Type = PublishDestination.PowerShellGallery,
            FilePath = missingPath,
            FilePathSpecified = true,
            Enabled = false
        });

        Assert.Equal(string.Empty, segment.Configuration.ApiKey);
        Assert.Equal(missingPath, segment.Configuration.ApiKeyFilePath);
        Assert.False(segment.Configuration.Enabled);
    }

    [Fact]
    public void Create_rejects_enabled_file_publish_without_file_path()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiFromFile",
            Type = PublishDestination.PowerShellGallery,
            Enabled = true
        }));

        Assert.Contains("FilePath", ex.Message, StringComparison.Ordinal);
        Assert.Contains("file-based publish", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_multiline_publish_api_key_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, "Write-Host 'not a key'" + Environment.NewLine + "Write-Host 'still not a key'");
        try
        {
            var factory = new PublishConfigurationFactory();

            var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "ApiFromFile",
                Type = PublishDestination.PowerShellGallery,
                FilePath = path,
                Enabled = true
            }));

            Assert.Contains("multi-line secret", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("single line", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not a script", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Theory]
    [InlineData("ApiKey")]
    [InlineData("JFrog")]
    public void Create_rejects_multiline_inline_publish_api_key(string parameterSetName)
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = parameterSetName,
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "line-one" + Environment.NewLine + "line-two",
            Enabled = true
        }));

        Assert.Contains("multi-line secret", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single line", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_multiline_repository_credential_secret_file()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "line-one" + Environment.NewLine + "line-two");
        try
        {
            var factory = new PublishConfigurationFactory();

            var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "JFrog",
                Type = PublishDestination.PowerShellGallery,
                RepositoryName = "JFrogPS",
                Tool = PublishTool.PSResourceGet,
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                JFrogRepository = "powershell-virtual",
                RepositoryCredentialUserName = "name@company.com",
                RepositoryCredentialSecretFilePath = path,
                RepositoryCredentialSecretFilePathSpecified = true,
                Enabled = true
            }));

            Assert.Contains("multi-line secret", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("single line", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Create_builds_jfrog_repository_configuration_from_shortcut()
    {
        var secretPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(secretPath, " pat ");
        try
        {
            var factory = new PublishConfigurationFactory();

            var segment = factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "ApiFromFile",
                Type = PublishDestination.PowerShellGallery,
                FilePath = secretPath,
                RepositoryName = "JFrogPS",
                Tool = PublishTool.PSResourceGet,
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                JFrogRepository = "powershell-virtual",
                RepositoryCredentialUserName = "name@company.com",
                RepositoryCredentialSecretFilePath = secretPath,
                RepositoryCredentialSecretFilePathSpecified = true,
                Enabled = true
            });

            Assert.Equal("pat", segment.Configuration.ApiKey);
            Assert.Equal("JFrogPS", segment.Configuration.RepositoryName);

            var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
            Assert.Equal("JFrogPS", repository.Name);
            Assert.Equal(RepositoryApiVersion.V3, repository.ApiVersion);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json", repository.Uri);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", repository.SourceUri);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", repository.PublishUri);

            var credential = Assert.IsType<RepositoryCredential>(repository.Credential);
            Assert.Equal("name@company.com", credential.UserName);
            Assert.Equal("pat", credential.Secret);
        }
        finally
        {
            if (File.Exists(secretPath))
                File.Delete(secretPath);
        }
    }

    [Fact]
    public void Create_builds_jfrog_repository_configuration_without_api_key()
    {
        var secretPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(secretPath, " pat ");
        try
        {
            var factory = new PublishConfigurationFactory();

            var segment = factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "JFrog",
                Type = PublishDestination.PowerShellGallery,
                RepositoryName = "JFrogPS",
                Tool = PublishTool.PSResourceGet,
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                JFrogRepository = "powershell-virtual",
                RepositoryCredentialUserName = "name@company.com",
                RepositoryCredentialSecretFilePath = secretPath,
                RepositoryCredentialSecretFilePathSpecified = true,
                Enabled = true
            });

            Assert.Equal(string.Empty, segment.Configuration.ApiKey);
            Assert.Equal("JFrogPS", segment.Configuration.RepositoryName);

            var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json", repository.Uri);

            var credential = Assert.IsType<RepositoryCredential>(repository.Credential);
            Assert.Equal("name@company.com", credential.UserName);
            Assert.Equal("pat", credential.Secret);
        }
        finally
        {
            if (File.Exists(secretPath))
                File.Delete(secretPath);
        }
    }

    [Fact]
    public void Create_reads_repository_credential_secret_from_environment()
    {
        var envName = "POWERFORGE_TEST_JFROG_PAT_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(envName, " env-pat ");
            var factory = new PublishConfigurationFactory();

            var segment = factory.Create(new PublishConfigurationRequest
            {
                ParameterSetName = "JFrog",
                Type = PublishDestination.PowerShellGallery,
                RepositoryName = "JFrogPS",
                Tool = PublishTool.PSResourceGet,
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                JFrogRepository = "powershell-virtual",
                RepositoryCredentialUserName = "name@company.com",
                RepositoryCredentialSecretEnvironmentVariable = envName,
                RepositoryCredentialSecretEnvironmentVariableSpecified = true,
                Enabled = true
            });

            var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
            var credential = Assert.IsType<RepositoryCredential>(repository.Credential);
            Assert.Equal("name@company.com", credential.UserName);
            Assert.Equal("env-pat", credential.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, null);
        }
    }

    [Fact]
    public void Create_builds_jfrog_oidc_runtime_credential_provider()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "JFrog",
            Type = PublishDestination.PowerShellGallery,
            RepositoryName = "JFrogPS",
            Tool = PublishTool.PSResourceGet,
            JFrogBaseUri = "https://company.jfrog.io/artifactory",
            JFrogRepository = "powershell-virtual",
            JFrogOidcProvider = "azure-oidc",
            JFrogOidcProviderType = JFrogOidcProviderType.Azure,
            JFrogOidcTokenIdEnvironmentVariable = "JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID",
            Enabled = true
        });

        var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
        Assert.Null(repository.Credential);
        var provider = Assert.IsType<RepositoryCredentialProviderConfiguration>(repository.CredentialProvider);
        Assert.Equal(RepositoryCredentialProviderKind.JFrogOidc, provider.Kind);
        Assert.Equal("https://company.jfrog.io/", provider.JFrogPlatformUri);
        Assert.Equal("azure-oidc", provider.JFrogOidcProvider);
        Assert.Equal("JFROG_CLI_OIDC_EXCHANGE_TOKEN_ID", provider.JFrogOidcTokenIdEnvironmentVariable);
        Assert.Equal(JFrogOidcProviderType.Azure, provider.JFrogOidcProviderType);
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

    [Fact]
    public void Create_allows_container_registry_repository_when_not_publishing()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "unused",
            RepositoryName = "CompanyAcr",
            RepositoryUri = "https://contoso.azurecr.io",
            RepositoryApiVersion = RepositoryApiVersion.ContainerRegistry,
            Enabled = false,
            UseAsDependencyVersionSource = true
        });

        var repository = Assert.IsType<PublishRepositoryConfiguration>(segment.Configuration.Repository);
        Assert.Equal(RepositoryApiVersion.ContainerRegistry, repository.ApiVersion);
        Assert.Equal("https://contoso.azurecr.io", repository.Uri);
        Assert.True(segment.Configuration.UseAsDependencyVersionSource);
    }

    [Fact]
    public void Create_rejects_enabled_mar_publish_repository()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "unused",
            RepositoryName = "MAR",
            RepositoryUri = "https://mcr.microsoft.com",
            RepositoryApiVersion = RepositoryApiVersion.ContainerRegistry,
            Enabled = true
        }));

        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_allows_repository_named_mar_without_verified_mar_uri()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "unused",
            RepositoryName = "MAR",
            Enabled = true
        });

        Assert.Equal("MAR", segment.Configuration.RepositoryName);
        Assert.Null(segment.Configuration.Repository);
        Assert.True(segment.Configuration.Enabled);
    }

    [Fact]
    public void Create_rejects_enabled_mar_publish_by_repository_uri_with_custom_name()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "unused",
            RepositoryName = "MicrosoftPackages",
            RepositoryUri = "https://mcr.microsoft.com/",
            RepositoryApiVersion = RepositoryApiVersion.ContainerRegistry,
            Enabled = true
        }));

        Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_allows_github_publish_repository_named_mar()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.GitHub,
            ApiKey = "token",
            UserName = "EvotecIT",
            RepositoryName = "MAR",
            Enabled = true
        });

        Assert.Equal(PublishDestination.GitHub, segment.Configuration.Destination);
        Assert.Equal("MAR", segment.Configuration.RepositoryName);
        Assert.True(segment.Configuration.Enabled);
    }

    [Fact]
    public void Create_rejects_container_registry_api_version_for_azure_artifacts()
    {
        var factory = new PublishConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "AzureArtifacts",
            AzureDevOpsOrganization = "contoso",
            AzureDevOpsProject = "Platform",
            AzureArtifactsFeed = "Modules",
            RepositoryApiVersion = RepositoryApiVersion.ContainerRegistry,
            Enabled = true
        }));

        Assert.Contains("ContainerRegistry", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Azure Artifacts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_sets_required_module_publish_source()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "token",
            RepositoryName = "CompanyGallery",
            Enabled = true,
            PublishRequiredModules = true,
            RequiredModuleSourceRepository = "InternalUpstream"
        });

        Assert.True(segment.Configuration.PublishRequiredModules);
        Assert.Equal("InternalUpstream", segment.Configuration.RequiredModuleSourceRepository);
    }

    [Fact]
    public void Create_defaults_required_module_publish_source_to_psgallery()
    {
        var factory = new PublishConfigurationFactory();

        var segment = factory.Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey",
            Type = PublishDestination.PowerShellGallery,
            ApiKey = "token",
            RepositoryName = "CompanyGallery",
            Enabled = true,
            PublishRequiredModules = true
        });

        Assert.True(segment.Configuration.PublishRequiredModules);
        Assert.Equal("PSGallery", segment.Configuration.RequiredModuleSourceRepository);
    }
}

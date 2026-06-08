using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleRepositoryProfileStoreTests
{
    [Fact]
    public void SaveProfile_NormalizesAzureArtifactsProfileWithEntraFirstDefaults()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var saved = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = " Company ",
                AzureDevOpsOrganization = " contoso ",
                AzureDevOpsProject = " Platform ",
                AzureArtifactsFeed = " Modules "
            });

            Assert.Equal("Company", saved.Name);
            Assert.Equal("contoso", saved.AzureDevOpsOrganization);
            Assert.Equal("Platform", saved.AzureDevOpsProject);
            Assert.Equal("Modules", saved.AzureArtifactsFeed);
            Assert.Equal("Modules", saved.RepositoryName);
            Assert.Equal(PrivateGalleryDefaults.AzureArtifactsRepositoryPriority, saved.Priority);
            Assert.Equal(RepositoryRegistrationTool.PSResourceGet, saved.Tool);
            Assert.Equal(PrivateGalleryBootstrapMode.ExistingSession, saved.BootstrapMode);
            Assert.Equal("AzureArtifactsCredentialProvider", saved.AuthenticationMode);
            Assert.True(File.Exists(path));

            var json = File.ReadAllText(path);
            Assert.Contains("\"Provider\": \"AzureArtifacts\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Provider\": \"Azure\"", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void SaveProfile_PreservesExplicitPriority()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var saved = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules",
                Priority = 25
            });

            Assert.Equal(25, saved.Priority);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void SaveProfile_NormalizesJFrogProfileWithCredentialPromptDefaults()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var saved = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = " Company ",
                Provider = PrivateGalleryProvider.JFrog,
                Repository = " powershell-virtual ",
                JFrogBaseUri = " https://company.jfrog.io/artifactory/ ",
                BootstrapMode = PrivateGalleryBootstrapMode.ExistingSession
            });

            Assert.Equal("Company", saved.Name);
            Assert.Equal(PrivateGalleryProvider.JFrog, saved.Provider);
            Assert.Equal(string.Empty, saved.AzureDevOpsOrganization);
            Assert.Equal("powershell-virtual", saved.AzureArtifactsFeed);
            Assert.Equal("powershell-virtual", saved.Repository);
            Assert.Equal("powershell-virtual", saved.RepositoryName);
            Assert.Equal("https://company.jfrog.io/artifactory", saved.JFrogBaseUri);
            Assert.Equal("powershell-virtual", saved.JFrogRepository);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json", saved.RepositoryUri);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", saved.RepositorySourceUri);
            Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", saved.RepositoryPublishUri);
            Assert.Equal(PrivateGalleryBootstrapMode.CredentialPrompt, saved.BootstrapMode);
            Assert.Equal("CredentialPrompt", saved.AuthenticationMode);
            Assert.Equal(PrivateGalleryDefaults.AzureArtifactsRepositoryPriority, saved.Priority);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void SaveProfile_PreservesJFrogCliBootstrapModeForJFrogProfiles()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var saved = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                Provider = PrivateGalleryProvider.JFrog,
                Repository = "powershell-virtual",
                JFrogBaseUri = "https://company.jfrog.io/artifactory",
                BootstrapMode = PrivateGalleryBootstrapMode.JFrogCli
            });

            Assert.Equal(PrivateGalleryBootstrapMode.JFrogCli, saved.BootstrapMode);
            Assert.Equal("CredentialPrompt", saved.AuthenticationMode);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void SaveProfile_NormalizesGitHubPackagesProfileWithTokenDefaults()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var saved = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = " Licensing ",
                Provider = PrivateGalleryProvider.GitHubPackages,
                GitHubOwner = " EvotecIT ",
                RepositoryName = " evotec-github "
            });

            Assert.Equal("Licensing", saved.Name);
            Assert.Equal(PrivateGalleryProvider.GitHubPackages, saved.Provider);
            Assert.Equal("EvotecIT", saved.GitHubOwner);
            Assert.Equal("EvotecIT", saved.Repository);
            Assert.Equal("evotec-github", saved.RepositoryName);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", saved.RepositoryUri);
            Assert.Equal(saved.RepositoryUri, saved.RepositorySourceUri);
            Assert.Equal(saved.RepositoryUri, saved.RepositoryPublishUri);
            Assert.Equal(PrivateGalleryBootstrapMode.CredentialPrompt, saved.BootstrapMode);
            Assert.Equal("CredentialPrompt", saved.AuthenticationMode);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void GetProfile_ReturnsCaseInsensitiveProfile()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);
            store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules"
            });

            var profile = store.GetProfile("company");

            Assert.NotNull(profile);
            Assert.Equal("Company", profile!.Name);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void RemoveProfile_RemovesProfileByName()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);
            store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules"
            });

            Assert.True(store.RemoveProfile("Company"));
            Assert.Null(store.GetProfile("Company"));
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void WriteAndReadProfilesFile_RoundTripsNormalizedProfiles()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);
            var profile = store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureDevOpsProject = "Platform",
                AzureArtifactsFeed = "Modules",
                RepositoryName = "CompanyModules"
            });
            var exportPath = Path.Combine(Path.GetDirectoryName(path)!, "export.json");

            store.WriteProfilesFile(exportPath, new[] { profile });
            var imported = ModuleRepositoryProfileStore.ReadProfilesFile(exportPath);

            Assert.Single(imported);
            Assert.Equal("Company", imported[0].Name);
            Assert.Equal("contoso", imported[0].AzureDevOpsOrganization);
            Assert.Equal("Platform", imported[0].AzureDevOpsProject);
            Assert.Equal("Modules", imported[0].AzureArtifactsFeed);
            Assert.Equal("CompanyModules", imported[0].RepositoryName);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void GetProfiles_RecoversFromMalformedStoreJson()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, "{ malformed");
            var store = new ModuleRepositoryProfileStore(path);

            var profiles = store.GetProfiles();

            Assert.Empty(profiles);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void ReadProfilesFile_HandlesNullProfilesArray()
    {
        var path = CreateTempFilePath();
        try
        {
            File.WriteAllText(path, """{"version":1,"profiles":null}""");

            var profiles = ModuleRepositoryProfileStore.ReadProfilesFile(path);

            Assert.Empty(profiles);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void ImportProfiles_RejectsExistingProfileWithoutOverwrite()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);
            store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules"
            });

            var ex = Assert.Throws<InvalidOperationException>(() => store.ImportProfiles(
                new[]
                {
                    new ModuleRepositoryProfile
                    {
                        Name = "Company",
                        AzureDevOpsOrganization = "contoso",
                        AzureArtifactsFeed = "Modules2"
                    }
                },
                overwrite: false));

            Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void ImportProfiles_OverwritesExistingProfileWhenRequested()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);
            store.SaveProfile(new ModuleRepositoryProfile
            {
                Name = "Company",
                AzureDevOpsOrganization = "contoso",
                AzureArtifactsFeed = "Modules"
            });

            var imported = store.ImportProfiles(
                new[]
                {
                    new ModuleRepositoryProfile
                    {
                        Name = "Company",
                        AzureDevOpsOrganization = "contoso",
                        AzureArtifactsFeed = "Modules2"
                    }
                },
                overwrite: true);

            Assert.Single(imported);
            var profile = store.GetProfile("Company");
            Assert.NotNull(profile);
            Assert.Equal("Modules2", profile!.AzureArtifactsFeed);
            Assert.Equal("Modules2", profile.RepositoryName);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void ImportProfiles_ReturnsUniqueProfilesWhenOverwriteInputContainsDuplicates()
    {
        var path = CreateTempFilePath();
        try
        {
            var store = new ModuleRepositoryProfileStore(path);

            var imported = store.ImportProfiles(
                new[]
                {
                    new ModuleRepositoryProfile
                    {
                        Name = "Company",
                        AzureDevOpsOrganization = "contoso",
                        AzureArtifactsFeed = "Modules"
                    },
                    new ModuleRepositoryProfile
                    {
                        Name = "company",
                        AzureDevOpsOrganization = "contoso",
                        AzureArtifactsFeed = "Modules2"
                    }
                },
                overwrite: true);

            Assert.Single(imported);
            Assert.Single(store.GetProfiles());
            Assert.Equal("Modules2", store.GetProfile("Company")!.AzureArtifactsFeed);
        }
        finally
        {
            TryDelete(Path.GetDirectoryName(path));
        }
    }

    [Fact]
    public void GetStores_AllReturnsUserThenMachineStores()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var userPath = Path.Combine(root, "user", "profiles.json");
        var machinePath = Path.Combine(root, "machine", "profiles.json");
        var previousUserPath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH");
        var previousMachinePath = Environment.GetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH");

        try
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", userPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", machinePath);

            var stores = ModuleRepositoryProfileStore.GetStores(ModuleRepositoryProfileScope.All);

            Assert.Collection(
                stores,
                user =>
                {
                    Assert.Equal(ModuleRepositoryProfileScope.User, user.Scope);
                    Assert.Equal(Path.GetFullPath(userPath), user.Path);
                },
                machine =>
                {
                    Assert.Equal(ModuleRepositoryProfileScope.Machine, machine.Scope);
                    Assert.Equal(Path.GetFullPath(machinePath), machine.Path);
                });
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH", previousUserPath);
            Environment.SetEnvironmentVariable("POWERFORGE_MODULE_REPOSITORY_MACHINE_PROFILE_PATH", previousMachinePath);
            TryDelete(root);
        }
    }

    private static string CreateTempFilePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "profiles.json");
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}

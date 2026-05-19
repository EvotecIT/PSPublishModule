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
            Assert.Equal(RepositoryRegistrationTool.PSResourceGet, saved.Tool);
            Assert.Equal(PrivateGalleryBootstrapMode.ExistingSession, saved.BootstrapMode);
            Assert.Equal("AzureArtifactsCredentialProvider", saved.AuthenticationMode);
            Assert.True(File.Exists(path));
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

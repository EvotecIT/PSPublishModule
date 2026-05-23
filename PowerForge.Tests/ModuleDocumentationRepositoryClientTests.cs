using System;
using System.IO;
using System.Text.Json.Nodes;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public class ModuleDocumentationRepositoryClientTests
{
    [Fact]
    public void DocumentationPlanner_Resolves_Environment_Token_For_Repository_Host()
    {
        var previousPgGitHub = Environment.GetEnvironmentVariable("PG_GITHUB_TOKEN");
        var previousGitHub = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var previousPgAzdo = Environment.GetEnvironmentVariable("PG_AZDO_PAT");
        var previousAzdo = Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT");

        try
        {
            Environment.SetEnvironmentVariable("PG_GITHUB_TOKEN", null);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "github-token");
            Environment.SetEnvironmentVariable("PG_AZDO_PAT", null);
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT", "azdo-token");

            Assert.Equal("github-token", DocumentationPlanner.ResolveTokenForTesting(RepoHost.GitHub, null));
            Assert.Equal("azdo-token", DocumentationPlanner.ResolveTokenForTesting(RepoHost.AzureDevOps, null));
            Assert.Equal("explicit-token", DocumentationPlanner.ResolveTokenForTesting(RepoHost.AzureDevOps, "explicit-token"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PG_GITHUB_TOKEN", previousPgGitHub);
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previousGitHub);
            Environment.SetEnvironmentVariable("PG_AZDO_PAT", previousPgAzdo);
            Environment.SetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT", previousAzdo);
        }
    }

    [Fact]
    public void GitHubRepository_Encodes_Path_Segments_Without_Escaping_Separators()
    {
        var encoded = GitHubRepository.EncodeContentPathForTesting(@"docs/en US/guide#.md");

        Assert.Equal("docs/en%20US/guide%23.md", encoded);
    }

    [Fact]
    public void TokenStore_Save_And_Clear_Preserve_Unrelated_Settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "PGTokenStore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        File.WriteAllText(settingsPath, """
        {
          "Version": 7,
          "Theme": "Dark",
          "Nested": {
            "Enabled": true
          },
          "GitHub": "old",
          "AzureDevOps": "old"
        }
        """);

        try
        {
            TokenStore.SettingsPathOverride = settingsPath;

            TokenStore.Save("gh-token", null);
            Assert.Equal("gh-token", TokenStore.GetToken(RepoHost.GitHub));

            var afterSave = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.Equal("Dark", afterSave["Theme"]!.GetValue<string>());
            Assert.True(afterSave["Nested"]!.AsObject()["Enabled"]!.GetValue<bool>());
            Assert.Equal(7, afterSave["Version"]!.GetValue<int>());

            TokenStore.Clear();

            var afterClear = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.Equal("Dark", afterClear["Theme"]!.GetValue<string>());
            Assert.True(afterClear["Nested"]!.AsObject()["Enabled"]!.GetValue<bool>());
            Assert.False(afterClear.ContainsKey("GitHub"));
            Assert.False(afterClear.ContainsKey("AzureDevOps"));
        }
        finally
        {
            TokenStore.SettingsPathOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

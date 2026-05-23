using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public class DocumentationPlannerTests
{
    private string CreateTempModule(out string internals)
    {
        var root = Path.Combine(Path.GetTempPath(), "PGTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "README.md"), "# Readme\nHello");
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), "# Changelog\n- v1");
        File.WriteAllText(Path.Combine(root, "LICENSE"), "License");
        internals = Path.Combine(root, "Internals");
        Directory.CreateDirectory(internals);
        return root;
    }

    [Fact]
    public void DefaultSelection_Includes_Readme_Changelog_License()
    {
        var root = CreateTempModule(out var internals);
        var finder = new DocumentationFinder();
        var planner = new DocumentationPlanner(finder);

        var req = new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            PreferInternals = false,
            TitleName = "TestMod",
            TitleVersion = "1.0.0"
        };
        var res = planner.Execute(req);
        Assert.True(res.Items.Count >= 3);
        Assert.Contains(res.Items, i => i.Title.Contains("Readme", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Items, i => i.Title.Contains("Changelog", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(res.Items, i => i.Title.Contains("License", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_RemoteRepositoryPaths_Classifies_TemplateDocs_As_SourceDocs()
    {
        var root = CreateTempModule(out var internals);
        var finder = new DocumentationFinder();
        var planner = new DocumentationPlanner(finder);
        var client = new FakeRepoClient();

        client.Files["docs"] = new List<(string Name, string Path)>
        {
            ("2022-03-20.md", "docs/2022-03-20.md"),
            ("Use-Git.md", "docs/Use-Git.md")
        };

        client.Content["docs/2022-03-20.md"] = """
        ---
        permalink: /2022/03/20/
        ---

        {% for post in site.posts %}
        * [{{ post.title }}]({{ post.url }})
        {% endfor %}
        """;
        client.Content["docs/Use-Git.md"] = """
        ## Use-Git

        [Out-Git](Out-Git.md)
        """;

        var req = new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            Online = true,
            RepositoryPaths = new[] { "docs" }
        };

        var res = planner.Execute(req, client);

        var templateDoc = Assert.Single(res.Items, i => string.Equals(i.FileName, "2022-03-20.md", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("DOCSOURCE", templateDoc.Kind);
        Assert.Contains("~~~markdown", templateDoc.Content, StringComparison.Ordinal);
        Assert.Contains("{% for post in site.posts %}", templateDoc.Content, StringComparison.Ordinal);

        var normalDoc = Assert.Single(res.Items, i => string.Equals(i.FileName, "Use-Git.md", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("DOC", normalDoc.Kind);
    }

    [Fact]
    public void Execute_LocalReadme_Rewrites_Document_Links_To_Blob_And_Assets_To_Raw()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "README.md"), """
        [Guide](docs/Use-Git.md)
        ![Logo](assets/ugit.svg)
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit"
        });

        var readme = Assert.Single(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/main/docs/Use-Git.md", readme.Content, StringComparison.Ordinal);
        Assert.Contains("https://raw.githubusercontent.com/StartAutomating/ugit/main/assets/ugit.svg", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_LocalReadme_Uses_Resolved_Default_Branch_For_Link_Rewrites()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "README.md"), """
        [Guide](docs/Use-Git.md)
        ![Logo](assets/ugit.svg)
        """);

        var client = new FakeRepoClient { DefaultBranch = "develop" };
        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit",
            Online = true
        }, client);

        var readme = Assert.Single(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/develop/docs/Use-Git.md", readme.Content, StringComparison.Ordinal);
        Assert.Contains("https://raw.githubusercontent.com/StartAutomating/ugit/develop/assets/ugit.svg", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_LocalReadme_Rewrites_AzureDevOps_Document_Links()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "README.md"), """
        [Guide](docs/Use-Git.md)
        ![Logo](assets/ugit.svg)
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://dev.azure.com/contoso/Platform/_git/DocsRepo",
            RepositoryBranch = "develop"
        });

        var readme = Assert.Single(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://dev.azure.com/contoso/Platform/_git/DocsRepo?version=GBdevelop&path=%2Fdocs%2FUse-Git.md", readme.Content, StringComparison.Ordinal);
        Assert.Contains("https://dev.azure.com/contoso/Platform/_apis/git/repositories/DocsRepo/items?api-version=7.1&versionDescriptor.version=develop&path=%2Fassets%2Fugit.svg", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_OnlineAll_Adds_RemoteStandardDocs_Only_Once()
    {
        var root = CreateTempModule(out var internals);
        var client = new FakeRepoClient();
        client.Content["README.md"] = "# Remote readme";
        client.Content["CHANGELOG.md"] = "# Remote changelog";
        client.Content["LICENSE"] = "Remote license";

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit",
            Online = true,
            Mode = DocumentationMode.All
        }, client);

        Assert.Equal(1, res.Items.Count(i => string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase) && string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, res.Items.Count(i => string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase) && string.Equals(i.FileName, "CHANGELOG.md", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, res.Items.Count(i => string.Equals(i.Source, "Remote", StringComparison.OrdinalIgnoreCase) && string.Equals(i.FileName, "LICENSE", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Execute_ReadmeSelection_Does_Not_Add_Supplemental_Docs_Scripts_Community_Or_Releases()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), """
        # Changelog

        ## [1.2.3] - 2024-12-07
        - Release.
        """);
        File.WriteAllText(Path.Combine(root, "SECURITY.md"), "# Security");

        var scriptsRoot = Path.Combine(internals, "Scripts");
        Directory.CreateDirectory(scriptsRoot);
        File.WriteAllText(Path.Combine(scriptsRoot, "Invoke-Demo.ps1"), "Get-Date");

        var docsRoot = Path.Combine(internals, "Docs");
        Directory.CreateDirectory(docsRoot);
        File.WriteAllText(Path.Combine(docsRoot, "Guide.md"), "# Guide");

        var client = new FakeRepoClient();
        client.Files["docs"] = new List<(string Name, string Path)> { ("RemoteGuide.md", "docs/RemoteGuide.md") };
        client.Content["docs/RemoteGuide.md"] = "# Remote guide";

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            Readme = true,
            LocalChangelogPath = Path.Combine(root, "CHANGELOG.md"),
            ProjectUri = "https://github.com/StartAutomating/ugit",
            RepositoryPaths = new[] { "docs" },
            Online = true
        }, client);

        Assert.Single(res.Items);
        Assert.Contains(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Items, i => string.Equals(i.Kind, "SCRIPT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Items, i => string.Equals(i.Kind, "DOC", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Items, i => string.Equals(i.Kind, "COMMUNITY", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Items, i => string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_FileSelection_Loads_Remote_Single_File()
    {
        var root = CreateTempModule(out var internals);
        var client = new FakeRepoClient();
        client.Content["docs/guide.md"] = """
        # Guide

        [Sibling](docs/sibling.md)
        """;

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit",
            SingleFile = "docs/guide.md",
            Online = true
        }, client);

        var item = Assert.Single(res.Items);
        Assert.Equal("Remote", item.Source);
        Assert.Equal("guide.md", item.FileName);
        Assert.Equal("docs/guide.md", item.Path);
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/main/docs/sibling.md", item.Content, StringComparison.Ordinal);
        Assert.True(res.UsedRemote);
    }

    [Fact]
    public void Execute_LocalReadme_Strips_Git_Suffix_From_ProjectUri()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "README.md"), """
        [Guide](docs/Use-Git.md)
        ![Logo](assets/ugit.svg)
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit.git"
        });

        var readme = Assert.Single(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase) && string.Equals(i.Source, "Local", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("https://github.com/StartAutomating/ugit/blob/main/docs/Use-Git.md", readme.Content, StringComparison.Ordinal);
        Assert.Contains("https://raw.githubusercontent.com/StartAutomating/ugit/main/assets/ugit.svg", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("ugit.git/", readme.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ExplicitReadmeSelection_Does_Not_Include_AboutTopics()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "about_Test.help.txt"), @"
TOPIC
    about_Test

SHORT DESCRIPTION
    Test topic.
");

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            Readme = true
        });

        Assert.Contains(res.Items, i => string.Equals(i.FileName, "README.md", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(res.Items, i => string.Equals(i.Kind, "ABOUT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_Changelog_Builds_Typed_Releases()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), """
        # Changelog

        ## [1.2.3] - 2024-12-07
        - Added support for structured releases.

        ## [1.2.2] - 2024-10-16
        - Previous release.
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit"
        });

        var releases = Assert.Single(res.Items, i => string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(releases.Releases);
        Assert.Equal(2, releases.Releases!.Count);
        Assert.Equal("1.2.3", releases.Releases[0].Tag);
        Assert.Equal("2024-12-07", releases.Releases[0].PublishedAt?.ToString("yyyy-MM-dd"));
        Assert.Equal("https://github.com/StartAutomating/ugit/releases/tag/1.2.3", releases.Releases[0].Url);
        Assert.Contains("Total releases: 2", releases.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Changelog_Infers_Prerelease_From_Tag()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), """
        # Changelog

        ## [1.2.4-preview1] - 2025-01-03
        - Preview release.
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit"
        });

        var releases = Assert.Single(res.Items, i => string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase));
        var preview = Assert.Single(releases.Releases!);
        Assert.True(preview.IsPrerelease);
        Assert.Equal("https://github.com/StartAutomating/ugit/releases/tag/1.2.4-preview1", preview.Url);
    }

    [Fact]
    public void Execute_Changelog_Merges_Repository_Release_Metadata()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), """
        # Changelog

        ## ugit 0.4.5.1:
        > Like It? <a href="https://github.com/StartAutomating/ugit" target="_blank" rel="noopener noreferrer">Star It</a>
        - fix duplicate commit issue (#334)
        """);

        var client = new FakeRepoClient();
        client.Releases.Add(new RepoRelease
        {
            Tag = "v0.4.5.1",
            Name = "ugit 0.4.5.1",
            Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.5.1",
            PublishedAt = new DateTimeOffset(2024, 12, 7, 0, 0, 0, TimeSpan.Zero)
        });

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit"
        }, client);

        var releases = Assert.Single(res.Items, i => string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase));
        var latest = Assert.Single(releases.Releases!);
        Assert.Equal("v0.4.5.1", latest.Tag);
        Assert.Equal("https://github.com/StartAutomating/ugit/releases/tag/v0.4.5.1", latest.Url);
        Assert.Equal("2024-12-07", latest.PublishedAt?.ToString("yyyy-MM-dd"));
        Assert.Contains("[#334](https://github.com/StartAutomating/ugit/issues/334)", latest.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("target=\"_blank\"", latest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"noopener noreferrer\"", latest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Changelog_Infers_VPrefixed_GitHub_Tag_For_Plain_Release_Heading()
    {
        var root = CreateTempModule(out var internals);
        File.WriteAllText(Path.Combine(root, "CHANGELOG.md"), """
        # Changelog

        ## ugit 0.4.5.1:
        - fix duplicate commit issue (#334)
        """);

        var planner = new DocumentationPlanner(new DocumentationFinder());
        var res = planner.Execute(new DocumentationPlanner.Request
        {
            RootBase = root,
            InternalsBase = internals,
            ProjectUri = "https://github.com/StartAutomating/ugit"
        });

        var releases = Assert.Single(res.Items, i => string.Equals(i.Kind, "RELEASES", StringComparison.OrdinalIgnoreCase));
        var latest = Assert.Single(releases.Releases!);
        Assert.Equal("v0.4.5.1", latest.Tag);
        Assert.Equal("https://github.com/StartAutomating/ugit/releases/tag/v0.4.5.1", latest.Url);
        Assert.DoesNotContain(':', latest.Name);
    }

    private sealed class FakeRepoClient : IRepoClient
    {
        public Dictionary<string, List<(string Name, string Path)>> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Content { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<RepoRelease> Releases { get; } = new();
        public string DefaultBranch { get; set; } = "main";

        public string GetDefaultBranch() => DefaultBranch;

        public string? GetFileContent(string path, string branch)
            => Content.TryGetValue(path, out var value) ? value : null;

        public List<(string Name, string Path)> ListFiles(string path, string branch)
            => Files.TryGetValue(path, out var value) ? value : new List<(string Name, string Path)>();

        public List<RepoRelease> ListReleases() => Releases;
    }
}

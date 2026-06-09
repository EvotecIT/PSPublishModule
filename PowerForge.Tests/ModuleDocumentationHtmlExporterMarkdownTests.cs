using System;
using System.IO;
using Xunit;

using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public class HtmlExporterMarkdownTests
{
    [Fact]
    public void PrepareMarkdown_Applies_Requested_Heading_Rules()
    {
        var module = new ModuleInfoModel
        {
            HeadingRules = DocumentationHeadingRules.H1AndH2
        };

        var prepared = HtmlExporter.PrepareMarkdownForTesting(module, """
        # Title
        Text
        ## Section
        More
        ```powershell
        # Not a heading
        ```
        """);

        Assert.Contains("# Title\n---\nText", prepared, StringComparison.Ordinal);
        Assert.Contains("## Section\n---\nMore", prepared, StringComparison.Ordinal);
        Assert.Contains("```powershell\n# Not a heading\n```", prepared, StringComparison.Ordinal);

        module.HeadingRules = DocumentationHeadingRules.H1;
        prepared = HtmlExporter.PrepareMarkdownForTesting(module, """
        # Title
        Text
        ## Section
        More
        """);

        Assert.Contains("# Title\n---\nText", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("## Section\n---\nMore", prepared, StringComparison.Ordinal);

        module.HeadingRules = DocumentationHeadingRules.None;
        prepared = HtmlExporter.PrepareMarkdownForTesting(module, "# Title\nText");
        Assert.Equal("# Title\nText", prepared);
    }

    [Fact]
    public void Export_UsesOfficeImoMarkdownProvider_ForAlternateFences()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "README",
                Kind = "FILE",
                Source = "Local",
                Content = """
                ## Getting started
                ~~~ps1
                # Install ugit from the PowerShell Gallery
                Install-Module ugit -Scope CurrentUser
                # Then import it.
                Import-Module ugit -Force -PassThru
                ~~~
                """
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.DoesNotContain("<p>~~~ps1</p>", html, StringComparison.Ordinal);
            Assert.Contains("language-powershell", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Install-Module ugit -Scope CurrentUser", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void Export_Renders_Structured_Releases_With_Release_Cards()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "Releases",
                Kind = "RELEASES",
                Releases = new()
                {
                    new RepoRelease
                    {
                        Name = "ugit 0.4.5.1",
                        Tag = "v0.4.5.1",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.5.1",
                        PublishedAt = new DateTimeOffset(2024, 12, 7, 0, 0, 0, TimeSpan.Zero),
                        Body = "- Fixed duplicate commit issue",
                        IsPrerelease = false
                    },
                    new RepoRelease
                    {
                        Name = "ugit 0.4.6-preview1",
                        Tag = "v0.4.6-preview1",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.6-preview1",
                        PublishedAt = new DateTimeOffset(2025, 1, 3, 0, 0, 0, TimeSpan.Zero),
                        Body = "- Preview notes",
                        IsPrerelease = true
                    }
                }
            }
        };

        items[0].Releases![0].Assets.Add(new RepoReleaseAsset
        {
            Name = "ugit.zip",
            DownloadUrl = "https://github.com/StartAutomating/ugit/releases/download/v0.4.5.1/ugit.zip",
            Size = 1024,
            ContentType = "application/zip"
        });

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.Contains("Release Overview", html, StringComparison.Ordinal);
            Assert.Contains("Latest Stable", html, StringComparison.Ordinal);
            Assert.Contains("Latest Preview", html, StringComparison.Ordinal);
            Assert.Contains("ugit 0.4.5.1", html, StringComparison.Ordinal);
            Assert.Contains("Fixed duplicate commit issue", html, StringComparison.Ordinal);
            Assert.Contains("Release Downloads", html, StringComparison.Ordinal);
            Assert.Contains("ugit.zip", html, StringComparison.Ordinal);
            Assert.Contains("https://github.com/StartAutomating/ugit/releases/tag/v0.4.5.1", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void Export_Sorts_Releases_By_Version_When_Published_Dates_Are_Missing()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "Releases",
                Kind = "RELEASES",
                Releases = new()
                {
                    new RepoRelease
                    {
                        Name = "ugit 0.4.9",
                        Tag = "v0.4.9",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.9",
                        Body = "- Stable release",
                        IsPrerelease = false
                    },
                    new RepoRelease
                    {
                        Name = "ugit 0.4.10",
                        Tag = "v0.4.10",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.10",
                        Body = "- Newer stable release",
                        IsPrerelease = false
                    },
                    new RepoRelease
                    {
                        Name = "ugit 0.4.10-preview1",
                        Tag = "v0.4.10-preview1",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.4.10-preview1",
                        Body = "- Preview release",
                        IsPrerelease = true
                    }
                }
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.Contains("Latest release", html, StringComparison.Ordinal);
            Assert.Contains("Latest stable", html, StringComparison.Ordinal);
            Assert.Contains("Latest preview", html, StringComparison.Ordinal);

            var latestReleaseIndex = html.IndexOf("ugit 0.4.10", StringComparison.Ordinal);
            var olderReleaseIndex = html.IndexOf("ugit 0.4.9", StringComparison.Ordinal);
            Assert.True(latestReleaseIndex >= 0, "Expected latest stable release label to be rendered.");
            Assert.True(olderReleaseIndex >= 0, "Expected older stable release label to be rendered.");
            Assert.True(latestReleaseIndex < olderReleaseIndex, "Expected version-aware ordering to place 0.4.10 ahead of 0.4.9 when publish dates are missing.");
            Assert.Contains("Latest Stable", html, StringComparison.Ordinal);
            Assert.Contains("Latest Preview", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void Export_Renders_Release_Body_Issue_Links_Without_Corrupt_Target_Attributes()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "Releases",
                Kind = "RELEASES",
                Releases = new()
                {
                    new RepoRelease
                    {
                        Name = "ugit 0.1.5",
                        Tag = "v0.1.5",
                        Url = "https://github.com/StartAutomating/ugit/releases/tag/v0.1.5",
                        Body = """
                        * Adding git.log .Checkout() and Revert() ([#27](https://github.com/StartAutomating/ugit/issues/27), [#28](https://github.com/StartAutomating/ugit/issues/28))
                        * Use-Git: Support for progress bars ([#18](https://github.com/StartAutomating/ugit/issues/18)). Warning when repo not found ([#21](https://github.com/StartAutomating/ugit/issues/21))
                        """,
                        IsPrerelease = false
                    }
                }
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.DoesNotContain("target=\"<em>blank\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("target=\"</em>blank\"", html, StringComparison.Ordinal);
            Assert.Contains("https://github.com/StartAutomating/ugit/issues/27", html, StringComparison.Ordinal);
            Assert.Contains("https://github.com/StartAutomating/ugit/issues/28", html, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    [Fact]
    public void Export_Renders_Format_Type_Content_As_Text_Code()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "ugit",
            Version = "1.0.0",
            SkipCommands = true,
            SkipDependencies = true
        };

        var items = new[]
        {
            new DocumentItem
            {
                Title = "ugit.format.ps1xml",
                Kind = "FORMAT",
                Source = "Local",
                Content = """
                ```xml
                <Configuration>
                  <ViewDefinitions />
                </Configuration>
                ```
                """
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.DoesNotContain("language-xml", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token tag", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Configuration", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ViewDefinitions", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }
}

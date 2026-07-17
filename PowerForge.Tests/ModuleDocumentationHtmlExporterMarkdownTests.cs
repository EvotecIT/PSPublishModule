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

        Assert.Contains("# Title\n\n---\n\nText", prepared, StringComparison.Ordinal);
        Assert.Contains("## Section\n\n---\n\nMore", prepared, StringComparison.Ordinal);
        Assert.Contains("```powershell\n# Not a heading\n```", prepared, StringComparison.Ordinal);

        module.HeadingRules = DocumentationHeadingRules.H1;
        prepared = HtmlExporter.PrepareMarkdownForTesting(module, """
        # Title
        Text
        ## Section
        More
        """);

        Assert.Contains("# Title\n\n---\n\nText", prepared, StringComparison.Ordinal);
        Assert.DoesNotContain("## Section\n\n---\n\nMore", prepared, StringComparison.Ordinal);

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
    public void Export_Does_Not_Trust_Raw_Html_From_Documentation_Sources()
    {
        var exporter = new HtmlExporter();
        var module = new ModuleInfoModel
        {
            Name = "SafeDocs",
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
                Source = "Repository",
                Content = """
                <details>
                <summary>More</summary>
                <script>alert('unsafe')</script>
                </details>
                """
            }
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.DoesNotContain("<details>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<summary>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script>alert('unsafe')</script>", html, StringComparison.Ordinal);
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
    public void Export_Uses_Lazy_DataTables_For_Markdown_Tables()
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
                | Name | Value |
                | --- | --- |
                | One | 1 |
                | Two | 2 |
                """
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.Contains("var lazy = true;", html, StringComparison.Ordinal);
            Assert.Contains("window.htmlForgeXWhenVisible", html, StringComparison.Ordinal);
            Assert.Contains("deferRender", html, StringComparison.Ordinal);
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
    public void Export_Adds_Module_Documentation_Markdown_Theme()
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
                # Themed Markdown

                ## Setup

                Use `Install-Module` for setup.

                ```powershell
                Install-Module ugit -Scope CurrentUser
                ```
                """
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.Contains("hfx-module-docs-markdown", html, StringComparison.Ordinal);
            Assert.Contains(".hfx-md h2", html, StringComparison.Ordinal);
            Assert.Contains(".hfx-md :not(pre) > code", html, StringComparison.Ordinal);
            Assert.Contains("overflow-wrap: anywhere", html, StringComparison.Ordinal);
            Assert.Contains("language-powershell", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("languages.powershell", html, StringComparison.Ordinal);
            Assert.Contains("Themed Markdown", html, StringComparison.Ordinal);
            Assert.Contains("Setup", html, StringComparison.Ordinal);
            Assert.DoesNotContain("cdn.jsdelivr.net", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">## Themed Markdown<", html, StringComparison.Ordinal);
            Assert.DoesNotContain("># Themed Markdown<", html, StringComparison.Ordinal);
            Assert.DoesNotContain(">## Setup<", html, StringComparison.Ordinal);
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
    public void Export_Renders_Nested_Documentation_Tabs_Vertically()
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
                Title = "One",
                Kind = "DOC",
                Source = "Local",
                FileName = "one.md",
                Content = "# One"
            },
            new DocumentItem
            {
                Title = "Two",
                Kind = "DOC",
                Source = "Local",
                FileName = "two.md",
                Content = "# Two"
            }
        };

        var outputPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");

        try
        {
            exporter.Export(module, items, outputPath, open: false);

            var html = File.ReadAllText(outputPath);
            Assert.Contains("aria-orientation=\"vertical\"", html, StringComparison.Ordinal);
            Assert.Contains("flex-md-column", html, StringComparison.Ordinal);
            Assert.Contains("max-height: 70vh", html, StringComparison.Ordinal);
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
    public void RenderHelpMarkdown_Uses_Lists_For_Inputs_And_Outputs()
    {
        var model = new CommandHelpModel
        {
            Name = "Get-Demo",
            Synopsis = "Gets demo data."
        };
        model.Inputs.Add(new TypeHelp
        {
            TypeName = "System.String",
            Description = "Pipeline input value."
        });
        model.Outputs.Add(new TypeHelp
        {
            TypeName = "Demo.Result",
            Description = "Structured output value."
        });

        var markdown = HtmlExporter.RenderHelpMarkdownForTesting(model);

        Assert.Contains("## Inputs", markdown, StringComparison.Ordinal);
        Assert.Contains("- `System.String` - Pipeline input value.", markdown, StringComparison.Ordinal);
        Assert.Contains("## Outputs", markdown, StringComparison.Ordinal);
        Assert.Contains("- `Demo.Result` - Structured output value.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Type | Description |", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderHelpMarkdown_Uses_Valid_CodeSpan_For_Generic_Type_Names()
    {
        var model = new CommandHelpModel
        {
            Name = "Get-Demo",
            Synopsis = "Gets demo data."
        };
        model.Outputs.Add(new TypeHelp
        {
            TypeName = "System.Collections.Generic.List`1[System.String]"
        });

        var markdown = HtmlExporter.RenderHelpMarkdownForTesting(model);

        Assert.Contains("- ``System.Collections.Generic.List`1[System.String]``", markdown, StringComparison.Ordinal);
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

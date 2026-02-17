using System;
using System.IO;
using System.Linq;
using Xunit;
using PowerForge.Web;

public class WebApiDocsGeneratorSourceAndCssTests
{
    [Fact]
    public void GenerateDocsHtml_AppliesSourceUrlMappings_WithPathTokens()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sourcemap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source link test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source link test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web",
            UrlPattern = "https://example.invalid/{root}/blob/main/{pathNoPrefix}#L{line}"
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            Assert.True(htmlFiles.Length > 0, "Expected generated HTML pages.");

            var hasMappedSourceLink = htmlFiles.Any(path =>
                File.ReadAllText(path).Contains("https://example.invalid/PowerForge.Web/blob/main/", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasMappedSourceLink, "Expected at least one source/edit link rendered using sourceUrlMappings tokens.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_SourceUrlMappings_MatchWhenSourceRootUsesParentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sourcemap-parent-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source link test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source link test.");

        var sourceRoot = Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web",
            UrlPattern = "https://example.invalid/PowerForge.Web/blob/main/{pathNoPrefix}#L{line}",
            StripPathPrefix = true
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            Assert.DoesNotContain(result.Warnings, w =>
                w.Contains("sourceurlmappings entry for 'PowerForge.Web'", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("did not match any discovered source paths", StringComparison.OrdinalIgnoreCase));

            var htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            Assert.True(htmlFiles.Length > 0, "Expected generated HTML pages.");
            var html = string.Join(Environment.NewLine, htmlFiles.Select(File.ReadAllText));
            Assert.Contains("https://example.invalid/PowerForge.Web/blob/main/", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.invalid/PowerForge.Web/blob/main/../", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_AlwaysIncludesFallbackCssBaseline_WhenCustomCssIsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-fallback-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var xmlPath = Path.Combine(root, "test.xml");
        File.WriteAllText(xmlPath,
            """
            <doc>
              <assembly><name>Test</name></assembly>
              <members>
                <member name="T:MyNamespace.Sample">
                  <summary>Sample.</summary>
                </member>
              </members>
            </doc>
            """);

        var cssPath = Path.Combine(root, "css", "api.css");
        Directory.CreateDirectory(Path.GetDirectoryName(cssPath)!);
        File.WriteAllText(cssPath, ".api-layout { outline: 0; }");

        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            OutputPath = outputPath,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api",
            CssHref = "/css/api.css"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var indexHtmlPath = Path.Combine(outputPath, "index.html");
            Assert.True(File.Exists(indexHtmlPath), "Expected index.html to be generated.");
            var html = File.ReadAllText(indexHtmlPath);

            Assert.Contains("href=\"/css/api.css\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".type-chip{display:inline-flex", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".chip-icon{display:inline-flex", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("id=\"api-namespace\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("initNamespaceCombobox", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".pf-combobox-list::-webkit-scrollbar", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".member-card pre::-webkit-scrollbar", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".member-card pre::-webkit-scrollbar-track", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".member-card pre::-webkit-scrollbar-thumb", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".member-header pre.member-signature", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[data-theme=\"light\"] body.pf-api-docs .member-card pre::-webkit-scrollbar-thumb", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("initNavDropdowns", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_SourceUrlMappings_UseMostSpecificPrefix_AndHonorStripPathPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sourcemap-specific-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source link test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source link test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web",
            UrlPattern = "https://example.invalid/root/{path}#L{line}"
        });
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web/Services",
            UrlPattern = "https://example.invalid/services/{path}#L{line}",
            StripPathPrefix = true
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            Assert.True(htmlFiles.Length > 0, "Expected generated HTML pages.");
            var html = string.Join(Environment.NewLine, htmlFiles.Select(File.ReadAllText));

            Assert.Contains("https://example.invalid/services/", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://example.invalid/services/PowerForge.Web/Services/", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_SourcePathPrefix_PrependsPathBeforeUrlTokenExpansion()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-source-prefix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source link test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source link test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            SourcePathPrefix = "RepoRoot",
            SourceUrlPattern = "https://example.invalid/blob/main/{path}#L{line}",
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);

            var htmlFiles = Directory.GetFiles(outputPath, "*.html", SearchOption.AllDirectories);
            Assert.True(htmlFiles.Length > 0, "Expected generated HTML pages.");
            var html = string.Join(Environment.NewLine, htmlFiles.Select(File.ReadAllText));

            Assert.Contains("https://example.invalid/blob/main/RepoRoot/", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenSourceUrlMappingPrefixDoesNotMatchDiscoveredPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-sourcemap-unmatched-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source mapping warning test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source mapping warning test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "DefinitelyMissingPrefix",
            UrlPattern = "https://example.invalid/blob/main/{path}#L{line}"
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);
            Assert.Contains(result.Warnings, w =>
                w.Contains("sourceurlmappings entry for 'DefinitelyMissingPrefix'", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("did not match any discovered source paths", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenSourceUrlPatternLikelyDuplicatesPathPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-source-duplication-hint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source duplication warning test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source duplication warning test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            SourceUrlPattern = "https://github.com/example/PowerForge.Web/blob/main/PowerForge.Web/{path}#L{line}",
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);
            Assert.Contains(result.Warnings, w =>
                w.Contains("detected likely duplicated path prefixes in GitHub source URLs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenSourceUrlPatternHasNoPathToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-source-pattern-no-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source token warning test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source token warning test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            SourceUrlPattern = "https://github.com/example/PowerForge.Web/blob/main/README.md#L{line}",
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);
            Assert.Contains(result.Warnings, w =>
                w.Contains("[PFWEB.APIDOCS.SOURCE]", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("sourceUrl does not contain a path token", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateDocsHtml_WarnsWhenSourceUrlMappingUsesUnsupportedToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webapidocs-source-mapping-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var assemblyPath = typeof(WebApiDocsGenerator).Assembly.Location;
        var xmlPath = Path.ChangeExtension(assemblyPath, ".xml");
        Assert.True(File.Exists(assemblyPath), "PowerForge.Web assembly should exist for source mapping token warning test.");
        Assert.True(File.Exists(xmlPath), "PowerForge.Web XML docs should exist for source mapping token warning test.");

        var sourceRoot = ResolveGitRoot(assemblyPath) ?? Path.GetDirectoryName(assemblyPath) ?? root;
        var outputPath = Path.Combine(root, "api");
        var options = new WebApiDocsOptions
        {
            XmlPath = xmlPath,
            AssemblyPath = assemblyPath,
            OutputPath = outputPath,
            SourceRootPath = sourceRoot,
            Format = "html",
            Template = "docs",
            BaseUrl = "/api"
        };
        options.SourceUrlMappings.Add(new WebApiDocsSourceUrlMapping
        {
            PathPrefix = "PowerForge.Web",
            UrlPattern = "https://example.invalid/blob/main/{path}/{branch}#L{line}"
        });

        try
        {
            var result = WebApiDocsGenerator.Generate(options);
            Assert.True(result.TypeCount > 0);
            Assert.Contains(result.Warnings, w =>
                w.Contains("[PFWEB.APIDOCS.SOURCE]", StringComparison.OrdinalIgnoreCase) &&
                w.Contains("unsupported token(s): {branch}", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string? ResolveGitRoot(string path)
    {
        var current = Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git")))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}

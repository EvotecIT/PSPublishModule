using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsSeoDescriptionTests
{
    [Fact]
    public void Generate_DocsTemplate_EmitsSpecificSearchDescriptionsWithinRecommendedBounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-seo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "Sample.Api.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Api</name></assembly>
                  <members>
                    <member name="T:Sample.Api.ReportBuilder">
                      <summary>Builds validated reports from document data.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Title = "Sample API Reference",
                BaseUrl = "/api",
                Format = "html",
                Template = "docs"
            });

            var indexDescription = ReadMetaContent(Path.Combine(outputPath, "index.html"), "description");
            Assert.InRange(indexDescription.Length, 120, 160);
            Assert.Contains("1 documented type", indexDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("1 documented types", indexDescription, StringComparison.Ordinal);

            var typePath = Directory.GetDirectories(outputPath)
                .Select(path => Path.Combine(path, "index.html"))
                .Single(File.Exists);
            var typeHtml = File.ReadAllText(typePath);
            var typeDescription = ReadMetaContent(typeHtml, "description");
            Assert.InRange(typeDescription.Length, 120, 160);
            Assert.StartsWith("ReportBuilder:", typeDescription, StringComparison.Ordinal);
            Assert.Contains("Builds validated reports", typeDescription, StringComparison.Ordinal);
            Assert.Contains("parameters", typeDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("examples", typeDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("source links", typeDescription, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("related APIs", typeDescription, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(typeDescription, ReadMetaContent(typeHtml, "og:description", propertyAttribute: true));
            Assert.Equal(typeDescription, ReadMetaContent(typeHtml, "twitter:description"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PowerShellHelp_UsesCmdletSynopsisInSearchDescription()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-powershell-seo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Save-SampleReport</command:name>
                      <command:commandType>Function</command:commandType>
                      <maml:description><maml:para>Saves a validated report to a file or stream.</maml:para></maml:description>
                    </command:details>
                    <command:syntax>
                      <command:syntaxItem><command:name>Save-SampleReport</command:name></command:syntaxItem>
                    </command:syntax>
                  </command:command>
                </helpItems>
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = helpPath,
                OutputPath = outputPath,
                Title = "Sample Cmdlet Reference",
                BaseUrl = "/api/powershell",
                Format = "html",
                Template = "docs"
            });

            var indexDescription = ReadMetaContent(Path.Combine(outputPath, "index.html"), "description");
            Assert.InRange(indexDescription.Length, 120, 160);
            Assert.Contains("1 documented reference entry", indexDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("1 documented reference entries", indexDescription, StringComparison.Ordinal);

            var cmdletDescription = ReadMetaContent(
                Path.Combine(outputPath, "save-samplereport", "index.html"),
                "description");
            Assert.InRange(cmdletDescription.Length, 120, 160);
            Assert.Contains("Saves a validated report", cmdletDescription, StringComparison.Ordinal);
            Assert.Contains("Save-SampleReport", cmdletDescription, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_PreservesLiteralAngleBracketNotationInSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-seo-angle-brackets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "Sample.Api.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Api</name></assembly>
                  <members>
                    <member name="T:Sample.Api.ComparisonGuide">
                      <summary>Compares x &lt; y &gt; z and preserves List&lt;T&gt; notation.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Title = "Sample API Reference",
                BaseUrl = "/api",
                Format = "html",
                Template = "docs"
            });

            var typePath = Directory.GetDirectories(outputPath)
                .Select(path => Path.Combine(path, "index.html"))
                .Single(File.Exists);
            var description = ReadMetaContent(typePath, "description");
            Assert.Contains("x < y > z", description, StringComparison.Ordinal);
            Assert.Contains("List<T>", description, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void GenerateSuitePortal_FormatsCountsIndependentlyOfBuildCulture()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-seo-culture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var outputPath = Path.Combine(root, "_site", "api-suite");
            var options = new WebApiDocsOptions
            {
                OutputPath = outputPath,
                Title = "Project APIs",
                BaseUrl = "/api-suite"
            };
            for (var index = 0; index < 1_000; index++)
            {
                options.ApiSuiteEntries.Add(new WebApiDocsSuiteEntry
                {
                    Id = $"project-{index}",
                    Label = $"Project {index}",
                    Href = $"/projects/{index}/api/",
                    Order = index
                });
            }

            _ = WebApiDocsGenerator.GenerateSuitePortal(options);

            var description = ReadMetaContent(Path.Combine(outputPath, "index.html"), "description");
            Assert.Contains("1,000 API references", description, StringComparison.Ordinal);
            Assert.DoesNotContain("1.000 API references", description, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_SimpleTemplate_UsesCapabilityNeutralDescriptions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-seo-simple-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "Sample.Api.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Api</name></assembly>
                  <members>
                    <member name="T:Sample.Api.ReportBuilder">
                      <summary>Builds validated reports from document data.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var outputPath = Path.Combine(root, "_site", "api");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outputPath,
                Title = "Sample API Reference",
                BaseUrl = "/api",
                Format = "html",
                Template = "simple"
            });

            var indexDescription = ReadMetaContent(Path.Combine(outputPath, "index.html"), "description");
            Assert.InRange(indexDescription.Length, 120, 160);
            Assert.Contains("generated reference pages", indexDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("searchable signatures", indexDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("type relationships", indexDescription, StringComparison.Ordinal);

            var typePath = Directory.GetFiles(Path.Combine(outputPath, "types"), "*.html").Single();
            var typeDescription = ReadMetaContent(typePath, "description");
            Assert.InRange(typeDescription.Length, 120, 160);
            Assert.Contains("member details", typeDescription, StringComparison.Ordinal);
            Assert.DoesNotContain("type relationships", typeDescription, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BuildApiTypeSeoDescription_UsesConceptualWordingForAboutTopics()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-seo-about-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "en-US", "Sample.Module-help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(helpPath)!);
            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-Sample</command:name>
                      <maml:description><maml:para>Gets sample data.</maml:para></maml:description>
                    </command:details>
                    <command:syntax><command:syntaxItem><command:name>Get-Sample</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);
            File.WriteAllText(Path.Combine(root, "about_Sample.help.txt"),
                """
                # about_Sample

                Explains configuration and workflow behavior.
                """);

            var outputPath = Path.Combine(root, "_site", "api", "powershell");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell,
                HelpPath = root,
                OutputPath = outputPath,
                Title = "Sample PowerShell Reference",
                BaseUrl = "/api/powershell",
                Template = "docs",
                Format = "html"
            });

            var description = ReadMetaContent(
                Path.Combine(outputPath, "about-sample", "index.html"),
                "description");
            Assert.InRange(description.Length, 120, 160);
            Assert.Contains("complete conceptual topic", description, StringComparison.Ordinal);
            Assert.DoesNotContain("syntax", description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("parameters", description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pipeline", description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FitApiSeoDescription_PreservesUnicodeScalarsAndMinimumLength()
    {
        var method = typeof(WebApiDocsGenerator).GetMethod(
            "FitApiSeoDescription",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var unicodeInput = new string('界', 158) + "😀tail";
        var unicodeDescription = Assert.IsType<string>(method!.Invoke(null, [unicodeInput]));
        Assert.InRange(unicodeDescription.Length, 120, 160);
        Assert.DoesNotContain('\uFFFD', unicodeDescription);
        AssertValidSurrogatePairs(unicodeDescription);

        var conjunctionInput = new string('x', 116) + " and " + new string('y', 100);
        var conjunctionDescription = Assert.IsType<string>(method.Invoke(null, [conjunctionInput]));
        Assert.InRange(conjunctionDescription.Length, 120, 160);
        Assert.DoesNotMatch(@"\b(?:and|or|with|including|from|to)\.$", conjunctionDescription);
    }

    private static void AssertValidSurrogatePairs(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                Assert.True(index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]));
                index++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(value[index]));
            }
        }
    }

    private static string ReadMetaContent(string path, string name, bool propertyAttribute = false)
    {
        var html = File.Exists(path) ? File.ReadAllText(path) : path;
        var attribute = propertyAttribute ? "property" : "name";
        var match = Regex.Match(
            html,
            $"<meta\\s+{attribute}=\"{Regex.Escape(name)}\"\\s+content=\"(?<content>[^\"]*)\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, $"Expected {attribute}={name} in generated HTML.");
        return WebUtility.HtmlDecode(match.Groups["content"].Value);
    }
}

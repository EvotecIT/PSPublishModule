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
            Assert.Contains("documented types", indexDescription, StringComparison.Ordinal);

            var typePath = Directory.GetDirectories(outputPath)
                .Select(path => Path.Combine(path, "index.html"))
                .Single(File.Exists);
            var typeHtml = File.ReadAllText(typePath);
            var typeDescription = ReadMetaContent(typeHtml, "description");
            Assert.InRange(typeDescription.Length, 120, 160);
            Assert.StartsWith("ReportBuilder:", typeDescription, StringComparison.Ordinal);
            Assert.Contains("Builds validated reports", typeDescription, StringComparison.Ordinal);
            Assert.Contains("parameters", typeDescription, StringComparison.Ordinal);
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
            Assert.Contains("documented cmdlets", indexDescription, StringComparison.Ordinal);

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

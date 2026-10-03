using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebGenerationIntegrityTests
{
    [Fact]
    public void CachedBuildReturnsFailedStepForMalformedConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-invalid-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "site.json"), "{");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"cache":true,"steps":[{"task":"build","config":"site.json","out":"site"}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.False(result.Steps.Single().Success);
            Assert.False(result.Steps.Single().Cached);
            Assert.False(string.IsNullOrWhiteSpace(result.Steps.Single().Message));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedPowerShellHelpPreservesExistingReference()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-help-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var help = Path.Combine(root, "Module-help.xml");
            File.WriteAllText(help, """
                <helpItems xmlns:c="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:m="http://schemas.microsoft.com/maml/2004/10">
                <c:command><c:details><c:name>Get-Fixture</c:name><m:description><m:para>Fixture command.</m:para></m:description></c:details></c:command>
                </helpItems>
                """);
            var options = new WebApiDocsOptions { Type = ApiDocsType.PowerShell, HelpPath = help, OutputPath = Path.Combine(root, "api"), Format = "json" };
            var generated = WebApiDocsGenerator.Generate(options);
            Assert.Equal(1, generated.TypeCount);
            var index = Path.Combine(options.OutputPath, "index.json");
            var before = File.ReadAllText(index);
            File.WriteAllText(help, "<helpItems><broken>");
            Assert.Throws<InvalidDataException>(() => WebApiDocsGenerator.Generate(options));
            Assert.Equal(before, File.ReadAllText(index));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("content")]
    [InlineData("theme")]
    [InlineData("data")]
    [InlineData("static")]
    [InlineData("inherited-config")]
    public void CachedBuildTracksSiteInputsWhenSizeAndTimestampsArePreserved(string input)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-site-integrity-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            Directory.CreateDirectory(Path.Combine(root, "themes", "t", "layouts"));
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "static"));
            var paths = new Dictionary<string, string>
            {
                ["content"] = Path.Combine(root, "content", "index.md"),
                ["theme"] = Path.Combine(root, "themes", "t", "layouts", "page.html"),
                ["data"] = Path.Combine(root, "data", "message.json"),
                ["static"] = Path.Combine(root, "static", "test.txt"),
                ["inherited-config"] = Path.Combine(root, "base.json")
            };
            File.WriteAllText(paths["content"], "---\ntitle: Home\nslug: index\n---\nOld!");
            File.WriteAllText(paths["theme"], "<!doctype html><title>{{TITLE}}</title><main>{{CONTENT}}Old!</main>");
            File.WriteAllText(paths["data"], "{\"title\":\"Old!\"}");
            File.WriteAllText(paths["static"], "Old!");
            File.WriteAllText(paths["inherited-config"], "{\"name\":\"Old!\"}");
            File.WriteAllText(Path.Combine(root, "themes", "t", "theme.json"), "{\"name\":\"t\",\"engine\":\"simple\",\"defaultLayout\":\"page\"}");
            File.WriteAllText(Path.Combine(root, "site.json"), """
                {"extends":"base.json","baseUrl":"https://example.test","defaultTheme":"t","themesRoot":"themes","dataRoot":"data",
                "collections":[{"name":"pages","input":"content","output":"/"}],"staticAssets":[{"source":"static"}],"cache":{"enabled":true,"mode":"mtime"}}
                """);
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"cache":true,"steps":[{"task":"build","config":"site.json","out":"site"}]}""");
            var first = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(first.Success, first.Steps.Single().Message);
            var warm = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(warm.Success);
            Assert.True(warm.Steps.Single().Cached);
            var timestamp = File.GetLastWriteTimeUtc(paths[input]);
            File.WriteAllText(paths[input], File.ReadAllText(paths[input]).Replace("Old!", "New!", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(paths[input], timestamp);
            var changed = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(changed.Success, changed.Steps.Single().Message);
            Assert.False(changed.Steps.Single().Cached);
            if (input is "content" or "theme")
                Assert.Contains("New!", File.ReadAllText(Path.Combine(root, "site", "index.html")));
            if (input == "static")
                Assert.Equal("New!", File.ReadAllText(Path.Combine(root, "site", "test.txt")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MalformedXmlPreservesExistingReferenceAndXmlOnlyReportsVisibilityLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, "<doc><members><member name=\"T:Example.Type\"><summary>Documented type.</summary></member></members></doc>");
            var options = new WebApiDocsOptions { XmlPath = xml, OutputPath = Path.Combine(root, "out"), Format = "json" };
            var generated = WebApiDocsGenerator.Generate(options);
            Assert.Contains(generated.Warnings, warning => warning.Contains("public accessibility", StringComparison.Ordinal));
            var typePath = Path.Combine(options.OutputPath, "types", "example-type.json");
            var before = File.ReadAllText(typePath);
            File.WriteAllText(xml, "<doc><broken>");
            Assert.Throws<InvalidDataException>(() => WebApiDocsGenerator.Generate(options));
            Assert.Equal(before, File.ReadAllText(typePath));
            using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(options.OutputPath, "index.json")));
            Assert.Equal(1, index.RootElement.GetProperty("typeCount").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }
}

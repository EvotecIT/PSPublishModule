using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerProjectDocsLinkMappingTests
{
    [Fact]
    public void RunPipeline_MapsOnlyConfiguredProjectDocsAndMarkdownExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-doc-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                { "projects": [
                  { "slug": "alpha", "surfaces": { "docs": true, "examples": true } },
                  { "slug": "beta", "surfaces": { "docs": true, "examples": true } }
                ] }
                """);
            const string markdown = "# Guide\n\n[Guide](/docs/alpha/start/)\n\n```text\n[Code](/docs/alpha/start/)\n```\n";
            foreach (var slug in new[] { "alpha", "beta" })
            {
                var source = Path.Combine(root, "sources", slug);
                Directory.CreateDirectory(Path.Combine(source, "Docs"));
                Directory.CreateDirectory(Path.Combine(source, "content", "examples"));
                File.WriteAllText(Path.Combine(source, "Docs", "guide.md"), markdown);
                File.WriteAllText(Path.Combine(source, "content", "examples", "sample.md"), markdown);
                File.WriteAllText(Path.Combine(source, "content", "examples", "sample.markdown"), markdown);
                File.WriteAllText(Path.Combine(source, "content", "examples", "sample.ps1"), markdown);
            }
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """
                { "steps": [{
                  "task": "project-docs-sync", "catalog": "./catalog.json",
                  "sourcesRoot": "./sources", "contentRoot": "./docs", "examplesRoot": "./examples",
                  "hydrateFromArtifacts": false, "generateToc": false,
                  "linkMappings": { "ALPHA": { "/docs/alpha/": "/projects/alpha/docs/" } }
                }] }
                """);

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.True(result.Success, result.Steps.Single().Message);
            foreach (var surface in new[] { "docs", "examples" })
            {
                var file = surface == "docs" ? "guide.md" : "sample.md";
                var alpha = File.ReadAllText(Path.Combine(root, surface, "alpha", file));
                Assert.Contains("[Guide](/projects/alpha/docs/start/)", alpha, StringComparison.Ordinal);
                Assert.Contains("[Code](/docs/alpha/start/)", alpha, StringComparison.Ordinal);
                Assert.Contains("[Guide](/docs/alpha/start/)",
                    File.ReadAllText(Path.Combine(root, surface, "beta", file)), StringComparison.Ordinal);
            }
            Assert.Equal(markdown, File.ReadAllText(Path.Combine(root, "examples", "alpha", "sample.ps1")));
            Assert.Contains("[Guide](/projects/alpha/docs/start/)",
                File.ReadAllText(Path.Combine(root, "examples", "alpha", "sample.markdown")), StringComparison.Ordinal);
            Assert.Equal(markdown, File.ReadAllText(Path.Combine(root, "sources", "alpha", "Docs", "guide.md")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerPublicProjectLinksTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pipeline_PublicMetadataExcludesUnknownRepositoryLinks_FromLegacyAndFallbackSnapshots(bool refreshStats)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-public-metadata-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "stats.json"), """
                {
                  "summary": {"repositoryCount":1,"nuGetPackageCount":1,"nuGetDownloads":123,"totalDownloads":123},
                  "gitHub": {"organization":"ExampleOrg","repositoryCount":1,"repositories":[{"name":"PublicRepo","fullName":"ExampleOrg/PublicRepo","url":"https://github.com/ExampleOrg/PublicRepo"}]},
                  "nuget": {"owner":"ExampleOwner","packageCount":1,"totalDownloads":123,"packages":[{"id":"ExamplePackage","version":"1.0.0","totalDownloads":123,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"}]}
                }
                """);
            File.WriteAllText(Path.Combine(root, "catalog.json"), """
                {"projects":[{"slug":"examplepackage","name":"ExamplePackage","kind":"product","mode":"hub-full","description":"Example product.","listed":true,"metrics":{"nuget":{"id":"ExamplePackage","version":"1.0.0","totalDownloads":123,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"}}}]}
                """);
            var step = refreshStats
                ? $$"""
                    {"task":"ecosystem-stats","out":"./stats.json","publishPath":"./public-stats.json","githubOrg":"pf-test-org-{{Guid.NewGuid():N}}","timeoutSeconds":1,
                     "syncProjectCatalogTelemetry":true,"projectCatalogPath":"./catalog.json","projectCatalogPublishPath":"./public-catalog.json"}
                    """
                : """
                    {"task":"project-catalog","catalog":"./catalog.json","publishPath":"./public-catalog.json","statsPath":"./stats.json","mergeTelemetry":true,
                     "mergeReleaseTelemetry":false,"importManifests":false,"applyCuration":false,"validate":false,"generatePages":false,"generateSections":false}
                    """;
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, "{\"steps\":[" + step + "]}");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success, result.Steps.FirstOrDefault()?.Message);

            foreach (var file in new[] { "catalog.json", "public-catalog.json" })
            {
                var content = File.ReadAllText(Path.Combine(root, file));
                Assert.DoesNotContain("ExampleOrg/PrivateRepo", content);
                using var document = JsonDocument.Parse(content);
                var metrics = document.RootElement.GetProperty("projects")[0].GetProperty("metrics").GetProperty("nuget");
                Assert.Equal(123, metrics.GetProperty("totalDownloads").GetInt64());
                Assert.Equal("1.0.0", metrics.GetProperty("version").GetString());
            }
            if (refreshStats)
            {
                Assert.Contains("fallback", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ExampleOrg/PrivateRepo", File.ReadAllText(Path.Combine(root, "public-stats.json")));
                Assert.Equal(File.ReadAllText(Path.Combine(root, "stats.json")), File.ReadAllText(Path.Combine(root, "public-stats.json")));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

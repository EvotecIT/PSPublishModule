using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerPublicProjectLinksTests
{
    [Theory]
    [InlineData(false, "package", "https://github.com/ExampleOrg/Suite")]
    [InlineData(true, "package", "https://github.com/ExampleOrg/Suite")]
    [InlineData(false, "repository", "https://github.com/ExampleOrg/Suite")]
    [InlineData(true, "repository", "https://github.com/ExampleOrg/Suite")]
    [InlineData(false, "alias", "https://github.com/ExampleOrg/Suite")]
    [InlineData(true, "alias", "https://github.com/ExampleOrg/Suite")]
    [InlineData(false, "repository", "https://api.github.com/repos/ExampleOrg/Suite")]
    [InlineData(false, "alias", "https://api.github.com/repos/ExampleOrg/Suite")]
    [InlineData(false, "repository", "https://raw.githubusercontent.com/ExampleOrg/Suite/main/README.md")]
    [InlineData(false, "alias", "https://raw.githubusercontent.com/ExampleOrg/Suite/main/README.md")]
    [InlineData(false, "repository", "https://github.com/ExampleOrg/%53uite")]
    [InlineData(false, "alias", "https://github.com/ExampleOrg/%53uite")]
    public void Pipeline_WithheldRepositoryLinksPreservePackageFamilyTotals(bool normalizedSnapshot, string match, string projectUrl)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-package-associations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var stats = new WebEcosystemStatsDocument
            {
                GitHub = new() { Organization = "ExampleOrg" },
                NuGet = new() { Items = new()
                {
                    new() { Id = "Suite", TotalDownloads = 100, ProjectUrl = projectUrl },
                    new() { Id = "Suite.Core", TotalDownloads = 200, ProjectUrl = projectUrl }
                } }
            };
            if (normalizedSnapshot) WebEcosystemStatsGenerator.NormalizePublicProjectLinks(stats);
            File.WriteAllText(Path.Combine(root, "stats.json"), JsonSerializer.Serialize(stats, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var project = match switch
            {
                "repository" => """{"slug":"product","name":"Product","githubRepo":"ExampleOrg/Suite","kind":"library","mode":"hub-full","listed":true}""",
                "alias" => """{"slug":"product","name":"Product","packageAliases":{"nuget":["Suite"]},"kind":"library","mode":"hub-full","listed":true}""",
                _ => """{"slug":"suite","name":"Suite","kind":"library","mode":"hub-full","listed":true}"""
            };
            // Repository and alias matching have no package-ID seed.
            if (match != "package")
            {
                stats.NuGet.Items[0].Id = "LibraryA";
                stats.NuGet.Items[1].Id = "LibraryB";
                File.WriteAllText(Path.Combine(root, "stats.json"), JsonSerializer.Serialize(stats, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            File.WriteAllText(Path.Combine(root, "catalog.json"), "{\"projects\":[" + project + "]}");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"project-catalog","catalog":"./catalog.json","publishPath":"./public-catalog.json","statsPath":"./stats.json","mergeTelemetry":true,"mergeReleaseTelemetry":false,"importManifests":false,"applyCuration":false,"validate":false,"generatePages":false,"generateSections":false}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success, result.Steps[0].Message);
            using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "public-catalog.json")));
            var metrics = catalog.RootElement.GetProperty("projects")[0].GetProperty("metrics").GetProperty("nuget");
            Assert.Equal(match == "package" ? 100 : 300, metrics.GetProperty("totalDownloads").GetInt64());
            Assert.Equal(match == "package" ? 1 : 2, metrics.GetProperty("packageCount").GetInt32());
            Assert.False(metrics.TryGetProperty("projectUrl", out var url) && url.ValueKind == JsonValueKind.String);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pipeline_WholeSnapshotFallbackAppliesCurrentlyConfiguredOrganization(bool differentRetainedOrganization)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-configured-link-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var organization = "pf-test-org-" + Guid.NewGuid().ToString("N");
            var retainedStats = JsonNode.Parse("""{"summary":{"nuGetPackageCount":1,"totalDownloads":123},"nuget":{"packageCount":1,"packages":[{"id":"Suite","totalDownloads":123,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"}]}}""")!;
            if (differentRetainedOrganization)
            {
                retainedStats["gitHub"] = JsonNode.Parse("""{"organization":"OldOrg","repositoryCount":1,"repositories":[{"fullName":"OldOrg/PublicRepo","url":"https://github.com/OldOrg/PublicRepo"}]}""");
                retainedStats["summary"]!["repositoryCount"] = 1;
            }
            File.WriteAllText(Path.Combine(root, "stats.json"), retainedStats.ToJsonString().Replace("ExampleOrg", organization, StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "catalog.json"), """{"projects":[{"slug":"suite","name":"Suite","kind":"library","mode":"hub-full","listed":true,"metrics":{"nuget":{"totalDownloads":123,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"}}},{"slug":"absent","name":"Absent","kind":"library","mode":"hub-full","listed":true,"metrics":{"nuget":{"totalDownloads":321,"projectUrl":"https://github.com/ExampleOrg/PrivateRepo"}}}]}""".Replace("ExampleOrg", organization, StringComparison.Ordinal));
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, $$"""{"steps":[{"task":"ecosystem-stats","out":"./stats.json","publishPath":"./public-stats.json","githubOrg":"{{organization}}","timeoutSeconds":1,"syncProjectCatalogTelemetry":true,"projectCatalogPath":"./catalog.json","projectCatalogPublishPath":"./public-catalog.json"}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success, result.Steps[0].Message);
            foreach (var file in new[] { "stats.json", "public-stats.json", "catalog.json", "public-catalog.json" })
                Assert.DoesNotContain(organization + "/PrivateRepo", File.ReadAllText(Path.Combine(root, file)));
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "public-stats.json")));
            Assert.Equal(organization, document.RootElement.GetProperty("gitHub").GetProperty("organization").GetString());
            Assert.Equal(0, document.RootElement.GetProperty("summary").GetProperty("repositoryCount").GetInt32());
            Assert.Equal(123, document.RootElement.GetProperty("nuget").GetProperty("packages")[0].GetProperty("totalDownloads").GetInt64());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pipeline_PublicMetadataExcludesUnknownRepositoryLinks_FromLegacyAndFallbackSnapshots(bool refreshStats)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-public-metadata-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var organization = "pf-test-org-" + Guid.NewGuid().ToString("N");
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
            foreach (var file in new[] { "stats.json", "catalog.json" })
                File.WriteAllText(Path.Combine(root, file), File.ReadAllText(Path.Combine(root, file)).Replace("ExampleOrg", organization, StringComparison.Ordinal));
            var step = refreshStats
                ? $$"""
                    {"task":"ecosystem-stats","out":"./stats.json","publishPath":"./public-stats.json","githubOrg":"{{organization}}","timeoutSeconds":1,
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
                Assert.DoesNotContain(organization + "/PrivateRepo", content);
                using var document = JsonDocument.Parse(content);
                var metrics = document.RootElement.GetProperty("projects")[0].GetProperty("metrics").GetProperty("nuget");
                Assert.Equal(123, metrics.GetProperty("totalDownloads").GetInt64());
                Assert.Equal("1.0.0", metrics.GetProperty("version").GetString());
            }
            if (refreshStats)
            {
                Assert.Contains("fallback", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(organization + "/PrivateRepo", File.ReadAllText(Path.Combine(root, "public-stats.json")));
                Assert.Equal(File.ReadAllText(Path.Combine(root, "stats.json")), File.ReadAllText(Path.Combine(root, "public-stats.json")));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

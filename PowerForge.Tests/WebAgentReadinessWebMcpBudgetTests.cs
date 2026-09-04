using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebAgentReadinessTests
{
    [Fact]
    public void SearchIndexGeneration_EnforcesEntryAndDecodedByteBudgets()
    {
        var excessiveCount = Enumerable.Range(0, WebSearchIndexPolicy.MaximumEntries + 1)
            .Select(index => new SearchIndexEntry { Title = $"Entry {index}", Url = $"/{index}/" })
            .ToArray();

        var countBounded = WebSearchIndexPolicy.CreateBoundedJson(excessiveCount);

        Assert.Equal(excessiveCount.Length, countBounded.SourceCount);
        Assert.Equal(WebSearchIndexPolicy.MaximumEntries, countBounded.Count);
        Assert.True(countBounded.Truncated);
        Assert.True(Encoding.UTF8.GetByteCount(countBounded.Json) <= WebSearchIndexPolicy.MaximumDecodedBytes);
        Assert.True(WebSearchIndexPolicy.TryValidateJsonArray(countBounded.Json, out var count, out _, out _));
        Assert.Equal(WebSearchIndexPolicy.MaximumEntries, count);

        var excessiveEntry = new SearchIndexEntry
        {
            Title = "Oversized",
            Url = "/oversized/",
            SearchText = new string('x', WebSearchIndexPolicy.MaximumDecodedBytes)
        };
        var smallEntry = new SearchIndexEntry { Title = "Small", Url = "/small/" };

        var byteBounded = WebSearchIndexPolicy.CreateBoundedJson(new[] { excessiveEntry, smallEntry });

        Assert.Equal(2, byteBounded.SourceCount);
        Assert.Equal(1, byteBounded.Count);
        Assert.True(byteBounded.Truncated);
        Assert.Contains("/small/", byteBounded.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("/oversized/", byteBounded.Json, StringComparison.Ordinal);
        Assert.True(WebSearchIndexPolicy.TryValidateJsonArray(byteBounded.Json, out count, out _, out _));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Build_CompactsSearchMetadataAndRecordsArtifactIntegrity()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test", "/search/");
        var contentPath = Path.Combine(root, "content", "pages", "index.md");
        var longDescription = new string('d', WebSearchIndexPolicy.MaximumSearchTextCharacters);
        File.WriteAllText(contentPath,
            $$"""
            ---
            title: Search metadata
            slug: index
            description: {{longDescription}}
            date: 2026-09-04
            image: /assets/search.png
            aliases:
              - CompactAliasToken
            keywords:
              - CompactKeywordToken
            extra_scripts: PresentationScriptMustNotLeak
            extra_css: PresentationCssMustNotLeak
            ---

            Searchable body.
            """);

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);

            var indexPath = Path.Combine(outputRoot, "search", "index.json");
            var indexText = File.ReadAllText(indexPath);
            using var index = JsonDocument.Parse(indexText);
            var entry = Assert.Single(index.RootElement.EnumerateArray());
            var searchText = entry.GetProperty("searchText").GetString() ?? string.Empty;
            Assert.Contains("CompactAliasToken", searchText, StringComparison.Ordinal);
            Assert.Contains("CompactKeywordToken", searchText, StringComparison.Ordinal);
            Assert.DoesNotContain("PresentationScriptMustNotLeak", searchText, StringComparison.Ordinal);
            Assert.DoesNotContain("PresentationCssMustNotLeak", searchText, StringComparison.Ordinal);
            Assert.True(searchText.Length <= 4_096);

            var meta = entry.GetProperty("meta");
            Assert.True(meta.TryGetProperty("date", out _));
            Assert.Equal("/assets/search.png", meta.GetProperty("image").GetString());
            Assert.False(meta.TryGetProperty("aliases", out _));
            Assert.False(meta.TryGetProperty("keywords", out _));
            Assert.False(meta.TryGetProperty("extra_scripts", out _));
            Assert.False(meta.TryGetProperty("extra_css", out _));

            var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(indexText))).ToLowerInvariant();
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "search", "manifest.json")));
            Assert.Equal(1, manifest.RootElement.GetProperty("entryCount").GetInt32());
            Assert.Equal(1, manifest.RootElement.GetProperty("totalEntryCount").GetInt32());
            Assert.False(manifest.RootElement.GetProperty("searchIndexTruncated").GetBoolean());
            Assert.Equal(new FileInfo(indexPath).Length, manifest.RootElement.GetProperty("searchIndexBytes").GetInt64());
            Assert.Equal(expectedHash, manifest.RootElement.GetProperty("searchIndexSha256").GetString());
            Assert.All(
                manifest.RootElement.GetProperty("collectionShards").EnumerateArray(),
                shard =>
                {
                    Assert.Equal(shard.GetProperty("sourceCount").GetInt32(), shard.GetProperty("count").GetInt32());
                    Assert.False(shard.GetProperty("truncated").GetBoolean());
                    Assert.True(shard.GetProperty("bytes").GetInt64() > 0);
                    Assert.Equal(64, shard.GetProperty("sha256").GetString()!.Length);
                });
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Build_UsesDistinctCollectionShardPathsWhenSlugsCollide()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test", "/search/");
        var csharpRoot = Path.Combine(root, "content", "csharp");
        var cppRoot = Path.Combine(root, "content", "cpp");
        var secondaryCollisionRoot = Path.Combine(root, "content", "secondary-collision");
        Directory.CreateDirectory(csharpRoot);
        Directory.CreateDirectory(cppRoot);
        Directory.CreateDirectory(secondaryCollisionRoot);
        File.WriteAllText(Path.Combine(csharpRoot, "index.md"), "---\ntitle: C sharp\n---\n\nC sharp content.");
        File.WriteAllText(Path.Combine(cppRoot, "index.md"), "---\ntitle: C plus plus\n---\n\nC plus plus content.");
        File.WriteAllText(Path.Combine(secondaryCollisionRoot, "index.md"), "---\ntitle: Secondary collision\n---\n\nSecondary collision content.");
        var secondaryCollision = $"c-{WebSearchIndexPolicy.ComputeSha256("C++".ToUpperInvariant())[..12]}";
        spec.Collections =
        [
            new CollectionSpec { Name = "C#", Input = "content/csharp", Output = "/csharp" },
            new CollectionSpec { Name = "C++", Input = "content/cpp", Output = "/cpp" },
            new CollectionSpec { Name = secondaryCollision, Input = "content/secondary-collision", Output = "/secondary-collision" }
        ];

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "search", "manifest.json")));
            var shards = manifest.RootElement.GetProperty("collectionShards").EnumerateArray().ToArray();
            Assert.Equal(3, shards.Length);
            var paths = shards.Select(static shard => shard.GetProperty("path").GetString()).ToArray();
            Assert.Equal(3, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(shards, shard =>
            {
                var relativePath = shard.GetProperty("path").GetString()!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var shardPath = Path.Combine(outputRoot, relativePath);
                var shardText = File.ReadAllText(shardPath);
                Assert.Equal(new FileInfo(shardPath).Length, shard.GetProperty("bytes").GetInt64());
                Assert.Equal(
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shardText))).ToLowerInvariant(),
                    shard.GetProperty("sha256").GetString());
                Assert.Single(JsonDocument.Parse(shardText).RootElement.EnumerateArray());
            });
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void PipelineSchema_AcceptsAgentReadyExerciseContract()
    {
        var schemaPath = Path.Combine(FindWebMcpRepositoryRoot(), "Schemas", "powerforge.web.pipelinespec.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        var pipeline = JsonNode.Parse(
            """
            {
              "steps": [
                {
                  "task": "agent-ready",
                  "operation": "exercise",
                  "url": "https://example.test/search/",
                  "query": "Word to PDF",
                  "toolName": "search_site",
                  "limit": 3,
                  "timeoutMs": 30000,
                  "ensureBrowser": true,
                  "headed": false,
                  "failOnFailures": true
                }
              ]
            }
            """)!;

        var evaluation = schema.Evaluate(pipeline, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(evaluation.IsValid, evaluation.ToString());
    }

    [Fact]
    public void Verify_RejectsSearchIndexAboveDecodedByteBudget()
    {
        var (root, spec, configPath, outputRoot) = CreateMinimalWebMcpBuild("https://example.test", "/search/");

        try
        {
            WebSiteBuilder.Build(spec, WebSitePlanner.Plan(spec, configPath), outputRoot);
            var indexPath = Path.Combine(outputRoot, "search", "index.json");
            using (var stream = new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.None))
                stream.SetLength(8L * 1024 * 1024 + 1);

            var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = outputRoot,
                BaseUrl = spec.BaseUrl,
                AgentReadiness = spec.AgentReadiness
            });

            var check = Assert.Single(result.Checks, candidate => candidate.Id == "webmcp");
            Assert.Equal("fail", check.Status);
            Assert.Contains("8388608", check.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Runtime_DeclaresBoundedToolAndIndexContracts()
    {
        var runtime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();

        Assert.Contains("var DEFAULT_RESULT_LIMIT = 3;", runtime, StringComparison.Ordinal);
        Assert.Contains("var MAX_RESULT_LIMIT = 5;", runtime, StringComparison.Ordinal);
        Assert.Contains("var MAX_RESULT_URL_CHARACTERS = 400;", runtime, StringComparison.Ordinal);
        Assert.Contains("if (!url || url.length > MAX_RESULT_URL_CHARACTERS) return null;", runtime, StringComparison.Ordinal);
        Assert.Contains("url: url,", runtime, StringComparison.Ordinal);
        Assert.Contains("outputTruncated: source.length > selected.length || shaped.length !== selected.length", runtime, StringComparison.Ordinal);
        Assert.Contains("result.totalMatches >= source.length", runtime, StringComparison.Ordinal);
        Assert.Contains("source.length > selected.length", runtime, StringComparison.Ordinal);
        Assert.Contains("lengthHeader == null ? NaN : Number(lengthHeader)", runtime, StringComparison.Ordinal);
        Assert.Contains("if (!contentEncoding && Number.isFinite(advertisedLength)", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("url: boundedText", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("url.length > MAX_RESULT_URL_CHARACTERS || seen[url]", runtime, StringComparison.Ordinal);
        Assert.Contains("var MAX_OUTPUT_CHARACTERS = 1500;", runtime, StringComparison.Ordinal);
        Assert.Contains("var MAX_INDEX_BYTES = 8 * 1024 * 1024;", runtime, StringComparison.Ordinal);
        Assert.Contains("var MAX_INDEX_ENTRIES = 5000;", runtime, StringComparison.Ordinal);
        Assert.Contains("outputTruncated", runtime, StringComparison.Ordinal);
        Assert.Contains("readBoundedResponseText", runtime, StringComparison.Ordinal);
        Assert.Contains("options?.signal", WebMcpBehavioralTester.RegistrationCaptureScript, StringComparison.Ordinal);
        Assert.Contains("delete tools[tool.name]", WebMcpBehavioralTester.RegistrationCaptureScript, StringComparison.Ordinal);
    }

    [Fact]
    public void BehavioralObservation_ProvesRegistrationOutputBudgetAndVisibleSynchronization()
    {
        var output = new
        {
            query = "Word to PDF",
            totalMatches = 3,
            returned = 3,
            moreResultsAvailable = false,
            outputTruncated = false,
            results = new[]
            {
                new { title = "One", url = "/one/" },
                new { title = "Two", url = "/two/" },
                new { title = "Three", url = "/three/" }
            }
        };
        var outputJson = JsonSerializer.Serialize(output);
        var observation = JsonSerializer.Serialize(new
        {
            registeredTools = new[] { "search_site" },
            schemaType = "object",
            schemaQueryType = "string",
            schemaQueryMinimum = 1,
            schemaQueryMaximum = 200,
            schemaLimitType = "integer",
            schemaLimitMinimum = 1,
            schemaMaximum = 5,
            schemaDefault = 3,
            schemaRequired = new[] { "query" },
            schemaAdditionalProperties = false,
            readOnlyHint = true,
            untrustedContentHint = true,
            visibleQuery = "Word to PDF",
            visibleResultText = "One Two Three",
            visibleResultUrls = new[]
            {
                "https://example.test/one/",
                "https://example.test/two/",
                "https://example.test/three/"
            },
            visibleResultChanged = true,
            outputResultUrls = new[]
            {
                "https://example.test/one/",
                "https://example.test/two/",
                "https://example.test/three/"
            },
            output,
            outputJson
        });

        var result = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions
            {
                Url = "https://example.test/search/",
                ToolName = "search_site"
            },
            "Word to PDF",
            observation);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(3, result.Returned);
        Assert.Equal(outputJson.Length, result.OutputCharacters);
        Assert.Equal("Word to PDF", result.VisibleQuery);
        Assert.Equal(3, result.VisibleResultUrls.Length);
    }

    [Fact]
    public void BehavioralObservation_RejectsStaleVisibleResultsAndInconsistentMetadata()
    {
        var output = new
        {
            query = "Word to PDF",
            totalMatches = 1,
            returned = 2,
            moreResultsAvailable = true,
            outputTruncated = false,
            results = new[]
            {
                new { title = "One", url = "/one/" },
                new { title = "Two", url = "/two/" }
            }
        };
        var observation = JsonSerializer.Serialize(new
        {
            registeredTools = new[] { "search_site" },
            schemaType = "object",
            schemaQueryType = "string",
            schemaQueryMinimum = 1,
            schemaQueryMaximum = 200,
            schemaLimitType = "integer",
            schemaLimitMinimum = 1,
            schemaMaximum = 5,
            schemaDefault = 3,
            schemaRequired = new[] { "query" },
            schemaAdditionalProperties = false,
            readOnlyHint = true,
            untrustedContentHint = true,
            visibleQuery = "Word to PDF",
            visibleResultText = "Unrelated initial result",
            visibleResultUrls = new[] { "https://example.test/unrelated/" },
            visibleResultChanged = false,
            outputResultUrls = new[] { "https://example.test/one/", "https://example.test/two/" },
            output,
            outputJson = JsonSerializer.Serialize(output)
        });

        var result = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions { Url = "https://example.test/search/", ToolName = "search_site" },
            "Word to PDF",
            observation);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("fewer total matches", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("more-results flag", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("visible results", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BehavioralObservation_RejectsUnboundedSchemaAndIgnoredRequestedLimit()
    {
        var output = new
        {
            query = "docs",
            totalMatches = 2,
            returned = 2,
            moreResultsAvailable = false,
            outputTruncated = false,
            results = new[]
            {
                new { title = "One", url = "/one/" },
                new { title = "Two", url = "/two/" }
            }
        };
        var observation = CreateBehavioralObservation(
            output,
            "docs",
            ["https://example.test/one/", "https://example.test/two/"],
            ["https://example.test/one/", "https://example.test/two/"],
            schemaQueryMaximum: 500,
            schemaAdditionalProperties: true);

        var result = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions
            {
                Url = "https://example.test/search/",
                ToolName = "search_site",
                Limit = 1
            },
            "docs",
            observation);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("input schema", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.Contains("effective limit of 1", StringComparison.Ordinal));
    }

    [Fact]
    public void BehavioralObservation_RequiresZeroResultsToChangeAndClearTheVisibleRegion()
    {
        var output = new
        {
            query = "no-such-result",
            totalMatches = 0,
            returned = 0,
            moreResultsAvailable = false,
            outputTruncated = false,
            results = Array.Empty<object>()
        };
        var staleObservation = CreateBehavioralObservation(
            output,
            "no-such-result",
            ["https://example.test/stale/"],
            [],
            visibleResultChanged: false);

        var stale = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions { Url = "https://example.test/search/", ToolName = "search_site" },
            "no-such-result",
            staleObservation);

        Assert.False(stale.Success);
        Assert.Contains(stale.Errors, error => error.Contains("zero-results state", StringComparison.Ordinal));

        var clearedObservation = CreateBehavioralObservation(
            output,
            "no-such-result",
            [],
            [],
            visibleResultChanged: true,
            visibleResultText: "No results found.");
        var cleared = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions { Url = "https://example.test/search/", ToolName = "search_site" },
            "no-such-result",
            clearedObservation);

        Assert.True(cleared.Success, string.Join("; ", cleared.Errors));
    }

    [Fact]
    public void BehavioralObservation_AllowsVisibleMatchesOmittedByTheResponseBudget()
    {
        var output = new
        {
            query = "long target",
            totalMatches = 1,
            returned = 0,
            moreResultsAvailable = true,
            outputTruncated = true,
            results = Array.Empty<object>()
        };
        var observation = CreateBehavioralObservation(
            output,
            "long target",
            ["https://example.test/visible-long-target/"],
            [],
            visibleResultChanged: true,
            visibleResultText: "Long target");

        var result = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions { Url = "https://example.test/search/", ToolName = "search_site" },
            "long target",
            observation);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.OutputTruncated);
        Assert.True(result.MoreResultsAvailable);
    }

    [Fact]
    public void BehavioralObservation_UsesCaseSensitiveUrlIdentity()
    {
        var output = new
        {
            query = "guide",
            totalMatches = 1,
            returned = 1,
            moreResultsAvailable = false,
            outputTruncated = false,
            results = new[] { new { title = "Guide", url = "/docs/guide/" } }
        };
        var observation = CreateBehavioralObservation(
            output,
            "guide",
            ["https://example.test/Docs/Guide/"],
            ["https://example.test/docs/guide/"],
            visibleResultChanged: true);

        var result = WebMcpBehavioralTester.ParseObservation(
            new WebMcpBehavioralTestOptions { Url = "https://example.test/search/", ToolName = "search_site" },
            "guide",
            observation);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("every URL", StringComparison.Ordinal));
    }

    [Fact]
    public void Verify_PageToolRequiresAnAdapterAssociatedWithItsExactToolName()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-webmcp-page-association-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "tools"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, "tools", "index.html"),
                """
                <!doctype html><html><body>
                <main data-webmcp-page-tool data-webmcp-tool-name="tool_a" data-webmcp-tool-description="Tool A." data-webmcp-read-only="true"></main>
                <main data-webmcp-page-tool data-webmcp-tool-name="tool_b" data-webmcp-tool-description="Tool B." data-webmcp-read-only="true"></main>
                <script src="/assets/tool-a.js" defer data-powerforge-webmcp data-webmcp-tool-name="tool_a"></script>
                </body></html>
                """);
            File.WriteAllText(Path.Combine(root, "assets", "tool-a.js"), "document.modelContext?.registerTool({ name: 'tool_a' });");
            var spec = WebMcpOnlySpec(agentsJson: false);
            spec.WebMcpTools =
            [
                new AgentWebMcpToolSpec { Name = "tool_a", Route = "/tools/", Description = "Tool A.", Kind = "page-tool" },
                new AgentWebMcpToolSpec { Name = "tool_b", Route = "/tools/", Description = "Tool B.", Kind = "page-tool" }
            ];

            var verified = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = spec
            });

            var check = Assert.Single(verified.Checks, candidate => candidate.Id == "webmcp");
            Assert.Equal("fail", check.Status);
            Assert.Contains("tool_b", check.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateBehavioralObservation(
        object output,
        string query,
        string[] visibleResultUrls,
        string[] outputResultUrls,
        int schemaQueryMaximum = 200,
        bool schemaAdditionalProperties = false,
        bool visibleResultChanged = true,
        string visibleResultText = "Results")
    {
        return JsonSerializer.Serialize(new
        {
            registeredTools = new[] { "search_site" },
            schemaType = "object",
            schemaQueryType = "string",
            schemaQueryMinimum = 1,
            schemaQueryMaximum,
            schemaLimitType = "integer",
            schemaLimitMinimum = 1,
            schemaMaximum = 5,
            schemaDefault = 3,
            schemaRequired = new[] { "query" },
            schemaAdditionalProperties,
            readOnlyHint = true,
            untrustedContentHint = true,
            visibleQuery = query,
            visibleResultText,
            visibleResultUrls,
            visibleResultChanged,
            outputResultUrls,
            output,
            outputJson = JsonSerializer.Serialize(output)
        });
    }

    private static string FindWebMcpRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Schemas", "powerforge.web.pipelinespec.schema.json")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the PowerForge repository root.");
    }
}

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerWordPressExportSnapshotTests
{
    [Fact]
    public async Task RunPipeline_WordPressExportSnapshot_WritesRawSnapshotAndSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var port = GetFreePort();
            (listener, cts, serverTask, _) = StartWordPressExportServer(port);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "wordpress-export-snapshot",
                      "siteUrl": "http://127.0.0.1:{{port}}/",
                      "out": "./snapshot",
                      "collections": [ "posts", "pages" ],
                      "languages": [ "en", "pl" ],
                      "recordsPerPage": 1,
                      "includeEmbed": true,
                      "summaryPath": "./snapshot/_reports/summary.json",
                      "manifestPath": "./snapshot/snapshot.manifest.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.Contains("wordpress-export-snapshot ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var enPostsPath = Path.Combine(root, "snapshot", "raw", "en", "posts.json");
            var enPagesPath = Path.Combine(root, "snapshot", "raw", "en", "pages.json");
            var plPostsPath = Path.Combine(root, "snapshot", "raw", "pl", "posts.json");
            var plPagesPath = Path.Combine(root, "snapshot", "raw", "pl", "pages.json");
            Assert.True(File.Exists(enPostsPath));
            Assert.True(File.Exists(enPagesPath));
            Assert.True(File.Exists(plPostsPath));
            Assert.True(File.Exists(plPagesPath));

            Assert.Equal(2, CountJsonArray(enPostsPath));
            Assert.Equal(1, CountJsonArray(enPagesPath));
            Assert.Equal(1, CountJsonArray(plPostsPath));
            Assert.Equal(0, CountJsonArray(plPagesPath));

            var summaryPath = Path.Combine(root, "snapshot", "_reports", "summary.json");
            var manifestPath = Path.Combine(root, "snapshot", "snapshot.manifest.json");
            Assert.True(File.Exists(summaryPath));
            Assert.True(File.Exists(manifestPath));

            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            Assert.Equal("http://127.0.0.1:" + port + "/", summary.RootElement.GetProperty("sourceUrl").GetString());
            Assert.Equal(2, summary.RootElement.GetProperty("counts").GetProperty("en").GetProperty("posts").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("counts").GetProperty("pl").GetProperty("posts").GetInt32());
            Assert.Equal(0, summary.RootElement.GetProperty("warnings").GetArrayLength());
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_WordPressExportSnapshot_FailsWhenOutputExistsAndForceIsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-export-force-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var outputDir = Path.Combine(root, "snapshot");
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "already.txt"), "keep");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "wordpress-export-snapshot",
                      "siteUrl": "http://127.0.0.1:65530/",
                      "out": "./snapshot",
                      "collections": [ "posts" ],
                      "languages": [ "en" ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("already contains files", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunPipeline_WordPressExportSnapshot_SupportsBasicAuthAndQueryOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-wp-export-auth-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        HttpListener? listener = null;
        CancellationTokenSource? cts = null;
        Task? serverTask = null;

        try
        {
            var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("demo-user:demo-pass"));
            var port = GetFreePort();
            var options = new WordPressExportServerOptions
            {
                RequiredAuthorization = expectedAuth
            };
            WordPressExportServerState state;
            (listener, cts, serverTask, state) = StartWordPressExportServer(port, options);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                $$"""
                {
                  "steps": [
                    {
                      "task": "wordpress-export-snapshot",
                      "siteUrl": "http://127.0.0.1:{{port}}/",
                      "out": "./snapshot",
                      "collections": [ "posts" ],
                      "languages": [ "en" ],
                      "authMode": "basic",
                      "username": "demo-user",
                      "password": "demo-pass",
                      "status": "draft",
                      "after": "2025-01-01T00:00:00Z",
                      "before": "2026-01-01T00:00:00Z",
                      "search": "network",
                      "includeIds": [ 101, 102 ],
                      "excludeIds": [ 201 ],
                      "query": {
                        "orderby": "date",
                        "order": "desc"
                      },
                      "perCollectionQuery": {
                        "posts": {
                          "status": "any"
                        }
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            Assert.Equal(expectedAuth, state.LastAuthorization);
            Assert.NotNull(state.LastPostsQuery);
            Assert.Equal("any", state.LastPostsQuery!["status"]);
            Assert.Equal("date", state.LastPostsQuery["orderby"]);
            Assert.Equal("desc", state.LastPostsQuery["order"]);
            Assert.Equal("2025-01-01T00:00:00Z", state.LastPostsQuery["after"]);
            Assert.Equal("2026-01-01T00:00:00Z", state.LastPostsQuery["before"]);
            Assert.Equal("network", state.LastPostsQuery["search"]);
            Assert.Equal("101,102", state.LastPostsQuery["include"]);
            Assert.Equal("201", state.LastPostsQuery["exclude"]);
        }
        finally
        {
            await StopServerAsync(listener, cts, serverTask);
            TryDeleteDirectory(root);
        }
    }

    private static int CountJsonArray(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
    }

    private static (HttpListener listener, CancellationTokenSource cts, Task serverTask, WordPressExportServerState state) StartWordPressExportServer(
        int port,
        WordPressExportServerOptions? options = null)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var cts = new CancellationTokenSource();
        var state = new WordPressExportServerState();

        var content = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["posts|en"] = new List<string>
            {
                """{"id":101,"slug":"en-post-1"}""",
                """{"id":102,"slug":"en-post-2"}"""
            },
            ["pages|en"] = new List<string>
            {
                """{"id":201,"slug":"en-page-1"}"""
            },
            ["posts|pl"] = new List<string>
            {
                """{"id":301,"slug":"pl-post-1"}"""
            },
            ["pages|pl"] = new List<string>()
        };

        var serverTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (cts.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cts.IsCancellationRequested)
                {
                    break;
                }

                if (context is null)
                    continue;

                state.LastAuthorization = context.Request.Headers["Authorization"];

                var path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (!path.StartsWith("/wp-json/wp/v2/", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(context.Response, 404, """{"code":"not_found"}""");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(options?.RequiredAuthorization))
                {
                    var authorization = context.Request.Headers["Authorization"];
                    if (!string.Equals(authorization, options.RequiredAuthorization, StringComparison.Ordinal))
                    {
                        WriteJson(context.Response, 401, """{"code":"rest_not_logged_in"}""");
                        continue;
                    }
                }

                var endpoint = path.Substring("/wp-json/wp/v2/".Length).Trim('/');
                var query = ParseQuery(context.Request.Url?.Query);
                if (endpoint.Equals("posts", StringComparison.OrdinalIgnoreCase))
                    state.LastPostsQuery = new Dictionary<string, string>(query, StringComparer.OrdinalIgnoreCase);
                var language = query.TryGetValue("wpml_language", out var languageValue) &&
                               !string.IsNullOrWhiteSpace(languageValue)
                    ? languageValue
                    : "default";

                if (!int.TryParse(query.TryGetValue("page", out var pageRaw) ? pageRaw : "1", out var page) || page <= 0)
                    page = 1;
                if (!int.TryParse(query.TryGetValue("per_page", out var perPageRaw) ? perPageRaw : "100", out var perPage) || perPage <= 0)
                    perPage = 100;

                var key = endpoint + "|" + language;
                if (!content.TryGetValue(key, out var items))
                    items = new List<string>();

                var totalPages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)perPage));
                if (page > totalPages)
                {
                    WriteJson(context.Response, 400, """{"code":"rest_post_invalid_page_number"}""");
                    continue;
                }

                var offset = (page - 1) * perPage;
                var pageItems = items.Skip(offset).Take(perPage);
                var payload = "[" + string.Join(",", pageItems) + "]";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-WP-TotalPages"] = totalPages.ToString()
                };
                WriteJson(context.Response, 200, payload, headers);
            }
        }, cts.Token);

        return (listener, cts, serverTask, state);
    }

    private static Dictionary<string, string> ParseQuery(string? queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString))
            return result;

        var value = queryString.StartsWith("?", StringComparison.Ordinal) ? queryString.Substring(1) : queryString;
        foreach (var pair in value.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = pair.IndexOf('=');
            if (index < 0)
            {
                var keyOnly = Uri.UnescapeDataString(pair);
                if (!string.IsNullOrWhiteSpace(keyOnly))
                    result[keyOnly] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair.Substring(0, index));
            var itemValue = Uri.UnescapeDataString(pair.Substring(index + 1));
            if (!string.IsNullOrWhiteSpace(key))
                result[key] = itemValue;
        }

        return result;
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, string json, Dictionary<string, string>? headers = null)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";

        if (headers is not null)
        {
            foreach (var pair in headers)
                response.Headers[pair.Key] = pair.Value;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private static async Task StopServerAsync(HttpListener? listener, CancellationTokenSource? cts, Task? serverTask)
    {
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
        }

        if (listener is not null)
        {
            try { listener.Stop(); } catch { }
            try { listener.Close(); } catch { }
        }

        if (serverTask is not null)
        {
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }

        cts?.Dispose();
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
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

    private sealed class WordPressExportServerOptions
    {
        public string? RequiredAuthorization { get; set; }
    }

    private sealed class WordPressExportServerState
    {
        public string? LastAuthorization { get; set; }
        public Dictionary<string, string>? LastPostsQuery { get; set; }
    }
}

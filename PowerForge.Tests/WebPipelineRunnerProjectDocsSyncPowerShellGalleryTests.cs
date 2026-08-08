using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerProjectDocsSyncPowerShellGalleryTests
{
    [Fact]
    public async Task RunPipeline_ProjectDocsSync_HydratesMissingPowerShellApiFromExactGalleryPackageWithoutArtifactToken()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-gallery-" + Guid.NewGuid().ToString("N"));
        var port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Directory.CreateDirectory(root);

        try
        {
            var packageBytes = CreateHelpPackage("Sample-help.xml", "<helpItems />");
            string? requestPath = null;
            string? authorization = null;
            var serverTask = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                requestPath = context.Request.RawUrl;
                authorization = context.Request.Headers["Authorization"];
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/octet-stream";
                context.Response.ContentLength64 = packageBytes.Length;
                await context.Response.OutputStream.WriteAsync(packageBytes);
                context.Response.Close();
            });

            WriteCatalog(root,
                """
                {
                  "slug": "sample",
                  "contentMode": "hybrid",
                  "surfaces": { "apiPowerShell": true },
                  "links": { "apiPowerShell": "https://example.test/api" },
                  "metrics": {
                    "powerShellGallery": { "id": "Sample Module", "version": "1.2.3-preview.1" }
                  }
                }
                """);
            WritePipeline(root, $"http://127.0.0.1:{port}/api/v2/package", includeArtifactToken: true);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result.Success);
            Assert.Equal("/api/v2/package/Sample%20Module/1.2.3-preview.1", requestPath);
            Assert.Null(authorization);
            Assert.True(File.Exists(Path.Combine(root, "data", "apidocs", "sample", "Sample-help.xml")));
            Assert.Contains("api=1/1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            listener.Stop();
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectDocsSync_DoesNotReplaceExistingApiWithGalleryPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-docs-gallery-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "slug": "sample",
                  "contentMode": "hybrid",
                  "surfaces": { "apiPowerShell": true },
                  "metrics": {
                    "powerShellGallery": { "id": "Sample", "version": "1.2.3" }
                  }
                }
                """);
            WritePipeline(root, "http://127.0.0.1:1/api/v2/package", includeArtifactToken: false);

            var sourceRoot = Path.Combine(root, "projects-sources", "sample", "en-US");
            Directory.CreateDirectory(sourceRoot);
            File.WriteAllText(Path.Combine(sourceRoot, "Sample-help.xml"), "<local />");

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.True(result.Success);
            var copiedHelp = File.ReadAllText(Path.Combine(root, "data", "apidocs", "sample", "Sample-help.xml"));
            Assert.Equal("<local />", copiedHelp);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static byte[] CreateHelpPackage(string fileName, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("en-US/" + fileName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return stream.ToArray();
    }

    private static void WriteCatalog(string root, string projectJson)
    {
        var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, $$"""{ "projects": [ {{projectJson}} ] }""");
    }

    private static void WritePipeline(string root, string packageBaseUrl, bool includeArtifactToken)
    {
        var artifactToken = includeArtifactToken ? "\"artifactToken\": \"must-not-leak\"," : string.Empty;
        File.WriteAllText(Path.Combine(root, "pipeline.json"),
            $$"""
            {
              "steps": [
                {
                  "task": "project-docs-sync",
                  "catalog": "./data/projects/catalog.json",
                  "sourcesRoot": "./projects-sources",
                  "contentRoot": "./content/docs",
                  "syncDocs": false,
                  "syncApi": true,
                  "syncExamples": false,
                  "apiRoot": "./data/apidocs",
                  "sourceApiPaths": ["en-US"],
                  "hydrateFromArtifacts": true,
                  "onlyLocalLinks": true,
                  "powerShellGalleryPackageBaseUrl": "{{packageBaseUrl}}",
                  {{artifactToken}}
                  "failOnMissingApiSource": true,
                  "strict": true
                }
              ]
            }
            """);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }
}

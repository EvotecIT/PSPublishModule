using System;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecutePortalDocsIndex(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outputDirectory = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputDirectory") ??
            GetString(step, "output-directory") ??
            "./data/portal");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("portal-docs-index requires out/outputDirectory.");

        var sourcesPath = ResolvePath(baseDir,
            GetString(step, "sources") ??
            GetString(step, "source") ??
            GetString(step, "sourcesFile") ??
            GetString(step, "sources-file") ??
            GetString(step, "config") ??
            "./portal.sources.json");

        var privateGalleryPath = ResolvePath(baseDir,
            GetString(step, "privateGallery") ??
            GetString(step, "private-gallery") ??
            GetString(step, "privateGalleryFeed") ??
            GetString(step, "private-gallery-feed") ??
            GetString(step, "gallery") ??
            "./data/private-gallery/feed.json");

        var token = GetString(step, "token");
        var tokenEnv = GetString(step, "tokenEnv") ??
                       GetString(step, "token-env") ??
                       GetString(step, "tokenEnvironmentVariable") ??
                       GetString(step, "token-environment-variable");

        var result = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
        {
            OutputDirectory = outputDirectory,
            BaseDirectory = baseDir,
            SourcesPath = sourcesPath,
            PrivateGalleryPath = privateGalleryPath,
            IncludeContent = GetBool(step, "includeContent") ?? GetBool(step, "include-content") ?? true,
            MaxContentBytes = GetInt(step, "maxContentBytes") ?? GetInt(step, "max-content-bytes") ?? 262144,
            RequestTimeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 30,
            Token = token,
            TokenEnvironmentVariable = tokenEnv
        });

        var warningNote = result.Warnings.Length > 0
            ? $"; warnings={result.Warnings.Length}"
            : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"portal-docs-index ok: sources={result.SourceCount}; documents={result.DocumentCount}; docs={result.DocsPath}; search={result.SearchPath}{warningNote}";
    }
}

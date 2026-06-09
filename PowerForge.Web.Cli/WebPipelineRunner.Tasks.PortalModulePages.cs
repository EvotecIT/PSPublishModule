using System;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecutePortalModulePages(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var rawOutputDirectory =
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputDirectory") ??
            GetString(step, "output-directory") ??
            "./content/generated/modules";
        if (string.IsNullOrWhiteSpace(rawOutputDirectory))
            throw new InvalidOperationException("portal-module-pages requires out/outputDirectory.");
        var outputDirectory = ResolvePath(baseDir, rawOutputDirectory);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("portal-module-pages output path could not be resolved.");

        var privateGalleryPath = ResolvePath(baseDir,
            GetString(step, "privateGallery") ??
            GetString(step, "private-gallery") ??
            GetString(step, "privateGalleryFeed") ??
            GetString(step, "private-gallery-feed") ??
            GetString(step, "gallery") ??
            "./data/private-gallery/feed.json");

        var portalDocsPath = ResolvePath(baseDir,
            GetString(step, "portalDocs") ??
            GetString(step, "portal-docs") ??
            GetString(step, "portalDocsIndex") ??
            GetString(step, "portal-docs-index") ??
            GetString(step, "docs") ??
            "./data/portal/docs.json");

        var result = WebPrivateGalleryPageGenerator.Generate(new WebPrivateGalleryPageOptions
        {
            BaseDirectory = baseDir,
            PrivateGalleryPath = privateGalleryPath!,
            PortalDocsPath = portalDocsPath,
            OutputDirectory = outputDirectory!,
            ProfileName = GetString(step, "profileName") ?? GetString(step, "profile-name"),
            RepositoryName = GetString(step, "repositoryName") ?? GetString(step, "repository-name"),
            Layout = GetString(step, "layout") ?? "page",
            IndexLayout = GetString(step, "indexLayout") ?? GetString(step, "index-layout"),
            ModuleLayout = GetString(step, "moduleLayout") ?? GetString(step, "module-layout"),
            DocumentLayout = GetString(step, "documentLayout") ?? GetString(step, "document-layout"),
            GenerateDocumentPages = GetBool(step, "generateDocumentPages") ??
                                    GetBool(step, "generate-document-pages") ??
                                    true
        });

        var warningNote = result.Warnings.Length > 0
            ? $"; warnings={result.Warnings.Length}"
            : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"portal-module-pages ok: modules={result.ModulePageCount}; docs={result.DocumentPageCount}; out={result.OutputDirectory}{warningNote}";
    }
}

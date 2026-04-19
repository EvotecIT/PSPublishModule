using System;
using System.Text.Json;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteTask(
        string task,
        JsonElement step,
        string label,
        string baseDir,
        bool fast,
        string effectiveMode,
        WebConsoleLogger? logger,
        ref string lastBuildOutPath,
        ref string[] lastBuildUpdatedFiles,
        WebPipelineStepResult stepResult)
    {
        switch (task)
        {
            case "build":
                ExecuteBuild(step, baseDir, logger, ref lastBuildOutPath, ref lastBuildUpdatedFiles, stepResult);
                break;
            case "nav-export":
                ExecuteNavExport(step, baseDir, stepResult);
                break;
            case "verify":
                ExecuteVerify(step, baseDir, fast, effectiveMode, stepResult);
                break;
            case "markdown-fix":
                ExecuteMarkdownFix(step, baseDir, stepResult);
                break;
            case "apidocs":
                ExecuteApiDocs(step, label, baseDir, fast, effectiveMode, logger, stepResult);
                break;
            case "project-apidocs":
            case "project-apidoc":
            case "project-api-docs":
                ExecuteProjectApiDocs(step, label, baseDir, fast, effectiveMode, logger, lastBuildOutPath, stepResult);
                break;
            case "changelog":
                ExecuteChangelog(step, baseDir, stepResult);
                break;
            case "release-hub":
                ExecuteReleaseHub(step, baseDir, stepResult);
                break;
            case "version-hub":
                ExecuteVersionHub(step, baseDir, stepResult);
                break;
            case "package-hub":
                ExecutePackageHub(step, baseDir, stepResult);
                break;
            case "llms":
                ExecuteLlms(step, baseDir, stepResult);
                break;
            case "compat-matrix":
                ExecuteCompatibilityMatrix(step, baseDir, stepResult);
                break;
            case "sitemap":
                ExecuteSitemap(step, baseDir, lastBuildOutPath, stepResult);
                break;
            case "agent-ready":
            case "agentready":
                ExecuteAgentReady(step, baseDir, lastBuildOutPath, stepResult);
                break;
            case "xref-merge":
                ExecuteXrefMerge(step, label, baseDir, fast, effectiveMode, logger, stepResult);
                break;
            case "optimize":
                ExecuteOptimize(step, label, baseDir, fast, logger, lastBuildOutPath, lastBuildUpdatedFiles, stepResult);
                break;
            case "audit":
                ExecuteAudit(step, label, baseDir, fast, logger, lastBuildOutPath, lastBuildUpdatedFiles, stepResult);
                break;
            case "seo-doctor":
                ExecuteSeoDoctor(step, baseDir, fast, lastBuildOutPath, lastBuildUpdatedFiles, stepResult);
                break;
            case "doctor":
                ExecuteDoctor(step, baseDir, fast, effectiveMode, stepResult);
                break;
            case "dotnet-build":
                ExecuteDotNetBuild(step, baseDir, stepResult);
                break;
            case "dotnet-publish":
                ExecuteDotNetPublish(step, baseDir, stepResult);
                break;
            case "overlay":
                ExecuteOverlay(step, baseDir, stepResult);
                break;
            case "route-fallbacks":
            case "routefallbacks":
            case "templated-routes":
                ExecuteRouteFallbacks(step, baseDir, stepResult);
                break;
            case "hosting":
                ExecuteHosting(step, baseDir, stepResult);
                break;
            case "cloudflare":
                ExecuteCloudflare(step, baseDir, stepResult);
                break;
            case "engine-lock":
            case "enginelock":
                ExecuteEngineLock(step, baseDir, stepResult);
                break;
            case "github-artifacts-prune":
            case "github-artifacts":
                ExecuteGitHubArtifactsPrune(step, baseDir, stepResult);
                break;
            case "indexnow":
                ExecuteIndexNow(step, baseDir, fast, lastBuildOutPath, lastBuildUpdatedFiles, stepResult);
                break;
            case "hook":
                ExecuteHook(step, label, baseDir, effectiveMode, stepResult);
                break;
            case "html-transform":
                ExecuteHtmlTransform(step, label, baseDir, stepResult);
                break;
            case "data-transform":
                ExecuteDataTransform(step, baseDir, stepResult);
                break;
            case "model-transform":
                ExecuteModelTransform(step, baseDir, stepResult);
                break;
            case "exec":
                ExecuteExec(step, baseDir, stepResult);
                break;
            case "git-sync":
                ExecuteGitSync(step, baseDir, logger, stepResult);
                break;
            case "sources-sync":
                ExecuteSourcesSync(step, baseDir, logger, stepResult);
                break;
            case "ecosystem-stats":
            case "ecosystemstats":
                ExecuteEcosystemStats(step, baseDir, stepResult);
                break;
            case "search-index-export":
            case "search-index":
            case "search-export":
                ExecuteSearchIndexExport(step, baseDir, stepResult);
                break;
            case "project-docs-sync":
            case "sync-project-docs":
            case "project-docs":
                ExecuteProjectDocsSync(step, baseDir, stepResult);
                break;
            case "project-catalog":
            case "projectcatalog":
                ExecuteProjectCatalog(step, baseDir, stepResult);
                break;
            case "apache-redirects":
            case "apache-redirect":
                ExecuteApacheRedirects(step, baseDir, stepResult);
                break;
            case "links-validate":
            case "link-validate":
            case "links":
                ExecuteLinksValidate(step, baseDir, stepResult);
                break;
            case "links-export-apache":
            case "link-export-apache":
            case "links-export":
                ExecuteLinksExportApache(step, baseDir, stepResult);
                break;
            case "links-import-wordpress":
            case "link-import-wordpress":
            case "links-import-pretty-links":
            case "links-import":
                ExecuteLinksImportWordPress(step, baseDir, stepResult);
                break;
            case "links-report-404":
            case "link-report-404":
            case "links-report":
                ExecuteLinksReport404(step, baseDir, stepResult);
                break;
            case "links-promote-404":
            case "link-promote-404":
            case "links-promote":
                ExecuteLinksPromote404(step, baseDir, stepResult);
                break;
            case "links-ignore-404":
            case "link-ignore-404":
            case "links-ignore":
                ExecuteLinksIgnore404(step, baseDir, stepResult);
                break;
            case "links-apply-review":
            case "link-apply-review":
            case "links-apply":
                ExecuteLinksApplyReview(step, baseDir, stepResult);
                break;
            case "wordpress-normalize":
            case "wordpress-normalize-content":
            case "normalize-wordpress-content":
                ExecuteWordPressNormalize(step, baseDir, stepResult);
                break;
            case "wordpress-media-sync":
            case "wordpress-sync-media":
            case "sync-wordpress-media":
                ExecuteWordPressMediaSync(step, baseDir, stepResult);
                break;
            case "wordpress-import-snapshot":
            case "wordpress-snapshot-import":
            case "import-wordpress-snapshot":
                ExecuteWordPressImportSnapshot(step, baseDir, stepResult);
                break;
            case "wordpress-export-snapshot":
            case "wordpress-snapshot-export":
            case "export-wordpress-snapshot":
                ExecuteWordPressExportSnapshot(step, baseDir, stepResult);
                break;
            default:
                stepResult.Success = false;
                stepResult.Message = "Unknown task";
                break;
        }
    }
}

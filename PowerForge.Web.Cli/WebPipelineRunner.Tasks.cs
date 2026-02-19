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
                ExecuteBuild(step, baseDir, ref lastBuildOutPath, ref lastBuildUpdatedFiles, stepResult);
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
            case "changelog":
                ExecuteChangelog(step, baseDir, stepResult);
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
            case "hosting":
                ExecuteHosting(step, baseDir, stepResult);
                break;
            case "cloudflare":
                ExecuteCloudflare(step, baseDir, stepResult);
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
            default:
                stepResult.Success = false;
                stepResult.Message = "Unknown task";
                break;
        }
    }
}

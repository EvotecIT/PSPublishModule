using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteNavExport(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var config = ResolvePath(baseDir, GetString(step, "config"));
        if (string.IsNullOrWhiteSpace(config))
            throw new InvalidOperationException("nav-export requires config.");

        var outValue = GetString(step, "out") ?? GetString(step, "output");
        var overwrite = GetBool(step, "overwrite") ?? false;

        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        var outPath = string.IsNullOrWhiteSpace(outValue)
            ? GetDefaultNavExportOutputPath(spec, plan.RootPath)
            : ResolvePath(baseDir, outValue) ?? throw new InvalidOperationException("nav-export requires out to resolve.");

        var result = WebSiteBuilder.ExportSiteNavJson(spec, plan, outPath, overwrite, WebCliJson.Options);
        if (!result.Success)
            throw new InvalidOperationException(result.Message ?? "Nav export failed.");

        stepResult.Success = true;
        stepResult.Message = result.Changed ? "nav-export updated" : "nav-export ok";
    }

    private static string GetDefaultNavExportOutputPath(SiteSpec spec, string rootPath)
    {
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var relativeRoot = Path.IsPathRooted(dataRoot)
            ? "data"
            : dataRoot.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(relativeRoot))
            relativeRoot = "data";

        return Path.Combine(rootPath, "static", relativeRoot, "site-nav.json");
    }
}

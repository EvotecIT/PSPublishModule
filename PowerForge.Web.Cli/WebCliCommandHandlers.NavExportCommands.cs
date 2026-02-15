using System;
using System.IO;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleNavExport(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--output") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var overwrite = HasOption(subArgs, "--overwrite");

        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.nav-export");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        var resolvedOut = string.IsNullOrWhiteSpace(outPath)
            ? GetDefaultNavExportOutputPath(spec, plan)
            : ResolveOutputPath(plan.RootPath, outPath);

        var result = WebSiteBuilder.ExportSiteNavJson(spec, plan, resolvedOut, overwrite, WebCliJson.Options);
        var exitCode = result.Success ? 0 : 1;

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.nav-export",
                Success = result.Success,
                ExitCode = exitCode,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebNavExportResult)
            });
            return exitCode;
        }

        if (!result.Success)
        {
            logger.Error(result.Message ?? "Nav export failed.");
            return exitCode;
        }

        logger.Success($"Nav export: {result.OutputPath}");
        logger.Info(result.Changed ? "Nav export updated." : "Nav export up-to-date.");
        return 0;
    }

    private static string GetDefaultNavExportOutputPath(SiteSpec spec, WebSitePlan plan)
    {
        var dataRoot = string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot;
        var relativeRoot = Path.IsPathRooted(dataRoot)
            ? "data"
            : dataRoot.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(relativeRoot))
            relativeRoot = "data";

        return Path.Combine(plan.RootPath, "static", relativeRoot, "site-nav.json");
    }

    private static string ResolveOutputPath(string rootPath, string outputPath)
    {
        var trimmed = outputPath.Trim().Trim('"');
        return Path.IsPathRooted(trimmed)
            ? Path.GetFullPath(trimmed)
            : Path.GetFullPath(Path.Combine(rootPath, trimmed));
    }
}

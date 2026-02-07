using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private const string DefaultArchetypeTemplate = """
---
title: {{title}}
slug: {{slug}}
date: {{date}}
collection: {{collection}}
---

# {{title}}
""";

    internal static int HandleSubCommand(string subCommand, string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        return subCommand switch
        {
            "plan" => HandlePlan(subArgs, outputJson, logger, outputSchemaVersion),
            "build" => HandleBuild(subArgs, outputJson, logger, outputSchemaVersion),
            "publish" => HandlePublish(subArgs, outputJson, logger, outputSchemaVersion),
            "verify" => HandleVerify(subArgs, outputJson, logger, outputSchemaVersion),
            "doctor" => HandleDoctor(subArgs, outputJson, logger, outputSchemaVersion),
            "markdown-fix" => HandleMarkdownFix(subArgs, outputJson, logger, outputSchemaVersion),
            "audit" => HandleAudit(subArgs, outputJson, logger, outputSchemaVersion),
            "scaffold" => HandleScaffold(subArgs, outputJson, logger, outputSchemaVersion),
            "new" => HandleNew(subArgs, outputJson, logger, outputSchemaVersion),
            "serve" => HandleServe(subArgs, outputJson, logger),
            "apidocs" => HandleApiDocs(subArgs, outputJson, logger, outputSchemaVersion),
            "changelog" => HandleChangelog(subArgs, outputJson, logger, outputSchemaVersion),
            "optimize" => HandleOptimize(subArgs, outputJson, logger, outputSchemaVersion),
            "pipeline" => HandlePipeline(subArgs, outputJson, logger, outputSchemaVersion),
            "dotnet-build" => HandleDotNetBuild(subArgs, outputJson, outputSchemaVersion, logger),
            "dotnet-publish" => HandleDotNetPublish(subArgs, outputJson, outputSchemaVersion, logger),
            "overlay" => HandleOverlay(subArgs, outputJson, logger, outputSchemaVersion),
            "llms" => HandleLlms(subArgs, outputJson, logger, outputSchemaVersion),
            "sitemap" => HandleSitemap(subArgs, outputJson, logger, outputSchemaVersion),
            _ => HandleUnknownCommand()
        };
    }

    private static int HandleUnknownCommand()
    {
        PrintUsage();
        return 2;
    }
}

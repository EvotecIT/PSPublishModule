using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleAgentReady(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (subArgs.Length == 0)
            return Fail("agent-ready requires an operation: prepare, verify, or scan.", outputJson, logger, "web.agent-ready");

        var operation = subArgs[0].Trim();
        var args = subArgs.Skip(1).ToArray();
        return operation.ToLowerInvariant() switch
        {
            "prepare" => HandleAgentReadyPrepare(args, outputJson, logger, outputSchemaVersion),
            "verify" => HandleAgentReadyVerify(args, outputJson, logger, outputSchemaVersion),
            "scan" => HandleAgentReadyScan(args, outputJson, logger, outputSchemaVersion),
            _ => Fail("agent-ready operation must be prepare, verify, or scan.", outputJson, logger, "web.agent-ready")
        };
    }

    private static int HandleAgentReadyPrepare(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(args, "--config");
        var siteRoot = TryGetOptionValue(args, "--site-root") ?? TryGetOptionValue(args, "--out");
        var baseUrl = TryGetOptionValue(args, "--base-url") ?? TryGetOptionValue(args, "--url");
        var failOnFailures = HasOption(args, "--fail-on-failures") || HasOption(args, "--fail-on-warnings");
        SiteSpec? siteSpec = null;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var loaded = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
            siteSpec = loaded.Spec;
            baseUrl ??= siteSpec.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.agent-ready.prepare");

        var result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            SiteName = siteSpec?.Name,
            AgentReadiness = siteSpec?.AgentReadiness
        });

        return WriteAgentReadyResult(result, outputJson, logger, outputSchemaVersion, "web.agent-ready.prepare", failOnFailures);
    }

    private static int HandleAgentReadyVerify(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(args, "--config");
        var siteRoot = TryGetOptionValue(args, "--site-root") ?? TryGetOptionValue(args, "--out");
        var baseUrl = TryGetOptionValue(args, "--base-url") ?? TryGetOptionValue(args, "--url");
        var failOnFailures = HasOption(args, "--fail-on-failures") || HasOption(args, "--fail-on-warnings");
        SiteSpec? siteSpec = null;

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var loaded = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
            siteSpec = loaded.Spec;
            baseUrl ??= siteSpec.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(siteRoot))
            return Fail("Missing required --site-root.", outputJson, logger, "web.agent-ready.verify");

        var result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
        {
            SiteRoot = siteRoot,
            BaseUrl = baseUrl,
            AgentReadiness = siteSpec?.AgentReadiness
        });

        return WriteAgentReadyResult(result, outputJson, logger, outputSchemaVersion, "web.agent-ready.verify", failOnFailures);
    }

    private static int HandleAgentReadyScan(string[] args, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var url = TryGetOptionValue(args, "--url") ?? TryGetOptionValue(args, "--base-url");
        var timeoutMs = TryParseInt(TryGetOptionValue(args, "--timeout-ms")) ?? 15000;
        var failOnFailures = HasOption(args, "--fail-on-failures") || HasOption(args, "--fail-on-warnings");

        if (string.IsNullOrWhiteSpace(url))
            return Fail("Missing required --url.", outputJson, logger, "web.agent-ready.scan");

        var result = WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
        {
            BaseUrl = url,
            TimeoutMs = timeoutMs
        }).GetAwaiter().GetResult();

        return WriteAgentReadyResult(result, outputJson, logger, outputSchemaVersion, "web.agent-ready.scan", failOnFailures);
    }

    private static int WriteAgentReadyResult(
        WebAgentReadinessResult result,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion,
        string command,
        bool failOnFailures)
    {
        var exitCode = failOnFailures && !result.Success ? 1 : 0;

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = exitCode == 0,
                ExitCode = exitCode,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebAgentReadinessResult)
            });
            return exitCode;
        }

        logger.Info($"Agent readiness {result.Operation}: {(result.Success ? "passed" : "issues found")}");
        if (result.WrittenFiles.Length > 0)
            logger.Info($"Written: {string.Join(", ", result.WrittenFiles)}");
        foreach (var check in result.Checks)
        {
            var line = $"[{check.Status}] {check.Name}: {check.Message}";
            if (string.Equals(check.Status, "fail", StringComparison.OrdinalIgnoreCase))
                logger.Error(line);
            else if (string.Equals(check.Status, "warn", StringComparison.OrdinalIgnoreCase))
                logger.Warn(line);
            else
                logger.Info(line);
        }

        foreach (var warning in result.Warnings)
            logger.Warn(warning);

        return exitCode;
    }

    private static int? TryParseInt(string? value)
        => int.TryParse(value, out var parsed) ? parsed : null;
}

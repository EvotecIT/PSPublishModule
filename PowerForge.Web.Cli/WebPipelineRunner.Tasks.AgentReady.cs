using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecuteAgentReady(JsonElement step, string baseDir, string lastBuildOutPath, WebPipelineStepResult stepResult)
    {
        var operation = GetString(step, "operation") ?? "prepare";
        var configPath = ResolvePath(baseDir, GetString(step, "config"));
        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root") ?? GetString(step, "out") ?? GetString(step, "output"));
        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? GetString(step, "url");
        var failOnFailures = GetBool(step, "failOnFailures") ?? GetBool(step, "fail-on-failures") ?? false;
        var timeoutMs = GetInt(step, "timeoutMs") ?? GetInt(step, "timeout-ms") ?? 15000;

        SiteSpec? siteSpec = null;
        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
        {
            var loaded = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
            siteSpec = loaded.Spec;
            baseUrl ??= siteSpec.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(siteRoot) && !string.IsNullOrWhiteSpace(lastBuildOutPath))
            siteRoot = lastBuildOutPath;

        if (operation.Trim().Equals("exercise", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("agent-ready exercise requires an exact WebMCP page URL.");
            var query = GetString(step, "query");
            if (string.IsNullOrWhiteSpace(query))
                throw new InvalidOperationException("agent-ready exercise requires query.");
            var behavioralResult = WebMcpBehavioralTester.TestSiteSearchAsync(new WebMcpBehavioralTestOptions
            {
                Url = baseUrl,
                ToolName = GetString(step, "toolName") ?? GetString(step, "tool-name") ?? "search_site",
                Query = query,
                Limit = GetInt(step, "limit") ?? 0,
                TimeoutMs = timeoutMs,
                EnsureBrowserInstalled = GetBool(step, "ensureBrowser") ?? GetBool(step, "ensure-browser") ?? false,
                Headless = !(GetBool(step, "headed") ?? false)
            }).GetAwaiter().GetResult();

            if (failOnFailures && !behavioralResult.Success)
                throw new InvalidOperationException($"agent-ready WebMCP exercise failed: {string.Join("; ", behavioralResult.Errors)}");
            stepResult.Success = true;
            stepResult.Message = $"Agent-ready exercise: {(behavioralResult.Success ? "passed" : "failed")}, {behavioralResult.Returned}/{behavioralResult.TotalMatches} results, {behavioralResult.OutputCharacters} output characters";
            return;
        }

        WebAgentReadinessResult result;
        switch (operation.Trim().ToLowerInvariant())
        {
            case "prepare":
                if (string.IsNullOrWhiteSpace(siteRoot))
                    throw new InvalidOperationException("agent-ready prepare requires siteRoot or a prior build step.");
                result = WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
                {
                    SiteRoot = siteRoot,
                    BaseUrl = baseUrl,
                    SiteName = siteSpec?.Name,
                    AgentReadiness = siteSpec?.AgentReadiness
                });
                break;
            case "verify":
                if (string.IsNullOrWhiteSpace(siteRoot))
                    throw new InvalidOperationException("agent-ready verify requires siteRoot or a prior build step.");
                result = WebAgentReadiness.Verify(new WebAgentReadinessVerifyOptions
                {
                    SiteRoot = siteRoot,
                    BaseUrl = baseUrl,
                    AgentReadiness = siteSpec?.AgentReadiness
                });
                break;
            case "scan":
                if (string.IsNullOrWhiteSpace(baseUrl))
                    throw new InvalidOperationException("agent-ready scan requires baseUrl/url or config.BaseUrl.");
                result = WebAgentReadiness.ScanAsync(new WebAgentReadinessScanOptions
                {
                    BaseUrl = baseUrl,
                    TimeoutMs = timeoutMs,
                    AgentReadiness = siteSpec?.AgentReadiness
                }).GetAwaiter().GetResult();
                break;
            default:
                throw new InvalidOperationException("agent-ready operation must be prepare, verify, scan, or exercise.");
        }

        if (failOnFailures && !result.Success)
        {
            var failed = result.Checks.FirstOrDefault(static c => string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase));
            throw new InvalidOperationException(failed is null
                ? "agent-ready checks failed."
                : $"agent-ready {failed.Name}: {failed.Message}");
        }

        stepResult.Success = true;
        var failedCount = result.Checks.Count(static c => string.Equals(c.Status, "fail", StringComparison.OrdinalIgnoreCase));
        var warnCount = result.Checks.Count(static c => string.Equals(c.Status, "warn", StringComparison.OrdinalIgnoreCase));
        stepResult.Message = $"Agent-ready {result.Operation}: {result.Checks.Length} checks, {failedCount} failed, {warnCount} warnings";
    }
}

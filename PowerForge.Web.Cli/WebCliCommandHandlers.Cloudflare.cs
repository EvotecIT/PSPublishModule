using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private const int CloudflareVerifyDefaultWarmupRequests = 1;
    private const int CloudflareVerifyMaxWarmupRequests = 10;

    private static int HandleCloudflare(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var verb = "purge";
        if (subArgs.Length > 0 && !subArgs[0].StartsWith("-", StringComparison.Ordinal))
        {
            verb = subArgs[0].Trim();
            subArgs = subArgs.Skip(1).ToArray();
        }

        if (verb.Equals("purge", StringComparison.OrdinalIgnoreCase))
            return HandleCloudflarePurge(subArgs, outputJson, logger, outputSchemaVersion);

        if (verb.Equals("verify", StringComparison.OrdinalIgnoreCase))
            return HandleCloudflareVerify(subArgs, outputJson, logger, outputSchemaVersion);

        return Fail($"Unknown cloudflare verb '{verb}'. Supported: purge, verify.", outputJson, logger, "web.cloudflare");
    }

    private static int HandleCloudflarePurge(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (!TryLoadCloudflareSiteProfile(subArgs, outputJson, logger, "web.cloudflare.purge", out var siteProfile, out var loadError))
            return loadError;

        var zoneId = TryGetOptionValue(subArgs, "--zone-id") ??
                     TryGetOptionValue(subArgs, "--zone") ??
                     TryGetOptionValue(subArgs, "--zoneId");
        if (string.IsNullOrWhiteSpace(zoneId))
            return Fail("Missing required --zone-id.", outputJson, logger, "web.cloudflare.purge");

        var token = TryGetOptionValue(subArgs, "--token") ??
                    TryGetOptionValue(subArgs, "--api-token") ??
                    TryGetOptionValue(subArgs, "--apiToken");
        var tokenEnv = TryGetOptionValue(subArgs, "--token-env") ??
                       TryGetOptionValue(subArgs, "--api-token-env") ??
                       TryGetOptionValue(subArgs, "--apiTokenEnv") ??
                       "CLOUDFLARE_API_TOKEN";
        if (string.IsNullOrWhiteSpace(token))
            token = Environment.GetEnvironmentVariable(tokenEnv);

        if (string.IsNullOrWhiteSpace(token))
            return Fail($"Missing Cloudflare API token. Provide --token or set env var '{tokenEnv}'.", outputJson, logger, "web.cloudflare.purge");

        var purgeEverything = HasOption(subArgs, "--purge-everything") || HasOption(subArgs, "--purgeEverything");
        var dryRun = HasOption(subArgs, "--dry-run") || HasOption(subArgs, "--dryRun");

        var baseUrl = TryGetOptionValue(subArgs, "--base-url") ??
                      TryGetOptionValue(subArgs, "--baseUrl") ??
                      siteProfile?.BaseUrl ??
                      Environment.GetEnvironmentVariable("POWERFORGE_BASE_URL");

        var urls = ReadOptionList(subArgs, "--url", "--urls");
        var paths = ReadOptionList(subArgs, "--path", "--paths");
        if (urls.Count == 0 && paths.Count == 0 && siteProfile is not null)
            paths.AddRange(siteProfile.PurgePaths);

        if (urls.Count == 0 && paths.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return Fail("Missing --base-url (required when using --path/--paths).", outputJson, logger, "web.cloudflare.purge");

            urls.AddRange(paths.Select(p => CombineUrl(baseUrl, p)));
        }

        var (ok, message) = CloudflareCachePurger.Purge(
            zoneId: zoneId,
            apiToken: token,
            purgeEverything: purgeEverything,
            fileUrls: urls,
            dryRun: dryRun,
            logger: logger);

        if (outputJson)
        {
            var element = JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["siteConfig"] = siteProfile?.SiteConfigPath,
                ["zoneId"] = zoneId,
                ["baseUrl"] = baseUrl,
                ["purgeEverything"] = purgeEverything,
                ["urlCount"] = urls.Count,
                ["dryRun"] = dryRun,
                ["message"] = message
            }, WebCliJson.Options);

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.cloudflare.purge",
                Success = ok,
                ExitCode = ok ? 0 : 1,
                Result = element
            });
            return ok ? 0 : 1;
        }

        if (ok) logger.Success(message);
        else logger.Error(message);
        return ok ? 0 : 1;
    }

    private static int HandleCloudflareVerify(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        if (!TryLoadCloudflareSiteProfile(subArgs, outputJson, logger, "web.cloudflare.verify", out var siteProfile, out var loadError))
            return loadError;

        var baseUrl = TryGetOptionValue(subArgs, "--base-url") ??
                      TryGetOptionValue(subArgs, "--baseUrl") ??
                      siteProfile?.BaseUrl ??
                      Environment.GetEnvironmentVariable("POWERFORGE_BASE_URL");

        var urls = ReadOptionList(subArgs, "--url", "--urls");
        var paths = ReadOptionList(subArgs, "--path", "--paths");
        if (urls.Count == 0 && paths.Count == 0 && siteProfile is not null)
            paths.AddRange(siteProfile.VerifyPaths);

        if (urls.Count == 0 && paths.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return Fail("Missing --base-url (required when using --path/--paths).", outputJson, logger, "web.cloudflare.verify");

            urls.AddRange(paths.Select(p => CombineUrl(baseUrl, p)));
        }

        if (urls.Count == 0)
            return Fail("Missing target URLs. Provide --url/--urls or --path/--paths.", outputJson, logger, "web.cloudflare.verify");

        var warmupRequests = ParseIntOption(
            TryGetOptionValue(subArgs, "--warmup") ??
            TryGetOptionValue(subArgs, "--warmup-requests") ??
            TryGetOptionValue(subArgs, "--warmupRequests"),
            CloudflareVerifyDefaultWarmupRequests);
        warmupRequests = Math.Clamp(warmupRequests, 0, CloudflareVerifyMaxWarmupRequests);

        var timeoutMs = ParseIntOption(
            TryGetOptionValue(subArgs, "--timeout-ms") ??
            TryGetOptionValue(subArgs, "--timeoutMs") ??
            TryGetOptionValue(subArgs, "--timeout"),
            15000);
        timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);

        var allowedStatuses = ReadOptionList(subArgs, "--allow-status", "--allow-statuses", "--allow");
        if (allowedStatuses.Count == 0)
            allowedStatuses.AddRange(CloudflareCacheVerifier.DefaultAllowedStatuses);

        var (ok, message, entries) = CloudflareCacheVerifier.Verify(
            urls: urls,
            warmupRequests: warmupRequests,
            allowedStatuses: allowedStatuses,
            timeoutMs: timeoutMs,
            logger: logger);

        if (outputJson)
        {
            var element = JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["siteConfig"] = siteProfile?.SiteConfigPath,
                ["baseUrl"] = baseUrl,
                ["urlCount"] = urls.Count,
                ["warmupRequests"] = warmupRequests,
                ["timeoutMs"] = timeoutMs,
                ["allowedStatuses"] = allowedStatuses,
                ["results"] = entries,
                ["message"] = message
            }, WebCliJson.Options);

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.cloudflare.verify",
                Success = ok,
                ExitCode = ok ? 0 : 1,
                Result = element
            });
            return ok ? 0 : 1;
        }

        if (ok) logger.Success(message);
        else logger.Error(message);
        return ok ? 0 : 1;
    }

    private static bool TryLoadCloudflareSiteProfile(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        string command,
        out CloudflareSiteRouteProfile? siteProfile,
        out int errorCode)
    {
        siteProfile = null;
        errorCode = 0;

        var siteConfigPath = TryGetOptionValue(subArgs, "--site-config") ??
                             TryGetOptionValue(subArgs, "--siteConfig") ??
                             TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(siteConfigPath))
            return true;

        var resolvedSiteConfig = Path.GetFullPath(siteConfigPath);
        try
        {
            siteProfile = CloudflareRouteProfileResolver.Load(resolvedSiteConfig);
            return true;
        }
        catch (Exception ex)
        {
            errorCode = Fail(
                $"Failed to load --site-config '{resolvedSiteConfig}': {ex.Message}",
                outputJson,
                logger,
                command);
            return false;
        }
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        var b = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(b))
            return path ?? string.Empty;

        if (!b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !b.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            b = "https://" + b;

        b = b.TrimEnd('/');

        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p))
            return b + "/";
        if (!p.StartsWith("/", StringComparison.Ordinal))
            p = "/" + p;

        return b + p;
    }
}

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

        if (verb.Equals("dns-record", StringComparison.OrdinalIgnoreCase) ||
            verb.Equals("dns", StringComparison.OrdinalIgnoreCase))
        {
            if (subArgs.Length > 0 && !subArgs[0].StartsWith("-", StringComparison.Ordinal))
            {
                if (!subArgs[0].Equals("apply", StringComparison.OrdinalIgnoreCase))
                    return Fail($"Unknown cloudflare dns-record verb '{subArgs[0]}'. Supported: apply.", outputJson, logger, "web.cloudflare.dns-record");
                subArgs = subArgs.Skip(1).ToArray();
            }

            return HandleCloudflareDnsRecord(subArgs, outputJson, logger, outputSchemaVersion);
        }

        if (verb.Equals("cache-policy", StringComparison.OrdinalIgnoreCase) ||
            verb.Equals("policy", StringComparison.OrdinalIgnoreCase) ||
            verb.Equals("rules", StringComparison.OrdinalIgnoreCase))
        {
            if (subArgs.Length > 0 && !subArgs[0].StartsWith("-", StringComparison.Ordinal))
            {
                if (!subArgs[0].Equals("apply", StringComparison.OrdinalIgnoreCase))
                    return Fail($"Unknown cloudflare cache-policy verb '{subArgs[0]}'. Supported: apply.", outputJson, logger, "web.cloudflare.cache-policy");
                subArgs = subArgs.Skip(1).ToArray();
            }

            return HandleCloudflareCachePolicy(subArgs, outputJson, logger, outputSchemaVersion);
        }

        return Fail($"Unknown cloudflare verb '{verb}'. Supported: purge, verify, cache-policy, dns-record.", outputJson, logger, "web.cloudflare");
    }

    private static int HandleCloudflareDnsRecord(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        const string command = "web.cloudflare.dns-record.apply";
        if (!TryValidateCloudflareDnsRecordArguments(subArgs, out var argumentError))
            return Fail(argumentError, outputJson, logger, command);

        var zoneName = TryGetOptionValue(subArgs, "--zone-name") ??
                       TryGetOptionValue(subArgs, "--zoneName") ??
                       TryGetOptionValue(subArgs, "--zone");
        if (string.IsNullOrWhiteSpace(zoneName))
            return Fail("Missing required --zone-name.", outputJson, logger, command);

        var recordName = TryGetOptionValue(subArgs, "--record-name") ??
                         TryGetOptionValue(subArgs, "--recordName") ??
                         TryGetOptionValue(subArgs, "--name");
        if (string.IsNullOrWhiteSpace(recordName))
            return Fail("Missing required --record-name.", outputJson, logger, command);

        var recordContent = TryGetOptionValue(subArgs, "--record-content") ??
                            TryGetOptionValue(subArgs, "--recordContent") ??
                            TryGetOptionValue(subArgs, "--content");
        if (string.IsNullOrWhiteSpace(recordContent))
            return Fail("Missing required --record-content.", outputJson, logger, command);

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
            return Fail($"Missing Cloudflare API token. Provide --token or set env var '{tokenEnv}'.", outputJson, logger, command);

        var recordType = TryGetOptionValue(subArgs, "--record-type") ??
                         TryGetOptionValue(subArgs, "--recordType") ??
                         TryGetOptionValue(subArgs, "--type") ??
                         "A";
        var proxiedValue = TryGetOptionValue(subArgs, "--proxied") ?? "true";
        if (!bool.TryParse(proxiedValue, out var proxied))
            return Fail("--proxied must be true or false.", outputJson, logger, command);

        var ttlValue = TryGetOptionValue(subArgs, "--ttl") ?? "1";
        if (!int.TryParse(ttlValue, out var ttl))
            return Fail("--ttl must be an integer.", outputJson, logger, command);

        var comment = TryGetOptionValue(subArgs, "--comment");
        var dryRun = HasOption(subArgs, "--dry-run") || HasOption(subArgs, "--dryRun");
        var result = CloudflareDnsRecordManager.Apply(
            zoneName,
            token,
            recordType,
            recordName,
            recordContent,
            proxied,
            ttl,
            comment,
            dryRun,
            logger);

        if (outputJson)
        {
            var element = JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["zoneId"] = result.ZoneId,
                ["recordId"] = result.RecordId,
                ["recordType"] = result.RecordType,
                ["recordName"] = result.RecordName,
                ["recordContent"] = result.RecordContent,
                ["proxied"] = result.Proxied,
                ["ttl"] = result.Ttl,
                ["dryRun"] = result.DryRun,
                ["changesRequired"] = result.ChangesRequired,
                ["changed"] = result.Changed,
                ["action"] = result.Action,
                ["message"] = result.Message
            }, WebCliJson.Options);

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = element,
                Error = result.Success ? null : result.Message
            });
            return result.Success ? 0 : 1;
        }

        if (result.Success) logger.Success(result.Message);
        else logger.Error(result.Message);
        return result.Success ? 0 : 1;
    }

    private static bool TryValidateCloudflareDnsRecordArguments(string[] args, out string error)
    {
        var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--zone-name", "--zoneName", "--zone",
            "--record-name", "--recordName", "--name",
            "--record-content", "--recordContent", "--content",
            "--token", "--api-token", "--apiToken",
            "--token-env", "--api-token-env", "--apiTokenEnv",
            "--record-type", "--recordType", "--type",
            "--proxied", "--ttl", "--comment", "--output"
        };
        var flagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--dry-run", "--dryRun", "--output-json", "--json"
        };

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (flagOptions.Contains(argument))
                continue;
            if (!valueOptions.Contains(argument))
            {
                error = $"Unknown cloudflare dns-record argument '{argument}'.";
                return false;
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Missing value for cloudflare dns-record option '{argument}'.";
                return false;
            }
            index++;
        }

        error = string.Empty;
        return true;
    }

    private static int HandleCloudflareCachePolicy(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        const string command = "web.cloudflare.cache-policy.apply";
        if (!TryLoadCloudflareSiteProfile(subArgs, outputJson, logger, command, out var siteProfile, out var loadError))
            return loadError;

        var zoneId = TryGetOptionValue(subArgs, "--zone-id") ??
                     TryGetOptionValue(subArgs, "--zone") ??
                     TryGetOptionValue(subArgs, "--zoneId");
        if (string.IsNullOrWhiteSpace(zoneId))
            return Fail("Missing required --zone-id.", outputJson, logger, command);

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
            return Fail($"Missing Cloudflare API token. Provide --token or set env var '{tokenEnv}'.", outputJson, logger, command);

        Uri? siteBaseUri = null;
        if (!string.IsNullOrWhiteSpace(siteProfile?.BaseUrl))
        {
            if (!Uri.TryCreate(siteProfile.BaseUrl, UriKind.Absolute, out siteBaseUri) ||
                (siteBaseUri.Scheme != Uri.UriSchemeHttps && siteBaseUri.Scheme != Uri.UriSchemeHttp))
                return Fail("site config BaseUrl must be an absolute HTTP or HTTPS URL.", outputJson, logger, command);
        }

        var hostname = TryGetOptionValue(subArgs, "--hostname") ??
                       TryGetOptionValue(subArgs, "--host");
        if (string.IsNullOrWhiteSpace(hostname) && siteBaseUri is not null)
            hostname = siteBaseUri.Host;
        if (string.IsNullOrWhiteSpace(hostname))
            return Fail("Missing --hostname (or a --site-config with BaseUrl).", outputJson, logger, command);

        var basePath = TryGetOptionValue(subArgs, "--base-path") ??
                       TryGetOptionValue(subArgs, "--basePath") ??
                       siteBaseUri?.AbsolutePath ??
                       "/";
        var policyName = TryGetOptionValue(subArgs, "--policy-name") ??
                         TryGetOptionValue(subArgs, "--policyName") ??
                         siteProfile?.Name ??
                         hostname;
        var htmlPaths = ReadOptionList(subArgs, "--html-path", "--html-paths");
        if (siteProfile is not null)
            htmlPaths.AddRange(siteProfile.VerifyPaths);

        var dryRun = HasOption(subArgs, "--dry-run") || HasOption(subArgs, "--dryRun");
        var result = CloudflareCachePolicyManager.Apply(
            zoneId,
            token,
            hostname,
            policyName,
            htmlPaths,
            dryRun,
            logger,
            basePath: basePath);

        if (outputJson)
        {
            var element = JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["siteConfig"] = siteProfile?.SiteConfigPath,
                ["zoneId"] = zoneId,
                ["hostname"] = result.Hostname,
                ["basePath"] = basePath,
                ["policyName"] = result.PolicyName,
                ["managedRuleCount"] = result.ManagedRuleCount,
                ["preservedRuleCount"] = result.PreservedRuleCount,
                ["dryRun"] = result.DryRun,
                ["changesRequired"] = result.ChangesRequired,
                ["changed"] = result.Changed,
                ["message"] = result.Message
            }, WebCliJson.Options);

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = element,
                Error = result.Success ? null : result.Message
            });
            return result.Success ? 0 : 1;
        }

        if (result.Success) logger.Success(result.Message);
        else logger.Error(result.Message);
        return result.Success ? 0 : 1;
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

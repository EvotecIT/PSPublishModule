using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleCloudflare(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        // Future-friendly shape: powerforge-web cloudflare purge ...
        // If the user omits the verb, default to purge.
        var verb = "purge";
        if (subArgs.Length > 0 && !subArgs[0].StartsWith("-", StringComparison.Ordinal))
        {
            verb = subArgs[0].Trim();
            subArgs = subArgs.Skip(1).ToArray();
        }

        if (!verb.Equals("purge", StringComparison.OrdinalIgnoreCase))
            return Fail($"Unknown cloudflare verb '{verb}'. Supported: purge.", outputJson, logger, "web.cloudflare");

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
                      Environment.GetEnvironmentVariable("POWERFORGE_BASE_URL");

        var urls = ReadOptionList(subArgs, "--url", "--urls");
        var paths = ReadOptionList(subArgs, "--path", "--paths");
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
                ["zoneId"] = zoneId,
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

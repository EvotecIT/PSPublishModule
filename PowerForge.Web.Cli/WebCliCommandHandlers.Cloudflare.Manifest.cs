using System.Text.Json;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleCloudflareManifest(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var verb = "create";
        if (subArgs.Length > 0 && !subArgs[0].StartsWith("-", StringComparison.Ordinal))
        {
            verb = subArgs[0].Trim();
            subArgs = subArgs.Skip(1).ToArray();
        }

        if (!verb.Equals("create", StringComparison.OrdinalIgnoreCase))
            return Fail($"Unknown cloudflare manifest verb '{verb}'. Supported: create.", outputJson, logger, "web.cloudflare.manifest");

        const string command = "web.cloudflare.manifest.create";
        if (!TryLoadCloudflareSiteProfile(subArgs, outputJson, logger, command, out var siteProfile, out var loadError))
            return loadError;
        if (siteProfile is null)
            return Fail("cloudflare manifest create requires --site-config.", outputJson, logger, command);

        var artifactPath = TryGetOptionValue(subArgs, "--artifact") ??
                           TryGetOptionValue(subArgs, "--artifact-path") ??
                           TryGetOptionValue(subArgs, "--artifactPath");
        if (string.IsNullOrWhiteSpace(artifactPath))
            return Fail("cloudflare manifest create requires --artifact.", outputJson, logger, command);

        var outputPath = TryGetOptionValue(subArgs, "--out") ??
                         TryGetOptionValue(subArgs, "--output-path") ??
                         TryGetOptionValue(subArgs, "--outputPath") ??
                         TryGetOptionValue(subArgs, "--output");
        if (string.IsNullOrWhiteSpace(outputPath))
            return Fail("cloudflare manifest create requires --out.", outputJson, logger, command);

        CloudflareDeploymentManifestCreateResult result;
        try
        {
            result = CloudflareDeploymentManifestStore.CreateFromTar(
                artifactPath,
                siteProfile.BaseUrl,
                outputPath,
                siteProfile.VerifyPaths,
                siteProfile.Cloudflare);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return Fail($"Failed to create Cloudflare deployment manifest: {ex.Message}", outputJson, logger, command);
        }

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = true,
                ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(result, WebCliJson.Context.CloudflareDeploymentManifestCreateResult)
            });
        }
        else
        {
            logger.Success($"Created Cloudflare deployment manifest with {result.ArtifactFileCount} file(s), {result.UrlPathCount} URL path(s), and {result.ContentBytes} content byte(s) in {result.ElapsedMilliseconds} ms: {result.ManifestPath}");
        }

        return 0;
    }

    private static int HandleCloudflareIncrementalPurge(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion,
        CloudflareSiteRouteProfile? siteProfile,
        string zoneId,
        string token,
        string? baseUrl,
        bool dryRun)
    {
        const string command = "web.cloudflare.purge";
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("Incremental purge requires --site-config or --base-url.", outputJson, logger, command);

        var currentManifestPath = TryGetOptionValue(subArgs, "--current-manifest") ??
                                  TryGetOptionValue(subArgs, "--current-manifest-path") ??
                                  TryGetOptionValue(subArgs, "--currentManifest") ??
                                  TryGetOptionValue(subArgs, "--currentManifestPath");
        if (string.IsNullOrWhiteSpace(currentManifestPath))
            return Fail("Incremental purge requires --current-manifest.", outputJson, logger, command);

        var previousManifestPath = TryGetOptionValue(subArgs, "--previous-manifest") ??
                                   TryGetOptionValue(subArgs, "--previous-manifest-path") ??
                                   TryGetOptionValue(subArgs, "--previousManifest") ??
                                   TryGetOptionValue(subArgs, "--previousManifestPath");
        var forceHostnameFallbackReason = TryGetOptionValue(subArgs, "--force-hostname-fallback-reason") ??
                                          TryGetOptionValue(subArgs, "--forceHostnameFallbackReason");
        var forceHostnameFallback = HasOption(subArgs, "--force-hostname-fallback") ||
                                    HasOption(subArgs, "--forceHostnameFallback") ||
                                    !string.IsNullOrWhiteSpace(forceHostnameFallbackReason);

        var result = CloudflareIncrementalCachePurger.Purge(
            zoneId,
            token,
            baseUrl,
            currentManifestPath,
            previousManifestPath,
            dryRun,
            logger,
            forcedHostnameFallbackReason: forceHostnameFallback
                ? forceHostnameFallbackReason?.Trim() ?? "the managed site policy was reconciled"
                : null,
            alwaysPurgePaths: siteProfile?.Cloudflare?.AlwaysPurgePaths);

        if (outputJson)
        {
            var element = BuildCloudflarePurgeResult(
                siteProfile?.SiteConfigPath,
                zoneId,
                baseUrl,
                CloudflareCachePurgeMode.Incremental,
                result.ActualMode == CloudflareCachePurgeMode.Files ? result.TargetCount : 0,
                result.TargetCount,
                dryRun,
                result.Message,
                result.ActualMode,
                result.RequestCount,
                result.UsedFallback,
                result.FallbackReason);
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = command,
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = element,
                Error = result.Success ? null : result.Message
            });
        }
        else if (result.Success)
        {
            logger.Success(result.Message);
        }
        else
        {
            logger.Error(result.Message);
        }

        return result.Success ? 0 : 1;
    }
}

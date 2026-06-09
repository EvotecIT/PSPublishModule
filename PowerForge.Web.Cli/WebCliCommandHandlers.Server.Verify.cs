using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerVerify(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.verify");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        var manifest = loaded.Manifest;
        var manifestPath = loaded.ManifestPath!;
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var failOnFailure = HasOption(subArgs, "--fail-on-failure");
        var urlTimeoutSeconds = ParseIntOption(TryGetOptionValue(subArgs, "--url-timeout-seconds"), 30);
        var target = BuildServerSshTarget(manifest.Target);
        var commandResults = new List<PowerForgeServerVerifyCommandResult>();
        var urlResults = new List<PowerForgeServerVerifyUrlResult>();
        var warnings = new List<string>();

        foreach (var command in manifest.Verify?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
        {
            if (command.Sensitive)
            {
                warnings.Add($"Skipping sensitive verify command '{command.Id}'.");
                continue;
            }

            var result = ExecuteRemote(sshCommand, target, command.Command ?? string.Empty);
            commandResults.Add(new PowerForgeServerVerifyCommandResult
            {
                Id = command.Id,
                Command = command.Command,
                Required = command.Required,
                ExitCode = result.ExitCode,
                Success = result.Success,
                OutputPreview = Preview(result.Stdout),
                ErrorPreview = Preview(result.Stderr)
            });
        }

        foreach (var url in manifest.Verify?.Urls ?? Array.Empty<PowerForgeServerVerifyUrl>())
            urlResults.Add(VerifyUrl(url, urlTimeoutSeconds));

        var failedCommands = commandResults
            .Where(static result => result.Required && !result.Success)
            .ToArray();
        var failedUrls = urlResults
            .Where(static result => !result.Success)
            .ToArray();

        if (failedCommands.Length > 0)
            warnings.Add($"{failedCommands.Length} required verify command(s) failed.");
        if (failedUrls.Length > 0)
            warnings.Add($"{failedUrls.Length} URL check(s) failed.");

        var success = failedCommands.Length == 0 && failedUrls.Length == 0;
        var resultSummary = new PowerForgeServerVerifyResult
        {
            ManifestPath = manifestPath,
            Target = target,
            Success = success,
            Commands = commandResults.ToArray(),
            Urls = urlResults.ToArray(),
            Warnings = warnings.ToArray()
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.server.verify",
                Success = success || !failOnFailure,
                ExitCode = success || !failOnFailure ? 0 : 1,
                Config = "web.serverrecovery",
                ConfigPath = manifestPath,
                Result = WebCliJson.SerializeToElement(resultSummary, WebCliJson.Context.PowerForgeServerVerifyResult),
                Error = warnings.Count == 0 ? null : string.Join(" | ", warnings)
            });
            return success || !failOnFailure ? 0 : 1;
        }

        logger.Success(success ? "Server verify completed successfully." : "Server verify completed with failures.");
        logger.Info($"Target: {target}");
        logger.Info($"Commands: {commandResults.Count}; URLs: {urlResults.Count}");
        foreach (var failure in failedCommands)
            logger.Warn($"command {failure.Id}: exit={failure.ExitCode}; error={failure.ErrorPreview}");
        foreach (var failure in failedUrls)
            logger.Warn($"url {failure.Url}: expected={failure.ExpectedStatus}; actual={failure.ActualStatus}; error={failure.Error}");

        return success || !failOnFailure ? 0 : 1;
    }

    private static PowerForgeServerVerifyUrlResult VerifyUrl(PowerForgeServerVerifyUrl verifyUrl, int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(verifyUrl.Url))
        {
            return new PowerForgeServerVerifyUrlResult
            {
                Url = verifyUrl.Url,
                ExpectedStatus = verifyUrl.ExpectedStatus,
                Via = verifyUrl.Via,
                Success = false,
                Error = "URL is missing."
            };
        }

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, verifyUrl.Url);
            request.Headers.UserAgent.ParseAdd("PowerForge-Web-ServerRecovery/1.0");
            using var response = httpClient.Send(request);

            var status = (int)response.StatusCode;
            var expected = verifyUrl.ExpectedStatus ?? 200;
            var serverHeader = response.Headers.Server.ToString();
            var cloudflareRay = response.Headers.TryGetValues("CF-Ray", out var cloudflareRayValues)
                ? cloudflareRayValues.FirstOrDefault()
                : null;
            var success = status == expected;
            if (verifyUrl.Via?.Equals("cloudflare", StringComparison.OrdinalIgnoreCase) == true)
                success = success && (!string.IsNullOrWhiteSpace(cloudflareRay) ||
                                      serverHeader.Contains("cloudflare", StringComparison.OrdinalIgnoreCase));

            return new PowerForgeServerVerifyUrlResult
            {
                Url = verifyUrl.Url,
                ExpectedStatus = expected,
                ActualStatus = status,
                Via = verifyUrl.Via,
                Success = success,
                ServerHeader = serverHeader,
                CloudflareRay = cloudflareRay,
                Error = success ? null : "Unexpected status or missing expected proxy header."
            };
        }
        catch (Exception ex)
        {
            return new PowerForgeServerVerifyUrlResult
            {
                Url = verifyUrl.Url,
                ExpectedStatus = verifyUrl.ExpectedStatus,
                Via = verifyUrl.Via,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static string? Preview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}

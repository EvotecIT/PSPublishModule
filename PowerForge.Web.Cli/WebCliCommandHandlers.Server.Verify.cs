using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleServerVerify(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var loaded = LoadServerRecoveryManifest(subArgs, outputJson, logger, "web.server.verify");
        if (loaded.Manifest is null)
            return loaded.ExitCode;

        return RunServerVerification(loaded.Manifest, loaded.ManifestPath!, subArgs, outputJson, logger, outputSchemaVersion);
    }

    private static int RunServerVerification(
        PowerForgeServerRecoveryManifest manifest, string manifestPath, string[] subArgs,
        bool outputJson, WebConsoleLogger logger, int outputSchemaVersion, string commandName = "web.server.verify",
        bool reconcileDisabledUnits = false, RemoteOperationLock? operationLock = null)
    {
        var local = HasOption(subArgs, "--local");
        if (local && HasOption(subArgs, "--ssh"))
            throw new InvalidOperationException("Server verify cannot combine --local with --ssh.");
        var sshCommand = TryGetOptionValue(subArgs, "--ssh") ?? "ssh";
        var failOnFailure = HasOption(subArgs, "--fail-on-failure");
        var urlTimeoutSeconds = ParseIntOption(TryGetOptionValue(subArgs, "--url-timeout-seconds"), 30);
        var target = local ? "local" : BuildServerSshTarget(manifest.Target);
        var commandResults = new List<PowerForgeServerVerifyCommandResult>();
        var urlResults = new List<PowerForgeServerVerifyUrlResult>();
        var warnings = new List<string>();

        try
        {
            foreach (var command in manifest.Verify?.Commands ?? Array.Empty<PowerForgeServerNamedCommand>())
            {
                if (command.Sensitive)
                {
                    warnings.Add($"Skipping sensitive verify command '{command.Id}'.");
                    continue;
                }

                var result = local
                    ? RunLocalServerScript(BuildScopedServerCommand(command))
                    : ExecuteRemote(sshCommand, target, command.Command ?? string.Empty);
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
        }
        finally
        {
            // Verification can activate a unit even when it throws. Keep the caller's
            // operation lock until every enforced-disabled unit has been rechecked.
            if (reconcileDisabledUnits)
            {
                var units = (manifest.Systemd?.Timers ?? Array.Empty<PowerForgeServerSystemdUnit>())
                    .Concat(manifest.Systemd?.Services ?? Array.Empty<PowerForgeServerSystemdUnit>());
                foreach (var unit in units.Where(static unit => unit.EnforceDisabled && !string.IsNullOrWhiteSpace(unit.Name)))
                {
                    try
                    {
                        var result = RunLocalServerScript(BuildStopAndDisableUnitCommand(unit.Name!));
                        commandResults.Add(new PowerForgeServerVerifyCommandResult
                        {
                            Id = $"enforceDisabled:{unit.Name}",
                            Required = true,
                            ExitCode = result.ExitCode,
                            Success = result.Success,
                            ErrorPreview = result.Success ? null : "Could not confirm the unit is stopped and disabled."
                        });
                    }
                    catch (Exception exception)
                    {
                        commandResults.Add(new PowerForgeServerVerifyCommandResult
                        {
                            Id = $"enforceDisabled:{unit.Name}",
                            Required = true,
                            ExitCode = 1,
                            Success = false,
                            ErrorPreview = Preview(exception.Message)
                        });
                    }
                }
            }
        }

        // Do not emit a successful verification result after the shared operation
        // lock has been lost during a command, URL check, or final reconciliation.
        operationLock?.EnsureHeld("after verification and disabled-unit reconciliation");

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
                Command = commandName,
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

    internal static string BuildScopedServerCommand(PowerForgeServerNamedCommand command)
        => string.IsNullOrWhiteSpace(command.WorkingDirectory)
            ? command.Command ?? string.Empty
            : $"( cd -- {ShellQuote(command.WorkingDirectory)} && {{\n{command.Command}\n}} )";

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

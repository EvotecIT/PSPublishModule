using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static int HandleProvider(
        string[] subArgs,
        bool outputJson,
        WebConsoleLogger logger,
        int outputSchemaVersion)
    {
        if (subArgs.Length == 0 || !subArgs[0].Equals("doctor", StringComparison.OrdinalIgnoreCase))
            return FailSearch("Provider requires the 'doctor' action.", outputJson, logger, "web.provider.doctor");

        var args = subArgs.Skip(1).ToArray();
        var missingValueOption = FindSearchOptionWithoutValue(args, "--config", "--output");
        if (missingValueOption is not null)
            return FailSearch($"{missingValueOption} requires a value.", outputJson, logger, "web.provider.doctor");
        var unexpectedArgument = FindUnexpectedProviderArgument(args, "--config", "--output");
        if (unexpectedArgument is not null)
            return FailSearch($"Provider doctor does not recognize argument '{unexpectedArgument}'.", outputJson, logger, "web.provider.doctor");

        var configPath = TryGetOptionValue(args, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return FailSearch("Provider doctor requires --config.", outputJson, logger, "web.provider.doctor");

        try
        {
            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options);
            var result = WebSearchProviderDoctor.Inspect(loaded.Configuration);
            var exitCode = result.Success ? 0 : 1;

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = outputSchemaVersion,
                    Command = "web.provider.doctor",
                    Success = result.Success,
                    ExitCode = exitCode,
                    ConfigPath = loaded.FullPath,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebSearchProviderDoctorResult)
                });
            }
            else
            {
                if (result.Success)
                    logger.Success("Search provider configuration passed capability checks.");
                else
                    logger.Error("Search provider configuration has blocking capability errors.");

                logger.Info(
                    $"Sites: {result.SiteCount}; providers: {result.ProviderCount}; configuration ready: {result.ConfigurationReadyCount}; collectors available: {result.CollectorAvailableCount}.");
                foreach (var check in result.Checks)
                {
                    var scope = BuildProviderCheckScope(check);
                    var message = $"[{check.Code}] {scope}{check.Message}";
                    switch (check.Severity)
                    {
                        case WebSearchProviderCheckSeverity.Error:
                            logger.Error(EscapeSearchConsoleText(message, "Provider check failed."));
                            break;
                        case WebSearchProviderCheckSeverity.Warning:
                            logger.Warn(EscapeSearchConsoleText(message, "Provider check warning."));
                            break;
                        default:
                            logger.Info(EscapeSearchConsoleText(message, "Provider check information."));
                            break;
                    }
                }
            }

            return exitCode;
        }
        catch (Exception ex)
        {
            return FailSearch(ex.Message, outputJson, logger, "web.provider.doctor");
        }
    }

    private static string BuildProviderCheckScope(WebSearchProviderCheck check)
    {
        if (!string.IsNullOrWhiteSpace(check.SiteId) && !string.IsNullOrWhiteSpace(check.ProviderId))
            return $"{check.SiteId}/{check.ProviderId}: ";
        if (!string.IsNullOrWhiteSpace(check.SiteId))
            return $"{check.SiteId}: ";
        return string.Empty;
    }

    private static string? FindUnexpectedProviderArgument(string[] args, params string[] optionNames)
    {
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!optionNames.Contains(args[index], StringComparer.OrdinalIgnoreCase))
                return args[index];
        }

        return null;
    }
}

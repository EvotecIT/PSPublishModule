using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private const string UnrelatedProviderCredentialPlaceholder = "credential-not-required-by-selected-action";

    internal static WebSearchProviderDoctorResult InspectProviderAction(
        WebSearchProviderConfiguration configuration,
        WebSearchSiteProviderConfiguration site,
        WebSearchProviderRegistration provider,
        string requiredCapability,
        bool useSelectedCredential)
    {
        var selectedCredentialVariable = useSelectedCredential
            ? provider.Credential?.EnvironmentVariable
            : null;
        var doctor = WebSearchProviderDoctor.InspectWithCapabilities(
            configuration,
            WebSearchCollectorCatalog.AvailableCapabilities,
            name => !string.IsNullOrWhiteSpace(selectedCredentialVariable) &&
                    string.Equals(name, selectedCredentialVariable, StringComparison.Ordinal)
                ? Environment.GetEnvironmentVariable(name)
                : UnrelatedProviderCredentialPlaceholder);
        if (!doctor.Success || string.IsNullOrWhiteSpace(doctor.ConfigurationHash))
        {
            var firstError = doctor.Checks.FirstOrDefault(check => check.Severity == WebSearchProviderCheckSeverity.Error);
            throw new ArgumentException(firstError?.Message ?? "Provider configuration has blocking capability errors.");
        }

        var providerState = doctor.Providers.SingleOrDefault(value =>
            value.SiteId.Equals(site.Id, StringComparison.OrdinalIgnoreCase) &&
            value.ProviderId.Equals(provider.Id, StringComparison.OrdinalIgnoreCase));
        if (providerState is null ||
            !providerState.AvailableCollectorCapabilities.Contains(requiredCapability, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Provider collector must implement capability '{requiredCapability}'.");
        }
        return doctor;
    }

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
        var duplicateOption = FindDuplicateProviderOption(args, "--config", "--output");
        if (duplicateOption is not null)
            return FailSearch($"Provider doctor accepts {duplicateOption} only once.", outputJson, logger, "web.provider.doctor");
        var unexpectedArgument = FindUnexpectedProviderArgument(args);
        if (unexpectedArgument is not null)
            return FailSearch($"Provider doctor does not recognize argument '{unexpectedArgument}'.", outputJson, logger, "web.provider.doctor");

        var configPath = TryGetOptionValue(args, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return FailSearch("Provider doctor requires --config.", outputJson, logger, "web.provider.doctor");
        var outputFormat = TryGetOptionValue(args, "--output");
        if (!string.IsNullOrWhiteSpace(outputFormat) && !outputFormat.Equals("json", StringComparison.OrdinalIgnoreCase))
            return FailSearch("Provider doctor supports only '--output json'.", outputJson, logger, "web.provider.doctor");

        try
        {
            var loaded = WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options);
            var result = WebSearchProviderDoctor.InspectWithCapabilities(
                loaded.Configuration,
                WebSearchCollectorCatalog.AvailableCapabilities);
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

    private static string? FindUnexpectedProviderArgument(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--json", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output-json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (argument.Equals("--config", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }

            return argument;
        }

        return null;
    }

    private static string? FindDuplicateProviderOption(string[] args, params string[] optionNames)
    {
        foreach (var optionName in optionNames)
        {
            if (args.Count(argument => argument.Equals(optionName, StringComparison.OrdinalIgnoreCase)) > 1)
                return optionName;
        }

        return null;
    }
}

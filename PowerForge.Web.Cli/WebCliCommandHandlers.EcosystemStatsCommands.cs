using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleEcosystemStats(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.ecosystem-stats");

        var githubOrganization = TryGetOptionValue(subArgs, "--github-org") ??
                                 TryGetOptionValue(subArgs, "--github-organization");
        var githubToken = TryGetOptionValue(subArgs, "--github-token") ??
                          TryGetOptionValue(subArgs, "--token");
        var githubTokenEnv = TryGetOptionValue(subArgs, "--github-token-env") ??
                             TryGetOptionValue(subArgs, "--token-env");
        if (string.IsNullOrWhiteSpace(githubToken) && !string.IsNullOrWhiteSpace(githubTokenEnv))
            githubToken = Environment.GetEnvironmentVariable(githubTokenEnv);

        var nugetOwner = TryGetOptionValue(subArgs, "--nuget-owner");
        var powerShellGalleryOwner = TryGetOptionValue(subArgs, "--psgallery-owner") ??
                                     TryGetOptionValue(subArgs, "--powershell-gallery-owner");
        var powerShellGalleryAuthor = TryGetOptionValue(subArgs, "--psgallery-author") ??
                                      TryGetOptionValue(subArgs, "--powershell-gallery-author");
        var title = TryGetOptionValue(subArgs, "--title");
        var baseDirectory = TryGetOptionValue(subArgs, "--base-dir") ??
                            TryGetOptionValue(subArgs, "--base-directory");
        var maxItems = ParseIntOption(TryGetOptionValue(subArgs, "--max-items"), 500);
        var timeoutSeconds = ParseIntOption(TryGetOptionValue(subArgs, "--timeout-seconds"), 30);

        if (string.IsNullOrWhiteSpace(githubOrganization) &&
            string.IsNullOrWhiteSpace(nugetOwner) &&
            string.IsNullOrWhiteSpace(powerShellGalleryOwner) &&
            string.IsNullOrWhiteSpace(powerShellGalleryAuthor))
        {
            return Fail(
                "Specify at least one source: --github-org, --nuget-owner, --psgallery-owner, or --psgallery-author.",
                outputJson,
                logger,
                "web.ecosystem-stats");
        }

        var options = new WebEcosystemStatsOptions
        {
            OutputPath = outPath,
            BaseDirectory = baseDirectory,
            Title = title,
            GitHubOrganization = githubOrganization,
            GitHubToken = githubToken,
            NuGetOwner = nugetOwner,
            PowerShellGalleryOwner = powerShellGalleryOwner,
            PowerShellGalleryAuthor = powerShellGalleryAuthor,
            MaxItems = maxItems > 0 ? maxItems : 500,
            RequestTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30
        };

        var result = WebEcosystemStatsGenerator.Generate(options);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.ecosystem-stats",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebEcosystemStatsResult)
            });
            return 0;
        }

        logger.Success($"Ecosystem stats generated: {result.OutputPath}");
        logger.Info($"GitHub repositories: {result.RepositoryCount}");
        logger.Info($"NuGet packages: {result.NuGetPackageCount}");
        logger.Info($"PowerShell Gallery modules: {result.PowerShellGalleryModuleCount}");
        if (result.Warnings.Length > 0)
        {
            foreach (var warning in result.Warnings)
                logger.Warn(warning);
        }
        return 0;
    }
}

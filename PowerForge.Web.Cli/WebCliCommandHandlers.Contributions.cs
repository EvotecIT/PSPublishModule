using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleContributions(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var action = subArgs.Length > 0 && !subArgs[0].StartsWith("--", StringComparison.Ordinal)
            ? subArgs[0].Trim().ToLowerInvariant()
            : "validate";
        var effectiveArgs = action == "validate" || action == "import" ? subArgs.Skip(1).ToArray() : subArgs;
        if (action is not ("validate" or "import"))
            return Fail("Unknown contributions action. Use 'validate' or 'import'.", outputJson, logger, "web.contributions");

        var sourceRoot = TryGetOptionValue(effectiveArgs, "--root") ??
                         TryGetOptionValue(effectiveArgs, "--source-root") ??
                         TryGetOptionValue(effectiveArgs, "--source") ??
                         ".";
        var siteRoot = TryGetOptionValue(effectiveArgs, "--site-root") ??
                       TryGetOptionValue(effectiveArgs, "--website-root") ??
                       TryGetOptionValue(effectiveArgs, "--site");
        var force = HasOption(effectiveArgs, "--force");
        var publish = HasOption(effectiveArgs, "--publish");

        WebContributionResult result;
        try
        {
            result = WebContributionProcessor.Process(new WebContributionOptions
            {
                SourceRoot = sourceRoot,
                SiteRoot = siteRoot,
                Import = action == "import",
                Force = force,
                Publish = publish
            });
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, outputJson, logger, "web.contributions");
        }

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.contributions",
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebContributionResult),
                Error = result.Success ? null : string.Join(" | ", result.Errors)
            });
            return result.Success ? 0 : 1;
        }

        foreach (var warning in result.Warnings)
            logger.Warn(warning);
        foreach (var error in result.Errors)
            logger.Error(error);

        logger.Info($"Contribution source: {result.SourceRoot}");
        logger.Info($"Authors: {result.AuthorCount}");
        logger.Info($"Posts: {result.PostCount}");
        if (action == "import")
        {
            logger.Info($"Imported posts: {result.ImportedPostCount}");
            logger.Info($"Copied assets: {result.CopiedAssetCount}");
            logger.Info($"Copied authors: {result.CopiedAuthorCount}");
        }

        if (!result.Success)
            return 1;

        logger.Success(action == "import" ? "Contribution import passed." : "Contribution validation passed.");
        return 0;
    }
}

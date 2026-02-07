using System;
using System.Linq;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleDotNetBuild(string[] subArgs, bool outputJson, int outputSchemaVersion, WebConsoleLogger logger)
    {
        var project = TryGetOptionValue(subArgs, "--project") ??
                      TryGetOptionValue(subArgs, "--solution") ??
                      TryGetOptionValue(subArgs, "--path");
        var configuration = TryGetOptionValue(subArgs, "--configuration");
        var framework = TryGetOptionValue(subArgs, "--framework");
        var runtime = TryGetOptionValue(subArgs, "--runtime");
        var noRestore = subArgs.Any(a => a.Equals("--no-restore", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(project))
            return Fail("Missing required --project.", outputJson, logger, "web.dotnet-build");

        var result = WebDotNetRunner.Build(new WebDotNetBuildOptions
        {
            ProjectOrSolution = project,
            Configuration = configuration,
            Framework = framework,
            Runtime = runtime,
            Restore = !noRestore
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.dotnet-build",
                Success = result.Success,
                ExitCode = result.ExitCode,
                Result = WebCliJson.SerializeToElement(new WebDotNetBuildResult
                {
                    Success = result.Success,
                    ExitCode = result.ExitCode,
                    Output = result.Output,
                    Error = result.Error
                }, WebCliJson.Context.WebDotNetBuildResult)
            });
            return result.Success ? 0 : 1;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
            Console.WriteLine(result.Output);
        if (!string.IsNullOrWhiteSpace(result.Error))
            Console.Error.WriteLine(result.Error);

        return result.Success ? 0 : 1;
    }

    private static int HandleDotNetPublish(string[] subArgs, bool outputJson, int outputSchemaVersion, WebConsoleLogger logger)
    {
        var project = TryGetOptionValue(subArgs, "--project");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var cleanOutput = HasOption(subArgs, "--clean") || HasOption(subArgs, "--clean-out");
        var configuration = TryGetOptionValue(subArgs, "--configuration");
        var framework = TryGetOptionValue(subArgs, "--framework");
        var runtime = TryGetOptionValue(subArgs, "--runtime");
        var selfContained = subArgs.Any(a => a.Equals("--self-contained", StringComparison.OrdinalIgnoreCase));
        var noBuild = subArgs.Any(a => a.Equals("--no-build", StringComparison.OrdinalIgnoreCase));
        var noRestore = subArgs.Any(a => a.Equals("--no-restore", StringComparison.OrdinalIgnoreCase));
        var baseHref = TryGetOptionValue(subArgs, "--base-href");
        var defineConstants = TryGetOptionValue(subArgs, "--define-constants") ??
                              TryGetOptionValue(subArgs, "--defineConstants");
        var blazorFixes = !subArgs.Any(a => a.Equals("--no-blazor-fixes", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(project))
            return Fail("Missing required --project.", outputJson, logger, "web.dotnet-publish");
        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.dotnet-publish");
        if (cleanOutput)
            WebCliFileSystem.CleanOutputDirectory(outPath);

        var result = WebDotNetRunner.Publish(new WebDotNetPublishOptions
        {
            ProjectPath = project,
            OutputPath = outPath,
            Configuration = configuration,
            Framework = framework,
            Runtime = runtime,
            SelfContained = selfContained,
            NoBuild = noBuild,
            NoRestore = noRestore,
            DefineConstants = defineConstants
        });

        if (result.Success && blazorFixes)
        {
            WebBlazorPublishFixer.Apply(new WebBlazorPublishFixOptions
            {
                PublishRoot = outPath,
                BaseHref = baseHref
            });
        }

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.dotnet-publish",
                Success = result.Success,
                ExitCode = result.ExitCode,
                Result = WebCliJson.SerializeToElement(new WebDotNetPublishResult
                {
                    Success = result.Success,
                    ExitCode = result.ExitCode,
                    Output = result.Output,
                    Error = result.Error,
                    OutputPath = outPath
                }, WebCliJson.Context.WebDotNetPublishResult)
            });
            return result.Success ? 0 : 1;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
            Console.WriteLine(result.Output);
        if (!string.IsNullOrWhiteSpace(result.Error))
            Console.Error.WriteLine(result.Error);

        return result.Success ? 0 : 1;
    }

    private static int HandleOverlay(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var source = TryGetOptionValue(subArgs, "--source");
        var destination = TryGetOptionValue(subArgs, "--destination") ??
                          TryGetOptionValue(subArgs, "--dest");
        var include = TryGetOptionValue(subArgs, "--include");
        var exclude = TryGetOptionValue(subArgs, "--exclude");
        var clean = subArgs.Any(a => a.Equals("--clean", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(source))
            return Fail("Missing required --source.", outputJson, logger, "web.overlay");
        if (string.IsNullOrWhiteSpace(destination))
            return Fail("Missing required --destination.", outputJson, logger, "web.overlay");

        var result = WebStaticOverlay.Apply(new WebStaticOverlayOptions
        {
            SourceRoot = source,
            DestinationRoot = destination,
            Clean = clean,
            Include = CliPatternHelper.SplitPatterns(include),
            Exclude = CliPatternHelper.SplitPatterns(exclude)
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.overlay",
                Success = true,
                ExitCode = 0,
                Result = WebCliJson.SerializeToElement(new WebStaticOverlayResult
                {
                    CopiedCount = result.CopiedCount
                }, WebCliJson.Context.WebStaticOverlayResult)
            });
            return 0;
        }

        logger.Success($"Overlay copied: {result.CopiedCount} files");
        return 0;
    }
}

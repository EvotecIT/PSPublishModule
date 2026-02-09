using System;
using System.IO;
using System.Linq;
using PowerForge;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandlePlan(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.plan");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.plan",
                Success = true,
                ExitCode = 0,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan)
            });
            return 0;
        }

        logger.Success($"Web plan: {plan.Name}");
        logger.Info($"Config: {plan.ConfigPath}");
        logger.Info($"Root: {plan.RootPath}");
        if (plan.Collections.Length > 0)
        {
            foreach (var collection in plan.Collections)
                logger.Info($"Collection: {collection.Name} ({collection.FileCount} files) -> {collection.OutputPath}");
        }
        if (plan.Projects.Length > 0)
        {
            foreach (var project in plan.Projects)
                logger.Info($"Project: {project.Name} ({project.Slug}) ({project.ContentFileCount} files)");
        }
        if (plan.RouteOverrideCount > 0) logger.Info($"Route overrides: {plan.RouteOverrideCount}");
        if (plan.RedirectCount > 0) logger.Info($"Redirects: {plan.RedirectCount}");
        return 0;
    }

    private static int HandleBuild(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var cleanOutput = HasOption(subArgs, "--clean") || HasOption(subArgs, "--clean-out");

        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.build");
        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.build");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        if (cleanOutput)
            WebCliFileSystem.CleanOutputDirectory(outPath);
        var result = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.build",
                Success = true,
                ExitCode = 0,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebBuildResult)
            });
            return 0;
        }

        logger.Success($"Web build output: {result.OutputPath}");
        logger.Info($"Plan: {result.PlanPath}");
        logger.Info($"Spec: {result.SpecPath}");
        logger.Info($"Redirects: {result.RedirectsPath}");
        return 0;
    }

    private static int HandleVerify(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.verify");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var isCi = ConsoleEnvironment.IsCI;
        var suppressWarnings = spec.Verify?.SuppressWarnings;
        var warningPreviewText = TryGetOptionValue(subArgs, "--warning-preview") ?? TryGetOptionValue(subArgs, "--warning-preview-count");
        var errorPreviewText = TryGetOptionValue(subArgs, "--error-preview") ?? TryGetOptionValue(subArgs, "--error-preview-count");
        var warningPreviewCount = ParseIntOption(warningPreviewText, 0);
        var errorPreviewCount = ParseIntOption(errorPreviewText, 0);
        var warningSummary = HasOption(subArgs, "--warning-summary") || HasOption(subArgs, "--warning-buckets");
        var warningSummaryTopText = TryGetOptionValue(subArgs, "--warning-summary-top") ?? TryGetOptionValue(subArgs, "--warning-buckets-top");
        var warningSummaryTop = ParseIntOption(warningSummaryTopText, 10);
        var baselineGenerate = HasOption(subArgs, "--baseline-generate");
        var baselineUpdate = HasOption(subArgs, "--baseline-update");
        var baselinePathValue = TryGetOptionValue(subArgs, "--baseline");
        var failOnNewWarnings = HasOption(subArgs, "--fail-on-new") || HasOption(subArgs, "--fail-on-new-warnings");
        var failOnWarnings = HasOption(subArgs, "--fail-on-warnings") || (spec.Verify?.FailOnWarnings ?? false);
        var failOnNavLint = HasOption(subArgs, "--fail-on-nav-lint") || (spec.Verify?.FailOnNavLint ?? isCi);
        var failOnThemeContract = HasOption(subArgs, "--fail-on-theme-contract") || (spec.Verify?.FailOnThemeContract ?? isCi);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
        var verify = WebSiteVerifier.Verify(spec, plan);
        var filteredWarnings = WebVerifyPolicy.FilterWarnings(verify.Warnings, suppressWarnings);
        var (verifySuccess, verifyPolicyFailures) = WebVerifyPolicy.EvaluateOutcome(
            verify,
            failOnWarnings,
            failOnNavLint,
            failOnThemeContract,
            suppressWarnings);

        if ((baselineGenerate || baselineUpdate || failOnNewWarnings) && string.IsNullOrWhiteSpace(baselinePathValue))
            baselinePathValue = ".powerforge/verify-baseline.json";

        var baselineKeys = (baselineGenerate || baselineUpdate || failOnNewWarnings || !string.IsNullOrWhiteSpace(baselinePathValue))
            ? WebVerifyBaselineStore.LoadWarningKeysSafe(plan.RootPath, baselinePathValue)
            : Array.Empty<string>();
        var baselineSet = baselineKeys.Length > 0
            ? new HashSet<string>(baselineKeys, StringComparer.OrdinalIgnoreCase)
            : null;
        var newWarnings = baselineSet is null
            ? Array.Empty<string>()
            : filteredWarnings.Where(w =>
                !string.IsNullOrWhiteSpace(w) &&
                !baselineSet.Contains(WebVerifyBaselineStore.NormalizeWarningKey(w))).ToArray();

        if (!string.IsNullOrWhiteSpace(baselinePathValue))
            verify.BaselinePath = baselinePathValue;
        verify.BaselineWarningCount = baselineKeys.Length;
        verify.NewWarnings = newWarnings;
        verify.NewWarningCount = newWarnings.Length;

        if (failOnNewWarnings)
        {
            if (baselineKeys.Length == 0)
            {
                verifySuccess = false;
                verifyPolicyFailures = verifyPolicyFailures
                    .Concat(new[] { "fail-on-new-warnings enabled but verify baseline could not be loaded (missing/empty/bad path). Generate one with --baseline-generate." })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else if (newWarnings.Length > 0)
            {
                verifySuccess = false;
                verifyPolicyFailures = verifyPolicyFailures
                    .Concat(new[] { $"fail-on-new-warnings enabled and verify produced {newWarnings.Length} new warning(s)." })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        string? writtenBaselinePath = null;
        if (baselineGenerate || baselineUpdate)
        {
            writtenBaselinePath = WebVerifyBaselineStore.Write(plan.RootPath, baselinePathValue, filteredWarnings, baselineUpdate, logger);
            verify.BaselinePath = writtenBaselinePath;
        }

        if (outputJson)
        {
            if (filteredWarnings.Length != verify.Warnings.Length)
            {
                verify = new WebVerifyResult
                {
                    Success = verify.Success,
                    Errors = verify.Errors,
                    Warnings = filteredWarnings,
                    BaselinePath = verify.BaselinePath,
                    BaselineWarningCount = verify.BaselineWarningCount,
                    NewWarningCount = verify.NewWarningCount,
                    NewWarnings = verify.NewWarnings
                };
            }

            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.verify",
                Success = verifySuccess,
                ExitCode = verifySuccess ? 0 : 1,
                Config = "web",
                ConfigPath = specPath,
                Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                Result = WebCliJson.SerializeToElement(verify, WebCliJson.Context.WebVerifyResult)
            });
            return verifySuccess ? 0 : 1;
        }

        if (filteredWarnings.Length > 0)
        {
            var max = warningPreviewCount <= 0 ? filteredWarnings.Length : Math.Max(0, warningPreviewCount);
            foreach (var warning in filteredWarnings.Take(max))
                logger.Warn(warning);
            var remaining = filteredWarnings.Length - max;
            if (remaining > 0)
                logger.Info($"Verify warnings: showing {max}/{filteredWarnings.Length} (use --warning-preview 0 to show all).");
        }
        if (verify.Errors.Length > 0)
        {
            var max = errorPreviewCount <= 0 ? verify.Errors.Length : Math.Max(0, errorPreviewCount);
            foreach (var error in verify.Errors.Take(max))
                logger.Error(error);
            var remaining = verify.Errors.Length - max;
            if (remaining > 0)
                logger.Info($"Verify errors: showing {max}/{verify.Errors.Length} (use --error-preview 0 to show all).");
        }
        if (verifyPolicyFailures.Length > 0)
        {
            foreach (var policy in verifyPolicyFailures)
                logger.Error($"verify-policy: {policy}");
        }

        if (verifySuccess)
            logger.Success("Web verify passed.");

        if (!string.IsNullOrWhiteSpace(verify.BaselinePath))
        {
            logger.Info($"Verify baseline: {verify.BaselinePath} ({verify.BaselineWarningCount} keys)");
            if (verify.NewWarningCount > 0)
                logger.Info($"New verify warnings vs baseline: {verify.NewWarningCount}");
            if (!string.IsNullOrWhiteSpace(writtenBaselinePath))
                logger.Info($"Verify baseline written: {writtenBaselinePath}");
        }

        if (warningSummary && filteredWarnings.Length > 0)
            logger.Info(WebWarningBucketer.BuildTopBucketsSummary(filteredWarnings, warningSummaryTop));

        logger.Info($"Verify: {verify.Errors.Length} errors, {filteredWarnings.Length} warnings");
        return verifySuccess ? 0 : 1;
    }

    private static int HandleMarkdownFix(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var rootPath = TryGetOptionValue(subArgs, "--root") ??
                       TryGetOptionValue(subArgs, "--path");
        var configPath = TryGetOptionValue(subArgs, "--config");
        var include = ReadOptionList(subArgs, "--include");
        var exclude = ReadOptionList(subArgs, "--exclude");
        var apply = HasOption(subArgs, "--apply") ||
                    HasOption(subArgs, "--write") ||
                    HasOption(subArgs, "--in-place");

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                var contentRoot = string.IsNullOrWhiteSpace(spec.ContentRoot) ? "content" : spec.ContentRoot;
                rootPath = Path.IsPathRooted(contentRoot)
                    ? contentRoot
                    : Path.Combine(plan.RootPath, contentRoot);
            }
        }

        if (string.IsNullOrWhiteSpace(rootPath))
            return Fail("Missing required --path or --config.", outputJson, logger, "web.markdown-fix");

        var result = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
        {
            RootPath = rootPath,
            Include = include.ToArray(),
            Exclude = exclude.ToArray(),
            ApplyChanges = apply
        });

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.markdown-fix",
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebMarkdownFixResult)
            });
            return result.Success ? 0 : 1;
        }

        logger.Success(apply
            ? $"Markdown fixer updated {result.ChangedFileCount} file(s)."
            : $"Markdown fixer found {result.ChangedFileCount} file(s) to update (dry-run).");
        logger.Info($"Files scanned: {result.FileCount}");
        logger.Info($"Replacements: {result.ReplacementCount}");
        if (result.Warnings.Length > 0)
        {
            foreach (var warning in result.Warnings)
                logger.Warn(warning);
        }

        return result.Success ? 0 : 1;
    }

    private static int HandleScaffold(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");
        var name = TryGetOptionValue(subArgs, "--name");
        var baseUrl = TryGetOptionValue(subArgs, "--base-url");
        var engine = TryGetOptionValue(subArgs, "--engine") ??
                     TryGetOptionValue(subArgs, "--theme-engine");

        if (string.IsNullOrWhiteSpace(outPath))
            return Fail("Missing required --out.", outputJson, logger, "web.scaffold");

        var result = WebSiteScaffolder.Scaffold(outPath, name, baseUrl, engine);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.scaffold",
                Success = true,
                ExitCode = 0,
                Config = "web",
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebScaffoldResult)
            });
            return 0;
        }

        logger.Success($"Web scaffold output: {result.OutputPath}");
        logger.Info($"Created files: {result.CreatedFileCount}");
        logger.Info($"Theme engine: {result.ThemeEngine}");
        return 0;
    }

    private static int HandleNew(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var configPath = TryGetOptionValue(subArgs, "--config");
        var collectionName = TryGetOptionValue(subArgs, "--collection") ?? "pages";
        var title = TryGetOptionValue(subArgs, "--title") ?? TryGetOptionValue(subArgs, "--name");
        var slug = TryGetOptionValue(subArgs, "--slug");
        var outPath = TryGetOptionValue(subArgs, "--out") ??
                      TryGetOptionValue(subArgs, "--out-path") ??
                      TryGetOptionValue(subArgs, "--output-path");

        if (string.IsNullOrWhiteSpace(configPath))
            return Fail("Missing required --config.", outputJson, logger, "web.new");
        if (string.IsNullOrWhiteSpace(title))
            return Fail("Missing required --title.", outputJson, logger, "web.new");

        var fullConfigPath = ResolveExistingFilePath(configPath);
        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

        var collection = spec.Collections.FirstOrDefault(c =>
            string.Equals(c.Name, collectionName, StringComparison.OrdinalIgnoreCase));
        if (collection is null)
            return Fail($"Collection not found: {collectionName}", outputJson, logger, "web.new");

        var slugValue = string.IsNullOrWhiteSpace(slug) ? Slugify(title) : slug.Trim();
        if (string.IsNullOrWhiteSpace(slugValue))
            return Fail("Missing slug (could not derive from title).", outputJson, logger, "web.new");

        var collectionRoot = ResolvePathRelative(plan.RootPath, collection.Input);
        var targetPath = !string.IsNullOrWhiteSpace(outPath)
            ? ResolvePathRelative(plan.RootPath, outPath)
            : Path.Combine(collectionRoot, slugValue.Replace('/', Path.DirectorySeparatorChar) + ".md");

        if (File.Exists(targetPath))
            return Fail($"File already exists: {targetPath}", outputJson, logger, "web.new");

        var archetypesRoot = ResolvePathRelative(plan.RootPath, spec.ArchetypesRoot ?? "archetypes");
        var archetypePath = Path.Combine(archetypesRoot, $"{collection.Name}.md");
        if (!File.Exists(archetypePath))
            archetypePath = Path.Combine(archetypesRoot, "default.md");

        var template = File.Exists(archetypePath)
            ? File.ReadAllText(archetypePath)
            : DefaultArchetypeTemplate;
        var content = ApplyArchetypeTemplate(template, title, slugValue, collection.Name);

        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
            Directory.CreateDirectory(targetDir);
        File.WriteAllText(targetPath, content);

        var result = new WebContentScaffoldResult
        {
            OutputPath = targetPath,
            Collection = collection.Name,
            Title = title,
            Slug = slugValue,
            Created = true
        };

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.new",
                Success = true,
                ExitCode = 0,
                Config = "web",
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebContentScaffoldResult)
            });
            return 0;
        }

        logger.Success($"Created {targetPath}");
        logger.Info($"Collection: {collection.Name}");
        logger.Info($"Slug: {slugValue}");
        return 0;
    }

    private static int HandleServe(string[] subArgs, bool outputJson, WebConsoleLogger logger)
    {
        var servePath = TryGetOptionValue(subArgs, "--path") ??
                        TryGetOptionValue(subArgs, "--dir") ??
                        TryGetOptionValue(subArgs, "--out");
        var config = TryGetOptionValue(subArgs, "--config");
        var portText = TryGetOptionValue(subArgs, "--port");
        var host = TryGetOptionValue(subArgs, "--host") ?? "localhost";

        if (!string.IsNullOrWhiteSpace(config))
        {
            var fullConfigPath = ResolveExistingFilePath(config);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var outPath = TryGetOptionValue(subArgs, "--out") ??
                          TryGetOptionValue(subArgs, "--out-path") ??
                          TryGetOptionValue(subArgs, "--output-path");
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(Path.GetDirectoryName(fullConfigPath) ?? ".", "_site");
            WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
            servePath = outPath;
        }

        if (string.IsNullOrWhiteSpace(servePath))
        {
            PrintUsage();
            return 2;
        }

        var port = 8080;
        if (!string.IsNullOrWhiteSpace(portText) && int.TryParse(portText, out var parsedPort))
            port = parsedPort;

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        WebStaticServer.Serve(servePath, host, port, cts.Token, message => logger.Info(message));
        return 0;
    }

    private static int HandlePipeline(string[] subArgs, bool outputJson, WebConsoleLogger logger, int outputSchemaVersion)
    {
        var pipelinePath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(pipelinePath))
            return Fail("Missing required --config.", outputJson, logger, "web.pipeline");

        var fullPath = ResolveExistingFilePath(pipelinePath);
        var profilePipeline = HasOption(subArgs, "--profile");
        var watch = HasOption(subArgs, "--watch");
        var devMode = HasOption(subArgs, "--dev");
        var fastMode = devMode || HasOption(subArgs, "--fast");
        var mode = TryGetOptionValue(subArgs, "--mode");
        if (devMode && string.IsNullOrWhiteSpace(mode))
            mode = "dev";

        var onlyTasks = ReadOptionList(subArgs, "--only");
        var skipTasks = ReadOptionList(subArgs, "--skip");

        // Dev mode should be a fast feedback loop by default. Users can still opt-in to heavy tasks
        // by using --only or explicitly omitting --dev.
        if (devMode && onlyTasks.Count == 0 && skipTasks.Count == 0)
        {
            skipTasks.Add("optimize");
            skipTasks.Add("audit");
        }

        if (watch)
        {
            if (outputJson)
                return Fail("--watch is not supported with JSON output.", outputJson, logger, "web.pipeline");

            return WebPipelineRunner.WatchPipeline(
                fullPath,
                logger,
                forceProfile: profilePipeline,
                fast: fastMode,
                mode: mode,
                onlyTasks: onlyTasks.Count > 0 ? onlyTasks.ToArray() : null,
                skipTasks: skipTasks.Count > 0 ? skipTasks.ToArray() : null);
        }

        var result = WebPipelineRunner.RunPipeline(
            fullPath,
            logger,
            forceProfile: profilePipeline,
            fast: fastMode,
            mode: mode,
            onlyTasks: onlyTasks.Count > 0 ? onlyTasks.ToArray() : null,
            skipTasks: skipTasks.Count > 0 ? skipTasks.ToArray() : null);

        if (outputJson)
        {
            WebCliJsonWriter.Write(new WebCliJsonEnvelope
            {
                SchemaVersion = outputSchemaVersion,
                Command = "web.pipeline",
                Success = result.Success,
                ExitCode = result.Success ? 0 : 1,
                Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebPipelineResult)
            });
            return result.Success ? 0 : 1;
        }

        foreach (var step in result.Steps)
        {
            if (step.Success)
                logger.Success($"{step.Task}: {step.Message}");
            else
                logger.Error($"{step.Task}: {step.Message}");
        }

        logger.Info($"Pipeline duration: {result.DurationMs} ms");
        if (!string.IsNullOrWhiteSpace(result.CachePath))
            logger.Info($"Pipeline cache: {result.CachePath}");
        if (!string.IsNullOrWhiteSpace(result.ProfilePath))
            logger.Info($"Pipeline profile: {result.ProfilePath}");

        return result.Success ? 0 : 1;
    }
}

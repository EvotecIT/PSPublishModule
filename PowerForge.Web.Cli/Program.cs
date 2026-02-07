using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

const int OutputSchemaVersion = 1;
const string DefaultArchetypeTemplate = @"---
title: {{title}}
slug: {{slug}}
date: {{date}}
collection: {{collection}}
---

# {{title}}
";

var argv = args ?? Array.Empty<string>();
if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

var subCommand = argv[0].ToLowerInvariant();
var subArgs = argv.Skip(1).ToArray();
var outputJson = IsJsonOutput(subArgs);
EnsureUtf8ConsoleEncoding();
var logger = new WebConsoleLogger();

try
{
    switch (subCommand)
    {
        case "plan":
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
                    SchemaVersion = OutputSchemaVersion,
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
                foreach (var c in plan.Collections)
                    logger.Info($"Collection: {c.Name} ({c.FileCount} files) -> {c.OutputPath}");
            }
            if (plan.Projects.Length > 0)
            {
                foreach (var p in plan.Projects)
                    logger.Info($"Project: {p.Name} ({p.Slug}) ({p.ContentFileCount} files)");
            }
            if (plan.RouteOverrideCount > 0) logger.Info($"Route overrides: {plan.RouteOverrideCount}");
            if (plan.RedirectCount > 0) logger.Info($"Redirects: {plan.RedirectCount}");
            return 0;
        }
        case "build":
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
            var res = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.build",
                    Success = true,
                    ExitCode = 0,
                    Config = "web",
                    ConfigPath = specPath,
                    Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                    Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                    Result = WebCliJson.SerializeToElement(res, WebCliJson.Context.WebBuildResult)
                });
                return 0;
            }

            logger.Success($"Web build output: {res.OutputPath}");
            logger.Info($"Plan: {res.PlanPath}");
            logger.Info($"Spec: {res.SpecPath}");
            logger.Info($"Redirects: {res.RedirectsPath}");
            return 0;
        }
        case "publish":
        {
            var configPath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(configPath))
                return Fail("Missing required --config.", outputJson, logger, "web.publish");

            var fullConfigPath = ResolveExistingFilePath(configPath);
            var publishSpec = JsonSerializer.Deserialize<WebPublishSpec>(
                File.ReadAllText(fullConfigPath), WebCliJson.Options);
            if (publishSpec is null)
                return Fail("Invalid publish spec.", outputJson, logger, "web.publish");

            if (string.IsNullOrWhiteSpace(publishSpec.Build.Config) || string.IsNullOrWhiteSpace(publishSpec.Build.Out))
                return Fail("Publish spec requires Build.Config and Build.Out.", outputJson, logger, "web.publish");
            if (string.IsNullOrWhiteSpace(publishSpec.Publish.Project) || string.IsNullOrWhiteSpace(publishSpec.Publish.Out))
                return Fail("Publish spec requires Publish.Project and Publish.Out.", outputJson, logger, "web.publish");

            var baseDir = Path.GetDirectoryName(fullConfigPath) ?? ".";
            var buildConfig = ResolvePathRelative(baseDir, publishSpec.Build.Config);
            var buildOut = ResolvePathRelative(baseDir, publishSpec.Build.Out);

            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(buildConfig, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            if (publishSpec.Build.Clean)
                WebCliFileSystem.CleanOutputDirectory(buildOut);
            var build = WebSiteBuilder.Build(spec, plan, buildOut, WebCliJson.Options);

            int? overlayCopied = null;
            if (publishSpec.Overlay is not null)
            {
                if (string.IsNullOrWhiteSpace(publishSpec.Overlay.Source) ||
                    string.IsNullOrWhiteSpace(publishSpec.Overlay.Destination))
                    return Fail("Overlay requires Source and Destination.", outputJson, logger, "web.publish");

                var overlay = WebStaticOverlay.Apply(new WebStaticOverlayOptions
                {
                    SourceRoot = ResolvePathRelative(baseDir, publishSpec.Overlay.Source),
                    DestinationRoot = ResolvePathRelative(baseDir, publishSpec.Overlay.Destination),
                    Include = publishSpec.Overlay.Include ?? Array.Empty<string>(),
                    Exclude = publishSpec.Overlay.Exclude ?? Array.Empty<string>()
                });
                overlayCopied = overlay.CopiedCount;
            }

            var publishOut = ResolvePathRelative(baseDir, publishSpec.Publish.Out);
            var publishResult = WebDotNetRunner.Publish(new WebDotNetPublishOptions
            {
                ProjectPath = ResolvePathRelative(baseDir, publishSpec.Publish.Project),
                OutputPath = publishOut,
                Configuration = publishSpec.Publish.Configuration,
                Framework = publishSpec.Publish.Framework,
                Runtime = publishSpec.Publish.Runtime,
                SelfContained = publishSpec.Publish.SelfContained,
                NoBuild = publishSpec.Publish.NoBuild,
                NoRestore = publishSpec.Publish.NoRestore,
                DefineConstants = publishSpec.Publish.DefineConstants
            });

            if (!publishResult.Success)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(publishResult.Error)
                    ? "dotnet publish failed."
                    : publishResult.Error);

            if (publishSpec.Publish.ApplyBlazorFixes)
            {
                WebBlazorPublishFixer.Apply(new WebBlazorPublishFixOptions
                {
                    PublishRoot = publishOut,
                    BaseHref = publishSpec.Publish.BaseHref
                });
            }

            int? optimizeUpdated = null;
            if (publishSpec.Optimize is not null)
            {
                var optimizeRoot = string.IsNullOrWhiteSpace(publishSpec.Optimize.SiteRoot)
                    ? publishOut
                    : ResolvePathRelative(baseDir, publishSpec.Optimize.SiteRoot);

                AssetPolicySpec? policy = null;
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.Config))
                {
                    var optimizeConfigPath = ResolvePathRelative(baseDir, publishSpec.Optimize.Config);
                    var (optimizeSpec, _) = WebSiteSpecLoader.LoadWithPath(optimizeConfigPath, WebCliJson.Options);
                    policy = optimizeSpec.AssetPolicy;
                }
                if (publishSpec.Optimize.CacheHeaders)
                {
                    policy ??= new AssetPolicySpec();
                    policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
                    policy.CacheHeaders.Enabled = true;
                    if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersOut))
                        policy.CacheHeaders.OutputPath = publishSpec.Optimize.CacheHeadersOut;
                    if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersHtml))
                        policy.CacheHeaders.HtmlCacheControl = publishSpec.Optimize.CacheHeadersHtml;
                    if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CacheHeadersAssets))
                        policy.CacheHeaders.ImmutableCacheControl = publishSpec.Optimize.CacheHeadersAssets;
                    if (publishSpec.Optimize.CacheHeadersPaths is { Length: > 0 })
                        policy.CacheHeaders.ImmutablePaths = publishSpec.Optimize.CacheHeadersPaths;
                }

                var optimizerOptions = new WebAssetOptimizerOptions
                {
                    SiteRoot = optimizeRoot,
                    CriticalCssPath = string.IsNullOrWhiteSpace(publishSpec.Optimize.CriticalCss)
                        ? null
                        : ResolvePathRelative(baseDir, publishSpec.Optimize.CriticalCss),
                    MinifyHtml = publishSpec.Optimize.MinifyHtml,
                    MinifyCss = publishSpec.Optimize.MinifyCss,
                    MinifyJs = publishSpec.Optimize.MinifyJs,
                    OptimizeImages = publishSpec.Optimize.OptimizeImages,
                    ImageExtensions = publishSpec.Optimize.ImageExtensions is { Length: > 0 }
                        ? publishSpec.Optimize.ImageExtensions
                        : new[] { ".png", ".jpg", ".jpeg", ".webp" },
                    ImageInclude = publishSpec.Optimize.ImageInclude ?? Array.Empty<string>(),
                    ImageExclude = publishSpec.Optimize.ImageExclude ?? Array.Empty<string>(),
                    ImageQuality = publishSpec.Optimize.ImageQuality ?? 82,
                    ImageStripMetadata = publishSpec.Optimize.ImageStripMetadata ?? true,
                    ImageGenerateWebp = publishSpec.Optimize.ImageGenerateWebp,
                    ImageGenerateAvif = publishSpec.Optimize.ImageGenerateAvif,
                    ImagePreferNextGen = publishSpec.Optimize.ImagePreferNextGen,
                    ResponsiveImageWidths = publishSpec.Optimize.ResponsiveImageWidths ?? Array.Empty<int>(),
                    EnhanceImageTags = publishSpec.Optimize.EnhanceImageTags,
                    ImageMaxBytesPerFile = publishSpec.Optimize.ImageMaxBytesPerFile ?? 0,
                    ImageMaxTotalBytes = publishSpec.Optimize.ImageMaxTotalBytes ?? 0,
                    HashAssets = publishSpec.Optimize.HashAssets,
                    HashExtensions = publishSpec.Optimize.HashExtensions is { Length: > 0 } ? publishSpec.Optimize.HashExtensions : new[] { ".css", ".js" },
                    HashExclude = publishSpec.Optimize.HashExclude ?? Array.Empty<string>(),
                    HashManifestPath = publishSpec.Optimize.HashManifest,
                    AssetPolicy = policy
                };
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CssPattern))
                    optimizerOptions.CssLinkPattern = publishSpec.Optimize.CssPattern;

                var optimizeResult = WebAssetOptimizer.OptimizeDetailed(optimizerOptions);
                if (publishSpec.Optimize.ImageFailOnBudget && optimizeResult.ImageBudgetExceeded)
                    throw new InvalidOperationException($"Image budget exceeded: {string.Join(" | ", optimizeResult.ImageBudgetWarnings)}");
                optimizeUpdated = optimizeResult.UpdatedCount;
            }

            var publishSummary = new WebPublishResult
            {
                Success = true,
                BuildOutputPath = buildOut,
                OverlayCopiedCount = overlayCopied,
                PublishOutputPath = publishOut,
                OptimizeUpdatedCount = optimizeUpdated
            };

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.publish",
                    Success = true,
                    ExitCode = 0,
                    Config = "web.publish",
                    ConfigPath = fullConfigPath,
                    Spec = WebCliJson.SerializeToElement(publishSpec, WebCliJson.Context.WebPublishSpec),
                    Result = WebCliJson.SerializeToElement(publishSummary, WebCliJson.Context.WebPublishResult)
                });
                return 0;
            }

            logger.Success("Web publish completed.");
            logger.Info($"Build output: {buildOut}");
            if (overlayCopied.HasValue) logger.Info($"Overlay copied: {overlayCopied.Value}");
            logger.Info($"Publish output: {publishOut}");
            if (optimizeUpdated.HasValue) logger.Info($"Optimize updated: {optimizeUpdated.Value}");
            return 0;
        }
        case "verify":
        {
            var configPath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(configPath))
                return Fail("Missing required --config.", outputJson, logger, "web.verify");

            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var verify = WebSiteVerifier.Verify(spec, plan);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.verify",
                    Success = verify.Success,
                    ExitCode = verify.Success ? 0 : 1,
                    Config = "web",
                    ConfigPath = specPath,
                    Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                    Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                    Result = WebCliJson.SerializeToElement(verify, WebCliJson.Context.WebVerifyResult)
                });
                return verify.Success ? 0 : 1;
            }

            if (verify.Warnings.Length > 0)
            {
                foreach (var w in verify.Warnings)
                    logger.Warn(w);
            }
            if (verify.Errors.Length > 0)
            {
                foreach (var e in verify.Errors)
                    logger.Error(e);
            }

            if (verify.Success)
                logger.Success("Web verify passed.");

            return verify.Success ? 0 : 1;
        }
        case "doctor":
        {
            var configPath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(configPath))
                return Fail("Missing required --config.", outputJson, logger, "web.doctor");

            var siteRootArg = TryGetOptionValue(subArgs, "--site-root") ??
                              TryGetOptionValue(subArgs, "--root") ??
                              TryGetOptionValue(subArgs, "--path");
            var outPath = TryGetOptionValue(subArgs, "--out") ??
                          TryGetOptionValue(subArgs, "--out-path") ??
                          TryGetOptionValue(subArgs, "--output-path");
            var runBuild = !HasOption(subArgs, "--no-build");
            var runVerify = !HasOption(subArgs, "--no-verify");
            var runAudit = !HasOption(subArgs, "--no-audit");

            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);

            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(Path.GetDirectoryName(fullConfigPath) ?? ".", "_site");
            var siteRoot = string.IsNullOrWhiteSpace(siteRootArg) ? outPath : siteRootArg;

            if (runBuild)
            {
                WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                siteRoot = outPath;
            }

            if (runAudit && (string.IsNullOrWhiteSpace(siteRoot) || !Directory.Exists(siteRoot)))
                return Fail("Doctor audit requires an existing site root. Use --out with --build or pass --site-root.", outputJson, logger, "web.doctor");

            var verify = runVerify ? WebSiteVerifier.Verify(spec, plan) : null;
            var audit = runAudit ? RunDoctorAudit(siteRoot!, subArgs) : null;
            var recommendations = BuildDoctorRecommendations(verify, audit);
            var success = (verify?.Success ?? true) && (audit?.Success ?? true);

            var doctorResult = new WebDoctorResult
            {
                Success = success,
                ConfigPath = specPath,
                SiteRoot = siteRoot ?? string.Empty,
                BuildExecuted = runBuild,
                VerifyExecuted = runVerify,
                AuditExecuted = runAudit,
                Verify = verify,
                Audit = audit,
                Recommendations = recommendations
            };

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.doctor",
                    Success = doctorResult.Success,
                    ExitCode = doctorResult.Success ? 0 : 1,
                    Config = "web",
                    ConfigPath = specPath,
                    Spec = WebCliJson.SerializeToElement(spec, WebCliJson.Context.SiteSpec),
                    Plan = WebCliJson.SerializeToElement(plan, WebCliJson.Context.WebSitePlan),
                    Result = WebCliJson.SerializeToElement(doctorResult, WebCliJson.Context.WebDoctorResult)
                });
                return doctorResult.Success ? 0 : 1;
            }

            if (runVerify && verify is not null)
            {
                foreach (var warning in verify.Warnings)
                    logger.Warn($"verify: {warning}");
                foreach (var error in verify.Errors)
                    logger.Error($"verify: {error}");
            }

            if (runAudit && audit is not null)
            {
                foreach (var warning in audit.Warnings)
                    logger.Warn($"audit: {warning}");
                foreach (var error in audit.Errors)
                    logger.Error($"audit: {error}");
            }

            if (doctorResult.Success)
                logger.Success("Web doctor passed.");

            logger.Info($"Build executed: {doctorResult.BuildExecuted}");
            logger.Info($"Verify executed: {doctorResult.VerifyExecuted}");
            logger.Info($"Audit executed: {doctorResult.AuditExecuted}");
            if (verify is not null)
                logger.Info($"Verify: {verify.Errors.Length} errors, {verify.Warnings.Length} warnings");
            if (audit is not null)
            {
                logger.Info($"Audit: {audit.ErrorCount} errors, {audit.WarningCount} warnings");
                if (!string.IsNullOrWhiteSpace(audit.SummaryPath))
                    logger.Info($"Audit summary: {audit.SummaryPath}");
                if (!string.IsNullOrWhiteSpace(audit.SarifPath))
                    logger.Info($"Audit SARIF: {audit.SarifPath}");
            }
            if (recommendations.Length > 0)
            {
                foreach (var recommendation in recommendations)
                    logger.Info($"Recommendation: {recommendation}");
            }

            return doctorResult.Success ? 0 : 1;
        }
        case "markdown-fix":
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
                    SchemaVersion = OutputSchemaVersion,
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
        case "audit":
        {
            var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                           TryGetOptionValue(subArgs, "--root") ??
                           TryGetOptionValue(subArgs, "--path") ??
                           TryGetOptionValue(subArgs, "--out");
            var configPath = TryGetOptionValue(subArgs, "--config");

            if (!string.IsNullOrWhiteSpace(configPath))
            {
                var fullConfigPath = ResolveExistingFilePath(configPath);
                var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
                var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                var outPath = TryGetOptionValue(subArgs, "--out") ??
                              TryGetOptionValue(subArgs, "--out-path") ??
                              TryGetOptionValue(subArgs, "--output-path");
                if (string.IsNullOrWhiteSpace(outPath))
                    outPath = Path.Combine(Path.GetDirectoryName(fullConfigPath) ?? ".", "_site");
                WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                siteRoot = outPath;
            }

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.audit");

            var include = ReadOptionList(subArgs, "--include");
            var exclude = ReadOptionList(subArgs, "--exclude");
            var ignoreNav = ReadOptionList(subArgs, "--ignore-nav", "--ignore-nav-path");
            var navIgnorePrefixes = ReadOptionList(subArgs, "--nav-ignore-prefix", "--nav-ignore-prefixes");
            var navRequiredLinks = ReadOptionList(subArgs, "--nav-required-link", "--nav-required-links");
            var navProfilesPath = TryGetOptionValue(subArgs, "--nav-profiles");
            var minNavCoverageText = TryGetOptionValue(subArgs, "--min-nav-coverage");
            var requiredRoutes = ReadOptionList(subArgs, "--required-route", "--required-routes");
            var useDefaultIgnoreNav = !HasOption(subArgs, "--no-default-ignore-nav");
            var navSelector = TryGetOptionValue(subArgs, "--nav-selector") ?? "nav";
            var navRequired = !HasOption(subArgs, "--nav-optional");
            var rendered = HasOption(subArgs, "--rendered");
            var renderedHeadless = !HasOption(subArgs, "--rendered-headful");
            var renderedEngine = TryGetOptionValue(subArgs, "--rendered-engine");
            var renderedEnsureInstalled = !HasOption(subArgs, "--rendered-no-install");
            var renderedMaxText = TryGetOptionValue(subArgs, "--rendered-max");
            var renderedTimeoutText = TryGetOptionValue(subArgs, "--rendered-timeout");
            var renderedBaseUrl = TryGetOptionValue(subArgs, "--rendered-base-url");
            var renderedHost = TryGetOptionValue(subArgs, "--rendered-host");
            var renderedPortText = TryGetOptionValue(subArgs, "--rendered-port");
            var renderedNoServe = HasOption(subArgs, "--rendered-no-serve");
            var renderedNoConsoleErrors = HasOption(subArgs, "--rendered-no-console-errors");
            var renderedNoConsoleWarnings = HasOption(subArgs, "--rendered-no-console-warnings");
            var renderedNoFailures = HasOption(subArgs, "--rendered-no-failures");
            var renderedInclude = ReadOptionList(subArgs, "--rendered-include");
            var renderedExclude = ReadOptionList(subArgs, "--rendered-exclude");
            var summaryEnabled = HasOption(subArgs, "--summary");
            var summaryPath = TryGetOptionValue(subArgs, "--summary-path");
            var summaryMaxText = TryGetOptionValue(subArgs, "--summary-max");
            var sarifEnabled = HasOption(subArgs, "--sarif");
            var sarifPath = TryGetOptionValue(subArgs, "--sarif-path");
            var useDefaultExclude = !HasOption(subArgs, "--no-default-exclude");
            var baselineGenerate = HasOption(subArgs, "--baseline-generate");
            var baselineUpdate = HasOption(subArgs, "--baseline-update");
            var baselinePathValue = TryGetOptionValue(subArgs, "--baseline");
            var failOnWarnings = HasOption(subArgs, "--fail-on-warnings");
            var failOnNew = HasOption(subArgs, "--fail-on-new");
            var maxErrorsText = TryGetOptionValue(subArgs, "--max-errors");
            var maxWarningsText = TryGetOptionValue(subArgs, "--max-warnings");
            var failCategories = ReadOptionList(subArgs, "--fail-category", "--fail-categories");
            var navCanonical = TryGetOptionValue(subArgs, "--nav-canonical");
            var navCanonicalSelector = TryGetOptionValue(subArgs, "--nav-canonical-selector");
            var navCanonicalRequired = HasOption(subArgs, "--nav-canonical-required");
            var checkUtf8 = !HasOption(subArgs, "--no-utf8");
            var checkMetaCharset = !HasOption(subArgs, "--no-meta-charset");
            var checkReplacementChars = !HasOption(subArgs, "--no-replacement-char-check");
            var checkHeadingOrder = !HasOption(subArgs, "--no-heading-order");
            var checkLinkPurpose = !HasOption(subArgs, "--no-link-purpose");
            var checkNetworkHints = !HasOption(subArgs, "--no-network-hints");
            var checkRenderBlocking = !HasOption(subArgs, "--no-render-blocking");
            var maxHeadBlockingText = TryGetOptionValue(subArgs, "--max-head-blocking");

            var ignoreNavPatterns = BuildIgnoreNavPatterns(ignoreNav, useDefaultIgnoreNav);
            var renderedMaxPages = ParseIntOption(renderedMaxText, 20);
            var renderedTimeoutMs = ParseIntOption(renderedTimeoutText, 30000);
            var renderedPort = ParseIntOption(renderedPortText, 0);
            var summaryMax = ParseIntOption(summaryMaxText, 10);
            var maxErrors = ParseIntOption(maxErrorsText, -1);
            var maxWarnings = ParseIntOption(maxWarningsText, -1);
            var minNavCoveragePercent = ParseIntOption(minNavCoverageText, 0);
            var maxHeadBlockingResources = ParseIntOption(maxHeadBlockingText, new WebAuditOptions().MaxHeadBlockingResources);
            if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePathValue))
                baselinePathValue = "audit-baseline.json";
            var resolvedSummaryPath = ResolveSummaryPath(summaryEnabled, summaryPath);
            var resolvedSarifPath = ResolveSarifPath(sarifEnabled, sarifPath);
            var navProfiles = LoadAuditNavProfiles(navProfilesPath);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = siteRoot,
                Include = include.ToArray(),
                Exclude = exclude.ToArray(),
                UseDefaultExcludes = useDefaultExclude,
                IgnoreNavFor = ignoreNavPatterns,
                NavSelector = navSelector,
                NavRequired = navRequired,
                NavIgnorePrefixes = navIgnorePrefixes.ToArray(),
                NavRequiredLinks = navRequiredLinks.ToArray(),
                NavProfiles = navProfiles,
                MinNavCoveragePercent = minNavCoveragePercent,
                RequiredRoutes = requiredRoutes.ToArray(),
                CheckLinks = !HasOption(subArgs, "--no-links"),
                CheckAssets = !HasOption(subArgs, "--no-assets"),
                CheckNavConsistency = !HasOption(subArgs, "--no-nav"),
                CheckTitles = !(HasOption(subArgs, "--no-titles") || HasOption(subArgs, "--no-title")),
                CheckDuplicateIds = !HasOption(subArgs, "--no-ids"),
                CheckHtmlStructure = !HasOption(subArgs, "--no-structure"),
                CheckRendered = rendered,
                RenderedHeadless = renderedHeadless,
                RenderedEngine = renderedEngine ?? "Chromium",
                RenderedEnsureInstalled = renderedEnsureInstalled,
                RenderedBaseUrl = renderedBaseUrl,
                RenderedServe = !renderedNoServe,
                RenderedServeHost = string.IsNullOrWhiteSpace(renderedHost) ? "localhost" : renderedHost,
                RenderedServePort = renderedPort,
                RenderedMaxPages = renderedMaxPages,
                RenderedTimeoutMs = renderedTimeoutMs,
                RenderedCheckConsoleErrors = !renderedNoConsoleErrors,
                RenderedCheckConsoleWarnings = !renderedNoConsoleWarnings,
                RenderedCheckFailedRequests = !renderedNoFailures,
                RenderedInclude = renderedInclude.ToArray(),
                RenderedExclude = renderedExclude.ToArray(),
                SummaryPath = resolvedSummaryPath,
                SarifPath = resolvedSarifPath,
                SummaryMaxIssues = summaryMax,
                BaselinePath = baselinePathValue,
                FailOnWarnings = failOnWarnings,
                FailOnNewIssues = failOnNew,
                MaxErrors = maxErrors,
                MaxWarnings = maxWarnings,
                FailOnCategories = failCategories.ToArray(),
                NavCanonicalPath = navCanonical,
                NavCanonicalSelector = navCanonicalSelector,
                NavCanonicalRequired = navCanonicalRequired,
                CheckUtf8 = checkUtf8,
                CheckMetaCharset = checkMetaCharset,
                CheckUnicodeReplacementChars = checkReplacementChars,
                CheckHeadingOrder = checkHeadingOrder,
                CheckLinkPurposeConsistency = checkLinkPurpose,
                CheckNetworkHints = checkNetworkHints,
                CheckRenderBlockingResources = checkRenderBlocking,
                MaxHeadBlockingResources = maxHeadBlockingResources
            });

            string? writtenBaselinePath = null;
            if (baselineGenerate || baselineUpdate)
            {
                writtenBaselinePath = WebAuditBaselineStore.Write(siteRoot, baselinePathValue, result, baselineUpdate, logger);
                result.BaselinePath = writtenBaselinePath;
            }

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.audit",
                    Success = result.Success,
                    ExitCode = result.Success ? 0 : 1,
                    Config = "web",
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebAuditResult)
                });
                return result.Success ? 0 : 1;
            }

            if (result.Warnings.Length > 0)
            {
                foreach (var w in result.Warnings)
                    logger.Warn(w);
            }
            if (result.Errors.Length > 0)
            {
                foreach (var e in result.Errors)
                    logger.Error(e);
            }

            if (result.Success)
                logger.Success("Web audit passed.");

            logger.Info($"Pages: {result.PageCount}");
            logger.Info($"Links: {result.LinkCount} (broken {result.BrokenLinkCount})");
            logger.Info($"Assets: {result.AssetCount} (missing {result.MissingAssetCount})");
            logger.Info($"Navigation: checked {result.NavCheckedCount}, ignored {result.NavIgnoredCount}, coverage {result.NavCoveragePercent:0.0}%, mismatches {result.NavMismatchCount}");
            if (result.RequiredRouteCount > 0)
                logger.Info($"Required routes: {result.RequiredRouteCount} (missing {result.MissingRequiredRouteCount})");
            logger.Info($"Issues: {result.ErrorCount} errors, {result.WarningCount} warnings");
            if (result.NewIssueCount > 0 || !string.IsNullOrWhiteSpace(result.BaselinePath))
                logger.Info($"New issues: {result.NewIssueCount} (errors {result.NewErrorCount}, warnings {result.NewWarningCount})");
            if (result.DuplicateIdCount > 0)
                logger.Info($"Duplicate IDs: {result.DuplicateIdCount}");
            if (result.RenderedPageCount > 0)
            {
                logger.Info($"Rendered pages: {result.RenderedPageCount}");
                if (result.RenderedConsoleErrorCount > 0)
                    logger.Info($"Rendered console errors: {result.RenderedConsoleErrorCount}");
                if (result.RenderedConsoleWarningCount > 0)
                    logger.Info($"Rendered console warnings: {result.RenderedConsoleWarningCount}");
                if (result.RenderedFailedRequestCount > 0)
                    logger.Info($"Rendered failed requests: {result.RenderedFailedRequestCount}");
            }
            if (!string.IsNullOrWhiteSpace(result.BaselinePath))
                logger.Info($"Baseline: {result.BaselinePath} ({result.BaselineIssueCount} keys)");
            if (!string.IsNullOrWhiteSpace(writtenBaselinePath))
                logger.Info($"Baseline written: {writtenBaselinePath}");
            if (!string.IsNullOrWhiteSpace(result.SummaryPath))
                logger.Info($"Audit summary: {result.SummaryPath}");
            if (!string.IsNullOrWhiteSpace(result.SarifPath))
                logger.Info($"Audit SARIF: {result.SarifPath}");

            return result.Success ? 0 : 1;
        }
        case "scaffold":
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

            var res = WebSiteScaffolder.Scaffold(outPath, name, baseUrl, engine);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.scaffold",
                    Success = true,
                    ExitCode = 0,
                    Config = "web",
                    Result = WebCliJson.SerializeToElement(res, WebCliJson.Context.WebScaffoldResult)
                });
                return 0;
            }

            logger.Success($"Web scaffold output: {res.OutputPath}");
            logger.Info($"Created files: {res.CreatedFileCount}");
            logger.Info($"Theme engine: {res.ThemeEngine}");
            return 0;
        }
        case "new":
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
                    SchemaVersion = OutputSchemaVersion,
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
        case "serve":
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
        case "apidocs":
        {
            var typeText = TryGetOptionValue(subArgs, "--type");
            var xmlPath = TryGetOptionValue(subArgs, "--xml");
            var helpPath = TryGetOptionValue(subArgs, "--help-path");
            var outPath = TryGetOptionValue(subArgs, "--out") ??
                          TryGetOptionValue(subArgs, "--out-path") ??
                          TryGetOptionValue(subArgs, "--output-path");
            var assemblyPath = TryGetOptionValue(subArgs, "--assembly");
            var title = TryGetOptionValue(subArgs, "--title");
            var baseUrl = TryGetOptionValue(subArgs, "--base-url") ?? "/api";
            var format = TryGetOptionValue(subArgs, "--format");
            var cssHref = TryGetOptionValue(subArgs, "--css");
            var headerHtml = TryGetOptionValue(subArgs, "--header-html");
            var footerHtml = TryGetOptionValue(subArgs, "--footer-html");
            var template = TryGetOptionValue(subArgs, "--template");
            var templateRoot = TryGetOptionValue(subArgs, "--template-root");
            var indexTemplate = TryGetOptionValue(subArgs, "--template-index");
            var typeTemplate = TryGetOptionValue(subArgs, "--template-type");
            var docsIndexTemplate = TryGetOptionValue(subArgs, "--template-docs-index");
            var docsTypeTemplate = TryGetOptionValue(subArgs, "--template-docs-type");
            var docsScript = TryGetOptionValue(subArgs, "--docs-script");
            var searchScript = TryGetOptionValue(subArgs, "--search-script");
            var docsHome = TryGetOptionValue(subArgs, "--docs-home") ?? TryGetOptionValue(subArgs, "--docs-home-url");
            var sidebarPosition = TryGetOptionValue(subArgs, "--sidebar") ?? TryGetOptionValue(subArgs, "--sidebar-position");
            var bodyClass = TryGetOptionValue(subArgs, "--body-class") ?? TryGetOptionValue(subArgs, "--bodyClass");
            var sourceRoot = TryGetOptionValue(subArgs, "--source-root");
            var sourceUrl = TryGetOptionValue(subArgs, "--source-url") ?? TryGetOptionValue(subArgs, "--source-pattern");
            var includeUndocumented = !HasOption(subArgs, "--documented-only") && !HasOption(subArgs, "--no-undocumented");
            if (HasOption(subArgs, "--include-undocumented"))
                includeUndocumented = true;
            var navJson = TryGetOptionValue(subArgs, "--nav") ?? TryGetOptionValue(subArgs, "--nav-json");
            var includeNamespaces = ReadOptionList(subArgs, "--include-namespace", "--namespace-prefix");
            var excludeNamespaces = ReadOptionList(subArgs, "--exclude-namespace");
            var includeTypes = ReadOptionList(subArgs, "--include-type");
            var excludeTypes = ReadOptionList(subArgs, "--exclude-type");

            var apiType = ApiDocsType.CSharp;
            if (!string.IsNullOrWhiteSpace(typeText) &&
                Enum.TryParse<ApiDocsType>(typeText, true, out var parsedType))
                apiType = parsedType;

            if (apiType == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(xmlPath))
                return Fail("Missing required --xml (CSharp API docs).", outputJson, logger, "web.apidocs");
            if (apiType == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(helpPath))
                return Fail("Missing required --help-path (PowerShell API docs).", outputJson, logger, "web.apidocs");
            if (string.IsNullOrWhiteSpace(outPath))
                return Fail("Missing required --out.", outputJson, logger, "web.apidocs");

            var options = new WebApiDocsOptions
            {
                Type = apiType,
                XmlPath = xmlPath ?? string.Empty,
                HelpPath = helpPath,
                AssemblyPath = assemblyPath,
                OutputPath = outPath,
                Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
                BaseUrl = baseUrl,
                Format = format,
                CssHref = cssHref,
                HeaderHtmlPath = headerHtml,
                FooterHtmlPath = footerHtml,
                Template = template,
                TemplateRootPath = templateRoot,
                IndexTemplatePath = indexTemplate,
                TypeTemplatePath = typeTemplate,
                DocsIndexTemplatePath = docsIndexTemplate,
                DocsTypeTemplatePath = docsTypeTemplate,
                DocsScriptPath = docsScript,
                SearchScriptPath = searchScript,
                DocsHomeUrl = docsHome,
                SidebarPosition = sidebarPosition,
                BodyClass = bodyClass,
                SourceRootPath = sourceRoot,
                SourceUrlPattern = sourceUrl,
                IncludeUndocumentedTypes = includeUndocumented,
                NavJsonPath = navJson
            };
            if (includeNamespaces.Count > 0)
                options.IncludeNamespacePrefixes.AddRange(includeNamespaces);
            if (excludeNamespaces.Count > 0)
                options.ExcludeNamespacePrefixes.AddRange(excludeNamespaces);
            if (includeTypes.Count > 0)
                options.IncludeTypeNames.AddRange(includeTypes);
            if (excludeTypes.Count > 0)
                options.ExcludeTypeNames.AddRange(excludeTypes);

            var result = WebApiDocsGenerator.Generate(options);

            if (!outputJson && result.Warnings.Length > 0)
            {
                foreach (var warning in result.Warnings)
                    logger.Warn(warning);
            }
            if (!outputJson && result.UsedReflectionFallback)
                logger.Info("API docs used reflection fallback (XML missing or empty).");

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.apidocs",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebApiDocsResult)
                });
                return 0;
            }

            logger.Success($"API docs generated: {result.OutputPath}");
            logger.Info($"Types: {result.TypeCount}");
            logger.Info($"Index: {result.IndexPath}");
            return 0;
        }
        case "changelog":
        {
            var sourceText = TryGetOptionValue(subArgs, "--source");
            var changelogPath = TryGetOptionValue(subArgs, "--changelog") ?? TryGetOptionValue(subArgs, "--changelog-path");
            var outPath = TryGetOptionValue(subArgs, "--out") ??
                          TryGetOptionValue(subArgs, "--out-path") ??
                          TryGetOptionValue(subArgs, "--output-path");
            var repo = TryGetOptionValue(subArgs, "--repo");
            var repoUrl = TryGetOptionValue(subArgs, "--repo-url");
            var token = TryGetOptionValue(subArgs, "--token");
            var maxText = TryGetOptionValue(subArgs, "--max");
            var title = TryGetOptionValue(subArgs, "--title");

            if (string.IsNullOrWhiteSpace(outPath))
                return Fail("Missing required --out.", outputJson, logger, "web.changelog");

            var source = WebChangelogSource.Auto;
            if (!string.IsNullOrWhiteSpace(sourceText) &&
                Enum.TryParse<WebChangelogSource>(sourceText, true, out var parsedSource))
                source = parsedSource;

            var max = ParseIntOption(maxText, 0);
            var options = new WebChangelogOptions
            {
                Source = source,
                ChangelogPath = changelogPath,
                OutputPath = outPath,
                BaseDirectory = Directory.GetCurrentDirectory(),
                Repo = repo,
                RepoUrl = repoUrl,
                Token = token,
                Title = title,
                MaxReleases = max <= 0 ? null : max
            };

            var result = WebChangelogGenerator.Generate(options);

            if (!outputJson && result.Warnings.Length > 0)
            {
                foreach (var warning in result.Warnings)
                    logger.Warn(warning);
            }

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.changelog",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebChangelogResult)
                });
                return 0;
            }

            logger.Success($"Changelog generated: {result.OutputPath}");
            logger.Info($"Releases: {result.ReleaseCount}");
            return 0;
        }
        case "optimize":
        {
            var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                           TryGetOptionValue(subArgs, "--root") ??
                           TryGetOptionValue(subArgs, "--path");
            var configPath = TryGetOptionValue(subArgs, "--config");
            var criticalCss = TryGetOptionValue(subArgs, "--critical-css");
            var cssPattern = TryGetOptionValue(subArgs, "--css-pattern");
            var minifyHtml = subArgs.Any(a => a.Equals("--minify-html", StringComparison.OrdinalIgnoreCase));
            var minifyCss = subArgs.Any(a => a.Equals("--minify-css", StringComparison.OrdinalIgnoreCase));
            var minifyJs = subArgs.Any(a => a.Equals("--minify-js", StringComparison.OrdinalIgnoreCase));
            var optimizeImages = HasOption(subArgs, "--optimize-images") || HasOption(subArgs, "--images");
            var imageExtensions = ReadOptionList(subArgs, "--image-ext", "--image-extensions");
            var imageInclude = ReadOptionList(subArgs, "--image-include");
            var imageExclude = ReadOptionList(subArgs, "--image-exclude");
            var imageQuality = ParseIntOption(TryGetOptionValue(subArgs, "--image-quality"), 82);
            var imageStripMetadata = !HasOption(subArgs, "--image-keep-metadata");
            var imageGenerateWebp = HasOption(subArgs, "--image-generate-webp");
            var imageGenerateAvif = HasOption(subArgs, "--image-generate-avif");
            var imagePreferNextGen = HasOption(subArgs, "--image-prefer-nextgen");
            var imageWidths = ParseIntListOption(TryGetOptionValue(subArgs, "--image-widths"));
            var imageEnhanceTags = HasOption(subArgs, "--image-enhance-tags");
            var imageMaxBytesPerFile = ParseLongOption(TryGetOptionValue(subArgs, "--image-max-bytes"), 0);
            var imageMaxTotalBytes = ParseLongOption(TryGetOptionValue(subArgs, "--image-max-total-bytes"), 0);
            var imageFailOnBudget = HasOption(subArgs, "--image-fail-on-budget");
            var hashAssets = HasOption(subArgs, "--hash-assets");
            var hashExtensions = ReadOptionList(subArgs, "--hash-ext", "--hash-extensions");
            var hashExclude = ReadOptionList(subArgs, "--hash-exclude");
            var hashManifest = TryGetOptionValue(subArgs, "--hash-manifest");
            var reportPath = TryGetOptionValue(subArgs, "--report-path");
            var headersEnabled = HasOption(subArgs, "--headers");
            var headersOut = TryGetOptionValue(subArgs, "--headers-out");
            var headersHtml = TryGetOptionValue(subArgs, "--headers-html");
            var headersAssets = TryGetOptionValue(subArgs, "--headers-assets");

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.optimize");

            AssetPolicySpec? policy = null;
            if (!string.IsNullOrWhiteSpace(configPath))
            {
                var resolved = ResolveExistingFilePath(configPath);
                var (spec, _) = WebSiteSpecLoader.LoadWithPath(resolved, WebCliJson.Options);
                policy = spec.AssetPolicy;
            }

            if (headersEnabled)
            {
                policy ??= new AssetPolicySpec();
                policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
                policy.CacheHeaders.Enabled = true;
                if (!string.IsNullOrWhiteSpace(headersOut))
                    policy.CacheHeaders.OutputPath = headersOut;
                if (!string.IsNullOrWhiteSpace(headersHtml))
                    policy.CacheHeaders.HtmlCacheControl = headersHtml;
                if (!string.IsNullOrWhiteSpace(headersAssets))
                    policy.CacheHeaders.ImmutableCacheControl = headersAssets;
            }

            var optimizeResult = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = siteRoot,
                CriticalCssPath = criticalCss,
                CssLinkPattern = string.IsNullOrWhiteSpace(cssPattern) ? "(app|api-docs)\\.css" : cssPattern,
                MinifyHtml = minifyHtml,
                MinifyCss = minifyCss,
                MinifyJs = minifyJs,
                OptimizeImages = optimizeImages,
                ImageExtensions = imageExtensions.Count > 0 ? imageExtensions.ToArray() : new[] { ".png", ".jpg", ".jpeg", ".webp" },
                ImageInclude = imageInclude.Count > 0 ? imageInclude.ToArray() : Array.Empty<string>(),
                ImageExclude = imageExclude.Count > 0 ? imageExclude.ToArray() : Array.Empty<string>(),
                ImageQuality = imageQuality,
                ImageStripMetadata = imageStripMetadata,
                ImageGenerateWebp = imageGenerateWebp,
                ImageGenerateAvif = imageGenerateAvif,
                ImagePreferNextGen = imagePreferNextGen,
                ResponsiveImageWidths = imageWidths.Length > 0 ? imageWidths : Array.Empty<int>(),
                EnhanceImageTags = imageEnhanceTags,
                ImageMaxBytesPerFile = imageMaxBytesPerFile,
                ImageMaxTotalBytes = imageMaxTotalBytes,
                HashAssets = hashAssets,
                HashExtensions = hashExtensions.Count > 0 ? hashExtensions.ToArray() : new[] { ".css", ".js" },
                HashExclude = hashExclude.Count > 0 ? hashExclude.ToArray() : Array.Empty<string>(),
                HashManifestPath = hashManifest,
                ReportPath = reportPath,
                AssetPolicy = policy
            });
            if (imageFailOnBudget && optimizeResult.ImageBudgetExceeded)
                return Fail($"Image budget exceeded: {string.Join(" | ", optimizeResult.ImageBudgetWarnings)}", outputJson, logger, "web.optimize");

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.optimize",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(optimizeResult, WebCliJson.Context.WebOptimizeResult)
                });
                return 0;
            }

            logger.Success($"Optimize updated files: {optimizeResult.UpdatedCount}");
            logger.Info($"HTML files: {optimizeResult.HtmlFileCount}");
            logger.Info($"Critical CSS inlined: {optimizeResult.CriticalCssInlinedCount}");
            logger.Info($"Minified HTML: {optimizeResult.HtmlMinifiedCount}");
            logger.Info($"HTML bytes saved: {optimizeResult.HtmlBytesSaved}");
            logger.Info($"Minified CSS: {optimizeResult.CssMinifiedCount}");
            logger.Info($"CSS bytes saved: {optimizeResult.CssBytesSaved}");
            logger.Info($"Minified JS: {optimizeResult.JsMinifiedCount}");
            logger.Info($"JS bytes saved: {optimizeResult.JsBytesSaved}");
            logger.Info($"Image files: {optimizeResult.ImageFileCount}");
            logger.Info($"Optimized images: {optimizeResult.ImageOptimizedCount}");
            logger.Info($"Image bytes before: {optimizeResult.ImageBytesBefore}");
            logger.Info($"Image bytes after: {optimizeResult.ImageBytesAfter}");
            logger.Info($"Image bytes saved: {optimizeResult.ImageBytesSaved}");
            logger.Info($"Generated image variants: {optimizeResult.ImageVariantCount}");
            logger.Info($"Image HTML rewrites: {optimizeResult.ImageHtmlRewriteCount}");
            logger.Info($"Image hints added: {optimizeResult.ImageHintedCount}");
            foreach (var entry in optimizeResult.OptimizedImages.Take(5))
                logger.Info($"Image saved: {entry.Path} (-{entry.BytesSaved}B)");
            if (optimizeResult.ImageBudgetExceeded)
            {
                foreach (var warning in optimizeResult.ImageBudgetWarnings)
                    logger.Warn($"Image budget: {warning}");
            }
            if (optimizeResult.HashedAssetCount > 0)
            {
                logger.Info($"Hashed assets: {optimizeResult.HashedAssetCount}");
                logger.Info($"Hashed reference rewrites: HTML {optimizeResult.HtmlHashRewriteCount}, CSS {optimizeResult.CssHashRewriteCount}");
            }
            if (optimizeResult.CacheHeadersWritten)
                logger.Info($"Cache headers: {optimizeResult.CacheHeadersPath}");
            if (!string.IsNullOrWhiteSpace(optimizeResult.ReportPath))
                logger.Info($"Optimize report: {optimizeResult.ReportPath}");
            return 0;
        }
        case "pipeline":
        {
            var pipelinePath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(pipelinePath))
                return Fail("Missing required --config.", outputJson, logger, "web.pipeline");

            var fullPath = ResolveExistingFilePath(pipelinePath);
            var profilePipeline = HasOption(subArgs, "--profile");
            var result = WebPipelineRunner.RunPipeline(fullPath, logger, profilePipeline);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
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
        case "dotnet-build":
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
                    SchemaVersion = OutputSchemaVersion,
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
        case "dotnet-publish":
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
                    SchemaVersion = OutputSchemaVersion,
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
        case "overlay":
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
                    SchemaVersion = OutputSchemaVersion,
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
        case "llms":
        {
            var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                           TryGetOptionValue(subArgs, "--root") ??
                           TryGetOptionValue(subArgs, "--path");
            var projectFile = TryGetOptionValue(subArgs, "--project");
            var apiIndex = TryGetOptionValue(subArgs, "--api-index");
            var apiBase = TryGetOptionValue(subArgs, "--api-base");
            var name = TryGetOptionValue(subArgs, "--name");
            var packageId = TryGetOptionValue(subArgs, "--package") ?? TryGetOptionValue(subArgs, "--package-id");
            var version = TryGetOptionValue(subArgs, "--version");
            var quickstart = TryGetOptionValue(subArgs, "--quickstart");
            var overview = TryGetOptionValue(subArgs, "--overview");
            var license = TryGetOptionValue(subArgs, "--license");
            var targets = TryGetOptionValue(subArgs, "--targets");
            var extra = TryGetOptionValue(subArgs, "--extra");
            var apiLevelText = TryGetOptionValue(subArgs, "--api-level");
            var apiMaxTypesText = TryGetOptionValue(subArgs, "--api-max-types");
            var apiMaxMembersText = TryGetOptionValue(subArgs, "--api-max-members");

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.llms");

            var apiLevel = WebApiDetailLevel.None;
            if (!string.IsNullOrWhiteSpace(apiLevelText) &&
                Enum.TryParse<WebApiDetailLevel>(apiLevelText, true, out var parsedLevel))
                apiLevel = parsedLevel;
            var apiMaxTypes = ParseIntOption(apiMaxTypesText, 200);
            var apiMaxMembers = ParseIntOption(apiMaxMembersText, 2000);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = siteRoot,
                ProjectFile = projectFile,
                ApiIndexPath = apiIndex,
                ApiBase = apiBase ?? "/api",
                Name = name,
                PackageId = packageId,
                Version = version,
                QuickstartPath = quickstart,
                Overview = overview,
                License = license,
                Targets = targets,
                ExtraContentPath = extra,
                ApiDetailLevel = apiLevel,
                ApiMaxTypes = apiMaxTypes,
                ApiMaxMembers = apiMaxMembers
            });

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.llms",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebLlmsResult)
                });
                return 0;
            }

            logger.Success("LLMS files generated.");
            logger.Info($"llms.txt: {result.LlmsTxtPath}");
            logger.Info($"llms.json: {result.LlmsJsonPath}");
            logger.Info($"llms-full.txt: {result.LlmsFullPath}");
            return 0;
        }
        case "sitemap":
        {
            var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                           TryGetOptionValue(subArgs, "--root") ??
                           TryGetOptionValue(subArgs, "--path");
            var baseUrl = TryGetOptionValue(subArgs, "--base-url");
            var outputPath = TryGetOptionValue(subArgs, "--out") ??
                             TryGetOptionValue(subArgs, "--out-path") ??
                             TryGetOptionValue(subArgs, "--output-path");
            var apiSitemap = TryGetOptionValue(subArgs, "--api-sitemap");
            var entriesPath = TryGetOptionValue(subArgs, "--entries");
            var htmlOutput = TryGetOptionValue(subArgs, "--html-out") ??
                             TryGetOptionValue(subArgs, "--html-output") ??
                             TryGetOptionValue(subArgs, "--html-path");
            var htmlTemplate = TryGetOptionValue(subArgs, "--html-template");
            var htmlCss = TryGetOptionValue(subArgs, "--html-css");
            var htmlTitle = TryGetOptionValue(subArgs, "--html-title");
            var generateHtml = HasOption(subArgs, "--html") ||
                               !string.IsNullOrWhiteSpace(htmlOutput) ||
                               !string.IsNullOrWhiteSpace(htmlTemplate) ||
                               !string.IsNullOrWhiteSpace(htmlCss) ||
                               !string.IsNullOrWhiteSpace(htmlTitle);

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.sitemap");
            if (string.IsNullOrWhiteSpace(baseUrl))
                return Fail("Missing required --base-url.", outputJson, logger, "web.sitemap");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = siteRoot,
                BaseUrl = baseUrl,
                OutputPath = outputPath,
                ApiSitemapPath = apiSitemap,
                Entries = LoadSitemapEntries(entriesPath),
                GenerateHtml = generateHtml,
                HtmlOutputPath = htmlOutput,
                HtmlTemplatePath = htmlTemplate,
                HtmlCssHref = htmlCss,
                HtmlTitle = htmlTitle
            });

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.sitemap",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(result, WebCliJson.Context.WebSitemapResult)
                });
                return 0;
            }

            logger.Success($"Sitemap generated: {result.OutputPath}");
            logger.Info($"URL count: {result.UrlCount}");
            if (!string.IsNullOrWhiteSpace(result.HtmlOutputPath))
                logger.Info($"HTML sitemap: {result.HtmlOutputPath}");
            return 0;
        }
        default:
            PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    if (outputJson)
    {
        WebCliJsonWriter.Write(new WebCliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = "web",
            Success = false,
            ExitCode = 1,
            Error = ex.Message
        });
        return 1;
    }

    logger.Error(ex.Message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("PowerForge.Web CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  powerforge-web plan --config <site.json> [--output json]");
    Console.WriteLine("  powerforge-web build --config <site.json> --out <path> [--clean] [--output json]");
    Console.WriteLine("  powerforge-web publish --config <publish.json> [--output json]");
    Console.WriteLine("  powerforge-web verify --config <site.json> [--output json]");
    Console.WriteLine("  powerforge-web doctor --config <site.json> [--out <path>] [--site-root <dir>] [--no-build] [--no-verify] [--no-audit]");
    Console.WriteLine("                     [--include <glob>] [--exclude <glob>] [--summary] [--summary-path <file>] [--sarif] [--sarif-path <file>]");
    Console.WriteLine("                     [--required-route <path[,path]>] [--nav-required-link <path[,path]>] [--output json]");
    Console.WriteLine("  powerforge-web markdown-fix --path <dir> [--include <glob>] [--exclude <glob>] [--apply] [--output json]");
    Console.WriteLine("  powerforge-web markdown-fix --config <site.json> [--path <dir>] [--include <glob>] [--exclude <glob>] [--apply] [--output json]");
    Console.WriteLine("  powerforge-web audit --site-root <dir> [--include <glob>] [--exclude <glob>] [--nav-selector <css>]");
    Console.WriteLine("  powerforge-web audit --config <site.json> [--out <path>] [--include <glob>] [--exclude <glob>] [--nav-selector <css>]");
    Console.WriteLine("                     [--no-links] [--no-assets] [--no-nav] [--no-titles] [--no-ids] [--no-structure]");
    Console.WriteLine("                     [--no-heading-order] [--no-link-purpose]");
    Console.WriteLine("                     [--rendered] [--rendered-engine <chromium|firefox|webkit>] [--rendered-max <n>] [--rendered-timeout <ms>]");
    Console.WriteLine("                     [--rendered-headful] [--rendered-base-url <url>] [--rendered-host <host>] [--rendered-port <n>] [--rendered-no-serve]");
    Console.WriteLine("                     [--rendered-no-install]");
    Console.WriteLine("                     [--rendered-no-console-errors] [--rendered-no-console-warnings] [--rendered-no-failures]");
    Console.WriteLine("                     [--rendered-include <glob>] [--rendered-exclude <glob>]");
    Console.WriteLine("                     [--ignore-nav <glob>] [--no-default-ignore-nav] [--nav-ignore-prefix <path>]");
    Console.WriteLine("                     [--nav-profiles <file.json>]");
    Console.WriteLine("                     [--nav-canonical <file>] [--nav-canonical-selector <css>] [--nav-canonical-required]");
    Console.WriteLine("                     [--nav-required-link <path[,path]>]");
    Console.WriteLine("                     [--min-nav-coverage <0-100>] [--required-route <path[,path]>]");
    Console.WriteLine("                     [--nav-optional]");
    Console.WriteLine("                     [--baseline <file>] [--fail-on-warnings] [--fail-on-new] [--max-errors <n>] [--max-warnings <n>] [--fail-category <name[,name]>]");
    Console.WriteLine("                     [--baseline-generate] [--baseline-update]");
    Console.WriteLine("                     [--no-utf8] [--no-meta-charset] [--no-replacement-char-check]");
    Console.WriteLine("                     [--no-network-hints] [--no-render-blocking] [--max-head-blocking <n>]");
    Console.WriteLine("                     [--no-default-exclude]");
    Console.WriteLine("                     [--summary] [--summary-path <file>] [--summary-max <n>]");
    Console.WriteLine("                     [--sarif] [--sarif-path <file>]");
    Console.WriteLine("  powerforge-web scaffold --out <path> [--name <SiteName>] [--base-url <url>] [--engine simple|scriban] [--output json]");
    Console.WriteLine("  powerforge-web new --config <site.json> --title <Title> [--collection <name>] [--slug <slug>] [--out <path>]");
    Console.WriteLine("  powerforge-web serve --path <dir> [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web serve --config <site.json> [--out <path>] [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web apidocs --type csharp --xml <file> --out <dir> [--assembly <file>] [--title <text>] [--base-url <url>] [--docs-home <url>] [--sidebar <left|right>] [--body-class <class>]");
    Console.WriteLine("  powerforge-web apidocs --type powershell --help-path <file|dir> --out <dir> [--title <text>] [--base-url <url>] [--docs-home <url>] [--sidebar <left|right>] [--body-class <class>]");
    Console.WriteLine("                     [--template <name>] [--template-root <dir>] [--template-index <file>] [--template-type <file>]");
    Console.WriteLine("                     [--template-docs-index <file>] [--template-docs-type <file>] [--docs-script <file>] [--search-script <file>]");
    Console.WriteLine("                     [--format json|hybrid] [--css <href>] [--header-html <file>] [--footer-html <file>]");
    Console.WriteLine("                     [--source-root <dir>] [--source-url <pattern>] [--documented-only]");
    Console.WriteLine("                     [--nav <file>] [--include-namespace <prefix[,prefix]>] [--exclude-namespace <prefix[,prefix]>]");
    Console.WriteLine("  powerforge-web changelog --out <file> [--source auto|file|github] [--changelog <file>] [--repo <owner/name>]");
    Console.WriteLine("                     [--repo-url <url>] [--token <token>] [--max <n>] [--title <text>]");
    Console.WriteLine("  powerforge-web optimize --site-root <dir> [--config <site.json>] [--critical-css <file>] [--css-pattern <regex>]");
    Console.WriteLine("                     [--minify-html] [--minify-css] [--minify-js]");
    Console.WriteLine("                     [--optimize-images] [--image-ext <.png,.jpg,.jpeg,.webp>] [--image-include <glob[,glob]>] [--image-exclude <glob[,glob]>]");
    Console.WriteLine("                     [--image-quality <1-100>] [--image-keep-metadata] [--image-generate-webp] [--image-generate-avif]");
    Console.WriteLine("                     [--image-prefer-nextgen] [--image-widths <320,640,1024>] [--image-enhance-tags]");
    Console.WriteLine("                     [--image-max-bytes <n>] [--image-max-total-bytes <n>] [--image-fail-on-budget]");
    Console.WriteLine("                     [--hash-assets] [--hash-ext <.css,.js>] [--hash-exclude <glob[,glob]>] [--hash-manifest <file>]");
    Console.WriteLine("                     [--headers] [--headers-out <file>] [--headers-html <value>] [--headers-assets <value>] [--report-path <file>]");
    Console.WriteLine("  powerforge-web dotnet-build --project <path> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--no-restore]");
    Console.WriteLine("  powerforge-web dotnet-publish --project <path> --out <dir> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--define-constants <list>]");
    Console.WriteLine("                     [--clean]");
    Console.WriteLine("                     [--self-contained] [--no-build] [--no-restore] [--base-href <path>] [--no-blazor-fixes]");
    Console.WriteLine("  powerforge-web overlay --source <dir> --destination <dir> [--include <glob[,glob...]>] [--exclude <glob[,glob...]>]");
    Console.WriteLine("  powerforge-web pipeline --config <pipeline.json> [--profile]");
    Console.WriteLine("  powerforge-web llms --site-root <dir> [--project <path>] [--api-index <path>] [--api-base /api]");
    Console.WriteLine("                     [--name <Name>] [--package <Id>] [--version <X.Y.Z>] [--quickstart <file>]");
    Console.WriteLine("                     [--overview <text>] [--license <text>] [--targets <text>] [--extra <file>]");
    Console.WriteLine("                     [--api-level none|summary|full] [--api-max-types <n>] [--api-max-members <n>]");
    Console.WriteLine("  powerforge-web sitemap --site-root <dir> --base-url <url> [--api-sitemap <path>] [--out <file>] [--entries <file>]");
    Console.WriteLine("                     [--html] [--html-out <file>] [--html-template <file>] [--html-css <href>] [--html-title <text>]");
}

static int Fail(string message, bool outputJson, WebConsoleLogger logger, string command)
{
    if (outputJson)
    {
        WebCliJsonWriter.Write(new WebCliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = command,
            Success = false,
            ExitCode = 2,
            Error = message
        });
        return 2;
    }

    logger.Error(message);
    PrintUsage();
    return 2;
}

static string? TryGetOptionValue(string[] argv, string optionName)
{
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
        return ++i < argv.Length ? argv[i] : null;
    }
    return null;
}

static List<string> ReadOptionList(string[] argv, params string[] optionNames)
{
    var values = new List<string>();
    foreach (var optionName in optionNames)
    {
        values.AddRange(GetOptionValues(argv, optionName));
    }

    var results = new List<string>();
    foreach (var value in values)
    {
        if (string.IsNullOrWhiteSpace(value)) continue;
        var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                results.Add(trimmed);
        }
    }

    return results;
}

static List<string> GetOptionValues(string[] argv, string optionName)
{
    var values = new List<string>();
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
        if (++i < argv.Length && !string.IsNullOrWhiteSpace(argv[i]))
            values.Add(argv[i]);
    }
    return values;
}

static bool HasOption(string[] argv, string optionName)
{
    for (var i = 0; i < argv.Length; i++)
    {
        if (argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

static int ParseIntOption(string? value, int fallback)
{
    if (string.IsNullOrWhiteSpace(value)) return fallback;
    return int.TryParse(value, out var parsed) ? parsed : fallback;
}

static long ParseLongOption(string? value, long fallback)
{
    if (string.IsNullOrWhiteSpace(value)) return fallback;
    return long.TryParse(value, out var parsed) ? parsed : fallback;
}

static int[] ParseIntListOption(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return Array.Empty<int>();

    var values = new List<int>();
    foreach (var token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
    {
        if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
            values.Add(parsed);
    }

    return values
        .Distinct()
        .OrderBy(v => v)
        .ToArray();
}

static string? ResolveSummaryPath(bool summaryEnabled, string? summaryPath)
{
    if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
        return null;

    return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
}

static string? ResolveSarifPath(bool sarifEnabled, string? sarifPath)
{
    if (!sarifEnabled && string.IsNullOrWhiteSpace(sarifPath))
        return null;

    return string.IsNullOrWhiteSpace(sarifPath) ? "audit.sarif.json" : sarifPath;
}

static WebAuditNavProfile[] LoadAuditNavProfiles(string? navProfilesPath)
{
    if (string.IsNullOrWhiteSpace(navProfilesPath))
        return Array.Empty<WebAuditNavProfile>();

    var fullPath = ResolveExistingFilePath(navProfilesPath);
    using var stream = File.OpenRead(fullPath);
    var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditNavProfileArray)
                   ?? Array.Empty<WebAuditNavProfile>();
    return profiles
        .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
        .ToArray();
}

static WebAuditResult RunDoctorAudit(string siteRoot, string[] argv)
{
    var include = ReadOptionList(argv, "--include");
    var exclude = ReadOptionList(argv, "--exclude");
    var ignoreNav = ReadOptionList(argv, "--ignore-nav", "--ignore-nav-path");
    var navIgnorePrefixes = ReadOptionList(argv, "--nav-ignore-prefix", "--nav-ignore-prefixes");
    var navRequiredLinks = ReadOptionList(argv, "--nav-required-link", "--nav-required-links");
    var navProfilesPath = TryGetOptionValue(argv, "--nav-profiles");
    var requiredRoutes = ReadOptionList(argv, "--required-route", "--required-routes");
    var minNavCoverageText = TryGetOptionValue(argv, "--min-nav-coverage");
    var navSelector = TryGetOptionValue(argv, "--nav-selector") ?? "nav";
    var navRequired = !HasOption(argv, "--nav-optional");
    var useDefaultIgnoreNav = !HasOption(argv, "--no-default-ignore-nav");
    var useDefaultExclude = !HasOption(argv, "--no-default-exclude");
    var summaryEnabled = HasOption(argv, "--summary");
    var summaryPath = TryGetOptionValue(argv, "--summary-path");
    var summaryMaxText = TryGetOptionValue(argv, "--summary-max");
    var sarifEnabled = HasOption(argv, "--sarif");
    var sarifPath = TryGetOptionValue(argv, "--sarif-path");
    var navCanonical = TryGetOptionValue(argv, "--nav-canonical");
    var navCanonicalSelector = TryGetOptionValue(argv, "--nav-canonical-selector");
    var navCanonicalRequired = HasOption(argv, "--nav-canonical-required");
    var checkUtf8 = !HasOption(argv, "--no-utf8");
    var checkMetaCharset = !HasOption(argv, "--no-meta-charset");
    var checkReplacementChars = !HasOption(argv, "--no-replacement-char-check");
    var checkHeadingOrder = !HasOption(argv, "--no-heading-order");
    var checkLinkPurpose = !HasOption(argv, "--no-link-purpose");
    var checkNetworkHints = !HasOption(argv, "--no-network-hints");
    var checkRenderBlocking = !HasOption(argv, "--no-render-blocking");
    var maxHeadBlockingText = TryGetOptionValue(argv, "--max-head-blocking");

    if (requiredRoutes.Count == 0)
        requiredRoutes.Add("/404.html");
    if (navRequiredLinks.Count == 0)
        navRequiredLinks.Add("/");

    var ignoreNavPatterns = BuildIgnoreNavPatterns(ignoreNav, useDefaultIgnoreNav);
    var summaryMax = ParseIntOption(summaryMaxText, 10);
    var minNavCoveragePercent = ParseIntOption(minNavCoverageText, 0);
    var maxHeadBlockingResources = ParseIntOption(maxHeadBlockingText, new WebAuditOptions().MaxHeadBlockingResources);
    var resolvedSummaryPath = ResolveSummaryPath(summaryEnabled, summaryPath);
    var resolvedSarifPath = ResolveSarifPath(sarifEnabled, sarifPath);
    var navProfiles = LoadAuditNavProfiles(navProfilesPath);

    return WebSiteAuditor.Audit(new WebAuditOptions
    {
        SiteRoot = siteRoot,
        Include = include.ToArray(),
        Exclude = exclude.ToArray(),
        UseDefaultExcludes = useDefaultExclude,
        IgnoreNavFor = ignoreNavPatterns,
        NavSelector = navSelector,
        NavRequired = navRequired,
        NavIgnorePrefixes = navIgnorePrefixes.ToArray(),
        NavRequiredLinks = navRequiredLinks.ToArray(),
        NavProfiles = navProfiles,
        MinNavCoveragePercent = minNavCoveragePercent,
        RequiredRoutes = requiredRoutes.ToArray(),
        CheckLinks = !HasOption(argv, "--no-links"),
        CheckAssets = !HasOption(argv, "--no-assets"),
        CheckNavConsistency = !HasOption(argv, "--no-nav"),
        CheckTitles = !(HasOption(argv, "--no-titles") || HasOption(argv, "--no-title")),
        CheckDuplicateIds = !HasOption(argv, "--no-ids"),
        CheckHtmlStructure = !HasOption(argv, "--no-structure"),
        SummaryPath = resolvedSummaryPath,
        SarifPath = resolvedSarifPath,
        SummaryMaxIssues = summaryMax,
        NavCanonicalPath = navCanonical,
        NavCanonicalSelector = navCanonicalSelector,
        NavCanonicalRequired = navCanonicalRequired,
        CheckUtf8 = checkUtf8,
        CheckMetaCharset = checkMetaCharset,
        CheckUnicodeReplacementChars = checkReplacementChars,
        CheckHeadingOrder = checkHeadingOrder,
        CheckLinkPurposeConsistency = checkLinkPurpose,
        CheckNetworkHints = checkNetworkHints,
        CheckRenderBlockingResources = checkRenderBlocking,
        MaxHeadBlockingResources = maxHeadBlockingResources
    });
}

static string[] BuildDoctorRecommendations(WebVerifyResult? verify, WebAuditResult? audit)
{
    var recommendations = new List<string>();

    static bool ContainsText(IEnumerable<string> source, string text) =>
        source.Any(line => line.Contains(text, StringComparison.OrdinalIgnoreCase));

    static bool ContainsCategory(WebAuditResult result, string category) =>
        result.Issues.Any(issue => issue.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

    if (verify is not null)
    {
        if (verify.Errors.Length > 0)
            recommendations.Add("Fix `verify` errors first; they indicate broken site configuration or portability contracts.");
        if (ContainsText(verify.Warnings, "Theme contract:"))
            recommendations.Add("Resolve theme contract warnings (schemaVersion, engine, manifest path, and portable asset paths) to keep themes reusable across repos.");
        if (ContainsText(verify.Warnings, "schemaVersion") || ContainsText(verify.Warnings, "contractVersion"))
            recommendations.Add("Standardize all themes on `schemaVersion: 2` and keep only one version field in theme manifests.");
        if (ContainsText(verify.Warnings, "theme manifest"))
            recommendations.Add("Standardize themes on `theme.manifest.json` contract v2 (including `scriptsPath`) for portable reusable themes.");
        if (ContainsText(verify.Warnings, "Navigation lint:"))
            recommendations.Add("Fix navigation lint findings (duplicate IDs, unknown menu references, and stale profile/path filters) before publishing.");
        if (ContainsText(verify.Warnings, "does not match any generated route"))
            recommendations.Add("Align navigation links and visibility/path patterns with generated routes to prevent dead menu entries.");
        if (ContainsText(verify.Warnings, "Markdown hygiene"))
            recommendations.Add("Convert raw HTML-heavy docs to native Markdown to reduce styling drift and simplify maintenance.");
        if (ContainsText(verify.Warnings, "portable relative path"))
            recommendations.Add("Replace rooted/OS-specific paths in theme mappings with portable relative paths.");
    }

    if (audit is not null)
    {
        if (audit.BrokenLinkCount > 0)
            recommendations.Add("Fix broken internal links before publish (`audit` link errors).");
        if (audit.MissingAssetCount > 0)
            recommendations.Add("Fix missing CSS/JS/image assets to avoid runtime regressions.");
        if (audit.MissingRequiredRouteCount > 0)
            recommendations.Add("Ensure required routes like `/404.html` are generated and published.");
        if (audit.NavMismatchCount > 0 || ContainsCategory(audit, "nav"))
            recommendations.Add("Unify navigation templates/components so all page families (docs/api/404) share a consistent nav contract.");
        if (ContainsCategory(audit, "network-hint"))
            recommendations.Add("Add `preconnect`/`dns-prefetch` hints for external origins (for example Google Fonts) to reduce critical path latency.");
        if (ContainsCategory(audit, "render-blocking"))
            recommendations.Add("Reduce render-blocking head resources: defer non-critical scripts and consolidate CSS.");
        if (ContainsCategory(audit, "heading-order"))
            recommendations.Add("Fix heading hierarchy so content does not skip levels (for example h2 -> h4) to improve accessibility.");
        if (ContainsCategory(audit, "link-purpose"))
            recommendations.Add("Use destination-specific link labels (avoid repeated generic labels like 'Learn more').");
        if (ContainsCategory(audit, "utf8"))
            recommendations.Add("Enforce UTF-8 output and meta charset declarations to avoid encoding regressions.");
        if (ContainsCategory(audit, "duplicate-id"))
            recommendations.Add("Remove duplicate HTML IDs to improve accessibility and scripting reliability.");
    }

    if (recommendations.Count == 0)
        recommendations.Add("No major engine findings detected by doctor. Keep running verify+audit in CI.");

    return recommendations
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string[] BuildIgnoreNavPatterns(List<string> userPatterns, bool useDefaults)
{
    if (!useDefaults)
        return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

    var defaults = new WebAuditOptions().IgnoreNavFor;
    if (userPatterns.Count == 0)
        return defaults;

    return defaults.Concat(userPatterns)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string ResolveExistingFilePath(string path)
{
    var full = Path.GetFullPath(path.Trim().Trim('"'));
    if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");
    return full;
}

static string ResolvePathRelative(string baseDir, string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return string.Empty;
    if (Path.IsPathRooted(value))
        return Path.GetFullPath(value);
    return Path.GetFullPath(Path.Combine(baseDir, value));
}


static string ApplyArchetypeTemplate(string template, string title, string slug, string collection)
{
    var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
    return template
        .Replace("{{title}}", title, StringComparison.OrdinalIgnoreCase)
        .Replace("{{slug}}", slug, StringComparison.OrdinalIgnoreCase)
        .Replace("{{date}}", date, StringComparison.OrdinalIgnoreCase)
        .Replace("{{collection}}", collection, StringComparison.OrdinalIgnoreCase);
}

static string Slugify(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;
    var lower = input.Trim().ToLowerInvariant();
    var sb = new System.Text.StringBuilder();
    foreach (var ch in lower)
    {
        if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_') sb.Append('-');
    }
    var slug = sb.ToString();
    while (slug.Contains("--")) slug = slug.Replace("--", "-");
    return slug.Trim('-');
}


static WebSitemapEntry[] LoadSitemapEntries(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return Array.Empty<WebSitemapEntry>();
    var full = ResolveExistingFilePath(path);
    var json = File.ReadAllText(full);
    return JsonSerializer.Deserialize<WebSitemapEntry[]>(json, WebCliJson.Options) ?? Array.Empty<WebSitemapEntry>();
}

static bool IsJsonOutput(string[] argv)
{
    foreach (var a in argv)
    {
        if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            return true;
    }

    var output = TryGetOptionValue(argv, "--output");
    return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
}

static void EnsureUtf8ConsoleEncoding()
{
    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    try
    {
        if (Console.OutputEncoding.CodePage != Encoding.UTF8.CodePage)
            Console.OutputEncoding = utf8NoBom;
    }
    catch
    {
        // Best effort only.
    }

    try
    {
        if (Console.InputEncoding.CodePage != Encoding.UTF8.CodePage)
            Console.InputEncoding = utf8NoBom;
    }
    catch
    {
        // Best effort only.
    }
}

internal sealed class WebConsoleLogger
{
    private readonly bool _useUnicodePrefixes = ShouldUseUnicodePrefixes();

    public void Info(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "ℹ " : "[INFO]")} {message}");
    public void Success(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "✅" : "[OK]")} {message}");
    public void Warn(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "⚠️" : "[WARN]")} {message}");
    public void Error(string message) => Console.WriteLine($"{(_useUnicodePrefixes ? "❌" : "[ERROR]")} {message}");

    private static bool ShouldUseUnicodePrefixes()
    {
        var forceAscii = Environment.GetEnvironmentVariable("POWERFORGE_WEB_ASCII_LOGS");
        if (string.Equals(forceAscii, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(forceAscii, "true", StringComparison.OrdinalIgnoreCase))
            return false;

        var codePage = Console.OutputEncoding.CodePage;
        return codePage == Encoding.UTF8.CodePage ||
               codePage == Encoding.Unicode.CodePage ||
               codePage == Encoding.BigEndianUnicode.CodePage;
    }
}

internal static class WebCliFileSystem
{
    internal static void CleanOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(normalizedRoot) &&
            string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clean root path: {fullPath}");

        if (!Directory.Exists(fullPath))
            return;

        foreach (var dir in Directory.GetDirectories(fullPath))
            Directory.Delete(dir, true);
        foreach (var file in Directory.GetFiles(fullPath))
            File.Delete(file);
    }
}

internal static class CliPatternHelper
{
    internal static string[] SplitPatterns(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal static class WebAuditBaselineStore
{
    private const long MaxBaselineFileSizeBytes = 10 * 1024 * 1024;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static string Write(
        string siteRoot,
        string? baselinePath,
        WebAuditResult result,
        bool mergeWithExisting,
        WebConsoleLogger? logger)
    {
        var resolvedPath = ResolveBaselinePath(siteRoot, baselinePath);
        try
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (mergeWithExisting)
            {
                foreach (var key in LoadIssueKeys(resolvedPath))
                    keys.Add(key);
            }

            foreach (var issue in result.Issues)
            {
                if (!string.IsNullOrWhiteSpace(issue.Key))
                    keys.Add(issue.Key);
            }

            var issues = result.Issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.Key) && keys.Contains(issue.Key))
                .GroupBy(issue => issue.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(issue => issue.Key, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var payload = new
            {
                version = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                issueCount = keys.Count,
                issueKeys = keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray(),
                issues
            };

            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(resolvedPath, json);
            return resolvedPath;
        }
        catch (Exception ex)
        {
            logger?.Warn($"Audit baseline write failed: {ex.Message}");
            return resolvedPath;
        }
    }

    internal static string ResolveBaselinePath(string siteRoot, string? baselinePath)
    {
        var candidate = string.IsNullOrWhiteSpace(baselinePath) ? "audit-baseline.json" : baselinePath.Trim();
        var normalizedRoot = NormalizeDirectoryPath(siteRoot);
        var resolvedPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(normalizedRoot, candidate));
        if (!IsWithinRoot(normalizedRoot, resolvedPath))
            throw new InvalidOperationException($"Baseline path must resolve under site root: {candidate}");
        return resolvedPath;
    }

    private static IEnumerable<string> LoadIssueKeys(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<string>();

        var info = new FileInfo(path);
        if (info.Length > MaxBaselineFileSizeBytes)
            return Array.Empty<string>();

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (TryGetPropertyIgnoreCase(root, "issueKeys", out var issueKeys) && issueKeys.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in issueKeys.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            if (TryGetPropertyIgnoreCase(root, "issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                {
                    if (issue.ValueKind != JsonValueKind.Object) continue;
                    if (!TryGetPropertyIgnoreCase(issue, "key", out var keyElement) || keyElement.ValueKind != JsonValueKind.String) continue;
                    var value = keyElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }

        return keys.ToArray();
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(rootPath, PathComparison);
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out value))
                return true;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}

internal static class WebPipelineRunner
{
    private const long MaxStateFileSizeBytes = 10 * 1024 * 1024;
    private const int MaxStampFileCount = 1000;
    private static readonly StringComparison FileSystemPathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly HashSet<string> FingerprintPathKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "config", "siteRoot", "site-root", "project", "solution", "path",
        "out", "output", "source", "destination", "dest",
        "xml", "help", "helpPath", "assembly",
        "changelog", "changelogPath",
        "apiIndex", "apiSitemap", "criticalCss", "hashManifest", "reportPath", "report-path",
        "summaryPath", "sarifPath", "baselinePath", "navCanonicalPath", "navProfiles",
        "summary-path", "sarif-path", "baseline-path", "nav-canonical-path", "nav-profiles",
        "templateRoot", "templateIndex", "templateType",
        "templateDocsIndex", "templateDocsType",
        "docsScript", "searchScript",
        "headerHtml", "footerHtml", "quickstart", "extra",
        "htmlOutput", "htmlTemplate", "cachePath", "profilePath"
    };

    private sealed class WebPipelineCacheState
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, WebPipelineCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WebPipelineCacheEntry
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string? Message { get; set; }
    }

    private sealed class PipelineStepDefinition
    {
        public int Index { get; set; }
        public string Task { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string[] DependsOn { get; set; } = Array.Empty<string>();
        public int[] DependencyIndexes { get; set; } = Array.Empty<int>();
        public JsonElement Element { get; set; }
    }

    internal static WebPipelineResult RunPipeline(string pipelinePath, WebConsoleLogger? logger, bool forceProfile = false)
    {
        var json = File.ReadAllText(pipelinePath);
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;
        if (!root.TryGetProperty("steps", out var stepsElement) || stepsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Pipeline config must include a steps array.");

        var baseDir = Path.GetDirectoryName(pipelinePath) ?? ".";
        var profileEnabled = (GetBool(root, "profile") ?? false) || forceProfile;
        var profilePath = profileEnabled
            ? ResolvePathWithinRoot(baseDir, GetString(root, "profilePath") ?? GetString(root, "profile-path"), Path.Combine(".powerforge", "pipeline-profile.json"))
            : null;
        var cacheEnabled = GetBool(root, "cache") ?? false;
        var cachePath = ResolvePathWithinRoot(baseDir, GetString(root, "cachePath") ?? GetString(root, "cache-path"), Path.Combine(".powerforge", "pipeline-cache.json"));
        var cacheState = cacheEnabled ? LoadPipelineCache(cachePath, logger) : null;
        var cacheUpdated = false;
        var runStopwatch = Stopwatch.StartNew();

        var result = new WebPipelineResult
        {
            CachePath = cacheEnabled ? cachePath : null
        };
        var steps = BuildStepDefinitions(stepsElement);
        var totalSteps = steps.Count;
        var stepResultsByIndex = new Dictionary<int, WebPipelineStepResult>();

        foreach (var definition in steps)
        {
            var step = definition.Element;
            var task = definition.Task;
            var stepIndex = definition.Index;
            var label = $"[{stepIndex}/{totalSteps}] {task}";
            logger?.Info($"Starting {label}...");
            var stopwatch = Stopwatch.StartNew();
            var stepResult = new WebPipelineStepResult { Task = task };
            var cacheKey = $"{stepIndex}:{task}";
            var stepFingerprint = string.Empty;
            var expectedOutputs = GetExpectedStepOutputs(task, step, baseDir);
            if (definition.DependencyIndexes.Length > 0)
            {
                foreach (var dependencyIndex in definition.DependencyIndexes)
                {
                    if (!stepResultsByIndex.TryGetValue(dependencyIndex, out var dependencyResult) || !dependencyResult.Success)
                    {
                        throw new InvalidOperationException($"Step '{definition.Id}' dependency #{dependencyIndex} failed or was not executed.");
                    }
                }
            }

            var dependencyMiss = definition.DependencyIndexes.Any(index =>
                !stepResultsByIndex.TryGetValue(index, out var dependencyResult) || !dependencyResult.Cached);
            if (cacheEnabled && cacheState is not null)
            {
                stepFingerprint = ComputeStepFingerprint(baseDir, step);
                if (cacheState.Entries.TryGetValue(cacheKey, out var cacheEntry) &&
                    string.Equals(cacheEntry.Fingerprint, stepFingerprint, StringComparison.Ordinal) &&
                    !dependencyMiss &&
                    AreExpectedOutputsPresent(expectedOutputs))
                {
                    stepResult.Success = true;
                    stepResult.Cached = true;
                    stepResult.Message = AppendDuration(cacheEntry.Message ?? "cache hit", stopwatch);
                    stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                    result.Steps.Add(stepResult);
                    stepResultsByIndex[stepIndex] = stepResult;
                    if (profileEnabled)
                        logger?.Info($"Finished {label} (cache hit) in {FormatDuration(stopwatch.Elapsed)}");
                    continue;
                }
            }

            try
            {
                switch (task)
                {
                    case "build":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var cleanOutput = GetBool(step, "clean") ?? false;
                        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("build requires config and out.");

                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        if (cleanOutput)
                            WebCliFileSystem.CleanOutputDirectory(outPath);
                        var build = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                        stepResult.Success = true;
                        stepResult.Message = $"Built {build.OutputPath}";
                        break;
                    }
                    case "verify":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        if (string.IsNullOrWhiteSpace(config))
                            throw new InvalidOperationException("verify requires config.");

                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        var verify = WebSiteVerifier.Verify(spec, plan);
                        if (!verify.Success)
                        {
                            var firstError = verify.Errors.Length > 0 ? verify.Errors[0] : "Web verify failed.";
                            throw new InvalidOperationException(firstError);
                        }

                        var warnCount = verify.Warnings.Length;
                        stepResult.Success = true;
                        stepResult.Message = warnCount > 0
                            ? $"Verify {warnCount} warnings"
                            : "Verify ok";
                        break;
                    }
                    case "markdown-fix":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        var rootPath = ResolvePath(baseDir, GetString(step, "root") ?? GetString(step, "path") ?? GetString(step, "siteRoot"));
                        var include = GetString(step, "include");
                        var exclude = GetString(step, "exclude");
                        var applyFixes = GetBool(step, "apply") ?? false;

                        if (string.IsNullOrWhiteSpace(rootPath) && !string.IsNullOrWhiteSpace(config))
                        {
                            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                            var contentRoot = string.IsNullOrWhiteSpace(spec.ContentRoot) ? "content" : spec.ContentRoot;
                            rootPath = Path.IsPathRooted(contentRoot)
                                ? contentRoot
                                : Path.Combine(plan.RootPath, contentRoot);
                        }

                        if (string.IsNullOrWhiteSpace(rootPath))
                            throw new InvalidOperationException("markdown-fix requires root/path/siteRoot or config.");

                        var fix = WebMarkdownHygieneFixer.Fix(new WebMarkdownFixOptions
                        {
                            RootPath = rootPath,
                            Include = CliPatternHelper.SplitPatterns(include),
                            Exclude = CliPatternHelper.SplitPatterns(exclude),
                            ApplyChanges = applyFixes
                        });

                        stepResult.Success = fix.Success;
                        stepResult.Message = applyFixes
                            ? $"Markdown fix updated {fix.ChangedFileCount}/{fix.FileCount} files ({fix.ReplacementCount} replacements)"
                            : $"Markdown fix dry-run {fix.ChangedFileCount}/{fix.FileCount} files ({fix.ReplacementCount} replacements)";
                        break;
                    }
                    case "apidocs":
                    {
                        var typeText = GetString(step, "type");
                        var xml = ResolvePath(baseDir, GetString(step, "xml"));
                        var help = ResolvePath(baseDir, GetString(step, "help") ?? GetString(step, "helpPath") ?? GetString(step, "help-path"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var assembly = ResolvePath(baseDir, GetString(step, "assembly"));
                        var title = GetString(step, "title");
                        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api";
                        var format = GetString(step, "format");
                        var css = GetString(step, "css");
                        var header = ResolvePath(baseDir, GetString(step, "headerHtml") ?? GetString(step, "header-html"));
                        var footer = ResolvePath(baseDir, GetString(step, "footerHtml") ?? GetString(step, "footer-html"));
                        var template = GetString(step, "template");
                        var templateRoot = ResolvePath(baseDir, GetString(step, "templateRoot") ?? GetString(step, "template-root"));
                        var indexTemplate = ResolvePath(baseDir, GetString(step, "templateIndex") ?? GetString(step, "template-index"));
                        var typeTemplate = ResolvePath(baseDir, GetString(step, "templateType") ?? GetString(step, "template-type"));
                        var docsIndexTemplate = ResolvePath(baseDir, GetString(step, "templateDocsIndex") ?? GetString(step, "template-docs-index"));
                        var docsTypeTemplate = ResolvePath(baseDir, GetString(step, "templateDocsType") ?? GetString(step, "template-docs-type"));
                        var docsScript = ResolvePath(baseDir, GetString(step, "docsScript") ?? GetString(step, "docs-script"));
                        var searchScript = ResolvePath(baseDir, GetString(step, "searchScript") ?? GetString(step, "search-script"));
                        var docsHome = GetString(step, "docsHome") ?? GetString(step, "docsHomeUrl") ??
                                       GetString(step, "docs-home") ?? GetString(step, "docs-home-url");
                        var sidebar = GetString(step, "sidebar") ?? GetString(step, "sidebarPosition") ?? GetString(step, "sidebar-position");
                        var bodyClass = GetString(step, "bodyClass") ?? GetString(step, "body-class");
                        var sourceRoot = ResolvePath(baseDir, GetString(step, "sourceRoot") ?? GetString(step, "source-root"));
                        var sourceUrl = GetString(step, "sourceUrl") ?? GetString(step, "source-url") ?? GetString(step, "sourcePattern") ?? GetString(step, "source-pattern");
                        var includeUndocumented = GetBool(step, "includeUndocumented") ?? GetBool(step, "include-undocumented") ?? true;
                        var nav = ResolvePath(baseDir, GetString(step, "nav") ?? GetString(step, "navJson") ?? GetString(step, "nav-json"));
                        var includeNamespaces = GetString(step, "includeNamespace") ?? GetString(step, "include-namespace");
                        var excludeNamespaces = GetString(step, "excludeNamespace") ?? GetString(step, "exclude-namespace");
                        var includeTypes = GetString(step, "includeType") ?? GetString(step, "include-type");
                        var excludeTypes = GetString(step, "excludeType") ?? GetString(step, "exclude-type");
                        var apiType = ApiDocsType.CSharp;
                        if (!string.IsNullOrWhiteSpace(typeText) &&
                            Enum.TryParse<ApiDocsType>(typeText, true, out var parsedType))
                            apiType = parsedType;
                        if (string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("apidocs requires out.");
                        if (apiType == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(xml))
                            throw new InvalidOperationException("apidocs requires xml for CSharp.");
                        if (apiType == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(help))
                            throw new InvalidOperationException("apidocs requires help for PowerShell.");

                        var options = new WebApiDocsOptions
                        {
                            Type = apiType,
                            XmlPath = xml ?? string.Empty,
                            HelpPath = help,
                            AssemblyPath = assembly,
                            OutputPath = outPath,
                            Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
                            BaseUrl = baseUrl,
                            Format = format,
                            CssHref = css,
                            HeaderHtmlPath = header,
                            FooterHtmlPath = footer,
                            Template = template,
                            TemplateRootPath = templateRoot,
                            IndexTemplatePath = indexTemplate,
                            TypeTemplatePath = typeTemplate,
                            DocsIndexTemplatePath = docsIndexTemplate,
                            DocsTypeTemplatePath = docsTypeTemplate,
                            DocsScriptPath = docsScript,
                            SearchScriptPath = searchScript,
                            DocsHomeUrl = docsHome,
                            SidebarPosition = sidebar,
                            BodyClass = bodyClass,
                            SourceRootPath = sourceRoot,
                            SourceUrlPattern = sourceUrl,
                            IncludeUndocumentedTypes = includeUndocumented,
                            NavJsonPath = nav
                        };
                        var includeList = CliPatternHelper.SplitPatterns(includeNamespaces);
                        var excludeList = CliPatternHelper.SplitPatterns(excludeNamespaces);
                        var includeTypeList = CliPatternHelper.SplitPatterns(includeTypes);
                        var excludeTypeList = CliPatternHelper.SplitPatterns(excludeTypes);
                        if (includeList.Length > 0)
                            options.IncludeNamespacePrefixes.AddRange(includeList);
                        if (excludeList.Length > 0)
                            options.ExcludeNamespacePrefixes.AddRange(excludeList);
                        if (includeTypeList.Length > 0)
                            options.IncludeTypeNames.AddRange(includeTypeList);
                        if (excludeTypeList.Length > 0)
                            options.ExcludeTypeNames.AddRange(excludeTypeList);

                        var res = WebApiDocsGenerator.Generate(options);
                        var note = res.UsedReflectionFallback ? " (reflection)" : string.Empty;
                        if (res.Warnings.Length > 0)
                        {
                            var firstWarning = res.Warnings[0];
                            if (!string.IsNullOrWhiteSpace(firstWarning))
                            {
                                var trimmed = firstWarning.Length > 120
                                    ? $"{firstWarning.Substring(0, 117)}..."
                                    : firstWarning;
                                note += $" (warn: {trimmed})";
                            }
                            else
                            {
                                note += $" ({res.Warnings.Length} warnings)";
                            }
                        }
                        stepResult.Success = true;
                        stepResult.Message = $"API docs {res.TypeCount} types{note}";
                        break;
                    }
                    case "changelog":
                    {
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var sourceText = GetString(step, "source");
                        var changelog = ResolvePath(baseDir, GetString(step, "changelog") ?? GetString(step, "changelogPath") ?? GetString(step, "changelog-path"));
                        var repo = GetString(step, "repo");
                        var repoUrl = GetString(step, "repoUrl") ?? GetString(step, "repo-url");
                        var token = GetString(step, "token");
                        var maxValue = GetInt(step, "max") ?? 0;
                        var title = GetString(step, "title");
                        if (string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("changelog requires out.");

                        var source = WebChangelogSource.Auto;
                        if (!string.IsNullOrWhiteSpace(sourceText) &&
                            Enum.TryParse<WebChangelogSource>(sourceText, true, out var parsedSource))
                            source = parsedSource;

                        var options = new WebChangelogOptions
                        {
                            Source = source,
                            ChangelogPath = changelog,
                            OutputPath = outPath,
                            Repo = repo,
                            RepoUrl = repoUrl,
                            Token = token,
                            Title = title,
                            MaxReleases = maxValue <= 0 ? null : maxValue
                        };

                        var res = WebChangelogGenerator.Generate(options);
                        var note = res.Source != WebChangelogSource.Auto ? $" ({res.Source.ToString().ToLowerInvariant()})" : string.Empty;
                        if (res.Warnings.Length > 0)
                        {
                            var firstWarning = res.Warnings[0];
                            if (!string.IsNullOrWhiteSpace(firstWarning))
                            {
                                var trimmed = firstWarning.Length > 120
                                    ? $"{firstWarning.Substring(0, 117)}..."
                                    : firstWarning;
                                note += $" (warn: {trimmed})";
                            }
                        }
                        stepResult.Success = true;
                        stepResult.Message = $"Changelog {res.ReleaseCount} releases{note}";
                        break;
                    }
                    case "llms":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("llms requires siteRoot.");

                        var apiLevelText = GetString(step, "apiLevel") ?? GetString(step, "api-level");
                        var res = WebLlmsGenerator.Generate(new WebLlmsOptions
                        {
                            SiteRoot = siteRoot,
                            ProjectFile = ResolvePath(baseDir, GetString(step, "project")),
                            ApiIndexPath = ResolvePath(baseDir, GetString(step, "apiIndex") ?? GetString(step, "api-index")),
                            ApiBase = GetString(step, "apiBase") ?? "/api",
                            Name = GetString(step, "name"),
                            PackageId = GetString(step, "package") ?? GetString(step, "packageId"),
                            Version = GetString(step, "version"),
                            QuickstartPath = ResolvePath(baseDir, GetString(step, "quickstart")),
                            Overview = GetString(step, "overview"),
                            License = GetString(step, "license"),
                            Targets = GetString(step, "targets"),
                            ExtraContentPath = ResolvePath(baseDir, GetString(step, "extra")),
                            ApiDetailLevel = ParseApiDetailLevel(apiLevelText),
                            ApiMaxTypes = GetInt(step, "apiMaxTypes") ?? 200,
                            ApiMaxMembers = GetInt(step, "apiMaxMembers") ?? 2000
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"LLMS generated ({res.Version})";
                        break;
                    }
                    case "sitemap":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url");
                        if (string.IsNullOrWhiteSpace(siteRoot) || string.IsNullOrWhiteSpace(baseUrl))
                            throw new InvalidOperationException("sitemap requires siteRoot and baseUrl.");

                        var entries = GetSitemapEntries(step, "entries");
                        var includeHtml = GetBool(step, "includeHtmlFiles");
                        var includeText = GetBool(step, "includeTextFiles");
                        var htmlEnabled = GetBool(step, "html") ?? false;
                        var htmlOutput = ResolvePath(baseDir, GetString(step, "htmlOutput") ?? GetString(step, "htmlOut") ?? GetString(step, "html-out"));
                        var htmlTemplate = ResolvePath(baseDir, GetString(step, "htmlTemplate") ?? GetString(step, "html-template"));
                        var htmlCss = GetString(step, "htmlCss") ?? GetString(step, "html-css");
                        var htmlTitle = GetString(step, "htmlTitle") ?? GetString(step, "html-title");
                        if (!htmlEnabled)
                        {
                            htmlEnabled = !string.IsNullOrWhiteSpace(htmlOutput) ||
                                          !string.IsNullOrWhiteSpace(htmlTemplate) ||
                                          !string.IsNullOrWhiteSpace(htmlCss) ||
                                          !string.IsNullOrWhiteSpace(htmlTitle);
                        }
                        var res = WebSitemapGenerator.Generate(new WebSitemapOptions
                        {
                            SiteRoot = siteRoot,
                            BaseUrl = baseUrl,
                            OutputPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output")),
                            ApiSitemapPath = ResolvePath(baseDir, GetString(step, "apiSitemap") ?? GetString(step, "api-sitemap")),
                            ExtraPaths = GetArrayOfStrings(step, "extraPaths") ?? GetArrayOfStrings(step, "extra-paths"),
                            Entries = entries.Length == 0 ? null : entries,
                            IncludeHtmlFiles = includeHtml ?? true,
                            IncludeTextFiles = includeText ?? true,
                            GenerateHtml = htmlEnabled,
                            HtmlOutputPath = htmlOutput,
                            HtmlTemplatePath = htmlTemplate,
                            HtmlCssHref = htmlCss,
                            HtmlTitle = htmlTitle
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"Sitemap {res.UrlCount} urls";
                        break;
                    }
                    case "optimize":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("optimize requires siteRoot.");

                        var configPath = ResolvePath(baseDir, GetString(step, "config"));
                        var minifyHtml = GetBool(step, "minifyHtml") ?? false;
                        var minifyCss = GetBool(step, "minifyCss") ?? false;
                        var minifyJs = GetBool(step, "minifyJs") ?? false;
                        var optimizeImages = GetBool(step, "optimizeImages") ?? GetBool(step, "images") ?? false;
                        var imageExtensions = GetArrayOfStrings(step, "imageExtensions") ?? GetArrayOfStrings(step, "image-ext");
                        var imageInclude = GetArrayOfStrings(step, "imageInclude") ?? GetArrayOfStrings(step, "image-include");
                        var imageExclude = GetArrayOfStrings(step, "imageExclude") ?? GetArrayOfStrings(step, "image-exclude");
                        var imageQuality = GetInt(step, "imageQuality") ?? GetInt(step, "image-quality") ?? 82;
                        var imageStripMetadata = GetBool(step, "imageStripMetadata") ?? GetBool(step, "image-strip-metadata") ?? true;
                        var imageGenerateWebp = GetBool(step, "imageGenerateWebp") ?? GetBool(step, "image-generate-webp") ?? false;
                        var imageGenerateAvif = GetBool(step, "imageGenerateAvif") ?? GetBool(step, "image-generate-avif") ?? false;
                        var imagePreferNextGen = GetBool(step, "imagePreferNextGen") ?? GetBool(step, "image-prefer-nextgen") ?? false;
                        var imageWidths = GetArrayOfStrings(step, "imageWidths") ?? GetArrayOfStrings(step, "image-widths");
                        var imageEnhanceTags = GetBool(step, "imageEnhanceTags") ?? GetBool(step, "image-enhance-tags") ?? false;
                        var imageMaxBytes = GetLong(step, "imageMaxBytesPerFile") ?? GetLong(step, "image-max-bytes") ?? 0;
                        var imageMaxTotalBytes = GetLong(step, "imageMaxTotalBytes") ?? GetLong(step, "image-max-total-bytes") ?? 0;
                        var imageFailOnBudget = GetBool(step, "imageFailOnBudget") ?? GetBool(step, "image-fail-on-budget") ?? false;
                        var hashAssets = GetBool(step, "hashAssets") ?? false;
                        var hashExtensions = GetArrayOfStrings(step, "hashExtensions") ?? GetArrayOfStrings(step, "hash-ext");
                        var hashExclude = GetArrayOfStrings(step, "hashExclude") ?? GetArrayOfStrings(step, "hash-exclude");
                        var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
                        var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                        var cacheHeaders = GetBool(step, "cacheHeaders") ?? GetBool(step, "headers") ?? false;
                        var cacheHeadersOut = GetString(step, "cacheHeadersOut") ?? GetString(step, "headersOut") ?? GetString(step, "headers-out");
                        var cacheHeadersHtml = GetString(step, "cacheHeadersHtml") ?? GetString(step, "headersHtml");
                        var cacheHeadersAssets = GetString(step, "cacheHeadersAssets") ?? GetString(step, "headersAssets");
                        var cacheHeadersPaths = GetArrayOfStrings(step, "cacheHeadersPaths") ?? GetArrayOfStrings(step, "headersPaths");

                        AssetPolicySpec? policy = null;
                        if (!string.IsNullOrWhiteSpace(configPath))
                        {
                            var (spec, _) = WebSiteSpecLoader.LoadWithPath(configPath, WebCliJson.Options);
                            policy = spec.AssetPolicy;
                        }
                        if (cacheHeaders)
                        {
                            policy ??= new AssetPolicySpec();
                            policy.CacheHeaders ??= new CacheHeadersSpec { Enabled = true };
                            policy.CacheHeaders.Enabled = true;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersOut))
                                policy.CacheHeaders.OutputPath = cacheHeadersOut;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersHtml))
                                policy.CacheHeaders.HtmlCacheControl = cacheHeadersHtml;
                            if (!string.IsNullOrWhiteSpace(cacheHeadersAssets))
                                policy.CacheHeaders.ImmutableCacheControl = cacheHeadersAssets;
                            if (cacheHeadersPaths is { Length: > 0 })
                                policy.CacheHeaders.ImmutablePaths = cacheHeadersPaths;
                        }

                        var optimize = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
                        {
                            SiteRoot = siteRoot,
                            CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
                            CssLinkPattern = GetString(step, "cssPattern") ?? "(app|api-docs)\\.css",
                            MinifyHtml = minifyHtml,
                            MinifyCss = minifyCss,
                            MinifyJs = minifyJs,
                            OptimizeImages = optimizeImages,
                            ImageExtensions = imageExtensions ?? new[] { ".png", ".jpg", ".jpeg", ".webp" },
                            ImageInclude = imageInclude ?? Array.Empty<string>(),
                            ImageExclude = imageExclude ?? Array.Empty<string>(),
                            ImageQuality = imageQuality,
                            ImageStripMetadata = imageStripMetadata,
                            ImageGenerateWebp = imageGenerateWebp,
                            ImageGenerateAvif = imageGenerateAvif,
                            ImagePreferNextGen = imagePreferNextGen,
                            ResponsiveImageWidths = ParseIntList(imageWidths),
                            EnhanceImageTags = imageEnhanceTags,
                            ImageMaxBytesPerFile = imageMaxBytes,
                            ImageMaxTotalBytes = imageMaxTotalBytes,
                            HashAssets = hashAssets,
                            HashExtensions = hashExtensions ?? new[] { ".css", ".js" },
                            HashExclude = hashExclude ?? Array.Empty<string>(),
                            HashManifestPath = hashManifest,
                            ReportPath = reportPath,
                            AssetPolicy = policy
                        });
                        if (imageFailOnBudget && optimize.ImageBudgetExceeded)
                            throw new InvalidOperationException($"Image budget exceeded: {string.Join(" | ", optimize.ImageBudgetWarnings)}");
                        stepResult.Success = true;
                        stepResult.Message = BuildOptimizeSummary(optimize);
                        break;
                    }
                    case "audit":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("audit requires siteRoot.");

                        var include = GetString(step, "include");
                        var exclude = GetString(step, "exclude");
                        var ignoreNav = GetString(step, "ignoreNav") ?? GetString(step, "ignore-nav");
                        var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                                GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
                        var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                                               GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
                        var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
                        var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
                        var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                                             GetString(step, "requiredRoute") ?? GetString(step, "required-route");
                        var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
                        var navRequired = GetBool(step, "navRequired");
                        var navOptional = GetBool(step, "navOptional");
                        var checkLinks = GetBool(step, "checkLinks") ?? true;
                        var checkAssets = GetBool(step, "checkAssets") ?? true;
                        var checkNav = GetBool(step, "checkNav") ?? true;
                        var checkTitles = GetBool(step, "checkTitles") ?? true;
                        var checkIds = GetBool(step, "checkDuplicateIds") ?? true;
                        var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
                        var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
                        var checkStructure = GetBool(step, "checkHtmlStructure") ?? true;
                        var rendered = GetBool(step, "rendered") ?? false;
                        var renderedEngine = GetString(step, "renderedEngine");
                        var renderedEnsureInstalled = GetBool(step, "renderedEnsureInstalled");
                        var renderedHeadless = GetBool(step, "renderedHeadless") ?? true;
                        var renderedBaseUrl = GetString(step, "renderedBaseUrl");
                        var renderedHost = GetString(step, "renderedHost");
                        var renderedPort = GetInt(step, "renderedPort") ?? 0;
                        var renderedServe = GetBool(step, "renderedServe") ?? true;
                        var renderedMaxPages = GetInt(step, "renderedMaxPages") ?? 20;
                        var renderedTimeoutMs = GetInt(step, "renderedTimeoutMs") ?? 30000;
                        var renderedCheckErrors = GetBool(step, "renderedCheckConsoleErrors") ?? true;
                        var renderedCheckWarnings = GetBool(step, "renderedCheckConsoleWarnings") ?? true;
                        var renderedCheckFailures = GetBool(step, "renderedCheckFailedRequests") ?? true;
                        var renderedInclude = GetString(step, "renderedInclude");
                        var renderedExclude = GetString(step, "renderedExclude");
                        var summary = GetBool(step, "summary") ?? false;
                        var summaryPath = GetString(step, "summaryPath");
                        var summaryMax = GetInt(step, "summaryMaxIssues") ?? 10;
                        var sarif = GetBool(step, "sarif") ?? false;
                        var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                        var baselineGenerate = GetBool(step, "baselineGenerate") ?? false;
                        var baselineUpdate = GetBool(step, "baselineUpdate") ?? false;
                        var baselinePath = GetString(step, "baselinePath") ?? GetString(step, "baseline");
                        var failOnWarnings = GetBool(step, "failOnWarnings") ?? false;
                        var failOnNewIssues = GetBool(step, "failOnNewIssues") ?? GetBool(step, "failOnNew") ?? false;
                        var maxErrors = GetInt(step, "maxErrors") ?? -1;
                        var maxWarnings = GetInt(step, "maxWarnings") ?? -1;
                        var failOnCategories = GetString(step, "failOnCategories") ?? GetString(step, "failCategories");
                        var navCanonicalPath = GetString(step, "navCanonicalPath") ?? GetString(step, "navCanonical");
                        var navCanonicalSelector = GetString(step, "navCanonicalSelector");
                        var navCanonicalRequired = GetBool(step, "navCanonicalRequired") ?? false;
                        var checkUtf8 = GetBool(step, "checkUtf8") ?? true;
                        var checkMetaCharset = GetBool(step, "checkMetaCharset") ?? true;
                        var checkReplacement = GetBool(step, "checkUnicodeReplacementChars") ?? true;
                        var checkNetworkHints = GetBool(step, "checkNetworkHints");
                        var checkRenderBlocking = GetBool(step, "checkRenderBlockingResources") ?? GetBool(step, "checkRenderBlocking");
                        var maxHeadBlockingResources = GetInt(step, "maxHeadBlockingResources") ?? GetInt(step, "max-head-blocking");
                        if ((baselineGenerate || baselineUpdate) && string.IsNullOrWhiteSpace(baselinePath))
                            baselinePath = "audit-baseline.json";
                        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
                        var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
                        var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
                        var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
                        var navRequiredValue = navRequired ?? !(navOptional ?? false);
                        var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
                        var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
                        var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);

                        var ensureInstall = rendered && (renderedEnsureInstalled ?? true);
                        var audit = WebSiteAuditor.Audit(new WebAuditOptions
                        {
                            SiteRoot = siteRoot,
                            Include = CliPatternHelper.SplitPatterns(include),
                            Exclude = CliPatternHelper.SplitPatterns(exclude),
                            UseDefaultExcludes = useDefaultExclude,
                            IgnoreNavFor = ignoreNavPatterns,
                            NavSelector = navSelector,
                            NavRequired = navRequiredValue,
                            NavIgnorePrefixes = navIgnorePrefixList,
                            NavRequiredLinks = CliPatternHelper.SplitPatterns(navRequiredLinks),
                            NavProfiles = navProfiles,
                            MinNavCoveragePercent = minNavCoveragePercent,
                            RequiredRoutes = CliPatternHelper.SplitPatterns(requiredRoutes),
                            CheckLinks = checkLinks,
                            CheckAssets = checkAssets,
                            CheckNavConsistency = checkNav,
                            CheckTitles = checkTitles,
                            CheckDuplicateIds = checkIds,
                            CheckHeadingOrder = checkHeadingOrder,
                            CheckLinkPurposeConsistency = checkLinkPurpose,
                            CheckHtmlStructure = checkStructure,
                            CheckRendered = rendered,
                            RenderedEngine = renderedEngine ?? "Chromium",
                            RenderedEnsureInstalled = ensureInstall,
                            RenderedHeadless = renderedHeadless,
                            RenderedBaseUrl = renderedBaseUrl,
                            RenderedServe = renderedServe,
                            RenderedServeHost = string.IsNullOrWhiteSpace(renderedHost) ? "localhost" : renderedHost,
                            RenderedServePort = renderedPort,
                            RenderedMaxPages = renderedMaxPages,
                            RenderedTimeoutMs = renderedTimeoutMs,
                            RenderedCheckConsoleErrors = renderedCheckErrors,
                            RenderedCheckConsoleWarnings = renderedCheckWarnings,
                            RenderedCheckFailedRequests = renderedCheckFailures,
                            RenderedInclude = CliPatternHelper.SplitPatterns(renderedInclude),
                            RenderedExclude = CliPatternHelper.SplitPatterns(renderedExclude),
                            SummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath),
                            SarifPath = resolvedSarifPath,
                            SummaryMaxIssues = summaryMax,
                            BaselinePath = baselinePath,
                            FailOnWarnings = failOnWarnings,
                            FailOnNewIssues = failOnNewIssues,
                            MaxErrors = maxErrors,
                            MaxWarnings = maxWarnings,
                            FailOnCategories = CliPatternHelper.SplitPatterns(failOnCategories),
                            NavCanonicalPath = navCanonicalPath,
                            NavCanonicalSelector = navCanonicalSelector,
                            NavCanonicalRequired = navCanonicalRequired,
                            CheckUtf8 = checkUtf8,
                            CheckMetaCharset = checkMetaCharset,
                            CheckUnicodeReplacementChars = checkReplacement,
                            CheckNetworkHints = checkNetworkHints ?? true,
                            CheckRenderBlockingResources = checkRenderBlocking ?? true,
                            MaxHeadBlockingResources = maxHeadBlockingResources ?? new WebAuditOptions().MaxHeadBlockingResources
                        });

                        string? baselineWrittenPath = null;
                        if (baselineGenerate || baselineUpdate)
                        {
                            baselineWrittenPath = WebAuditBaselineStore.Write(siteRoot, baselinePath, audit, baselineUpdate, logger);
                            audit.BaselinePath = baselineWrittenPath;
                        }

                        stepResult.Success = audit.Success;
                        stepResult.Message = audit.Success
                            ? BuildAuditSummary(audit)
                            : BuildAuditFailureSummary(audit, GetInt(step, "errorPreviewCount") ?? 5);
                        if (!string.IsNullOrWhiteSpace(baselineWrittenPath))
                            stepResult.Message += $", baseline {baselineWrittenPath}";
                        if (!audit.Success)
                            throw new InvalidOperationException(stepResult.Message);
                        break;
                    }
                    case "doctor":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        if (string.IsNullOrWhiteSpace(config))
                            throw new InvalidOperationException("doctor requires config.");

                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var runBuild = GetBool(step, "build");
                        var runVerify = GetBool(step, "verify");
                        var runAudit = GetBool(step, "audit");
                        var noBuild = GetBool(step, "noBuild") ?? false;
                        var noVerify = GetBool(step, "noVerify") ?? false;
                        var noAudit = GetBool(step, "noAudit") ?? false;
                        var executeBuild = runBuild ?? !noBuild;
                        var executeVerify = runVerify ?? !noVerify;
                        var executeAudit = runAudit ?? !noAudit;

                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        if (string.IsNullOrWhiteSpace(outPath))
                            outPath = Path.Combine(Path.GetDirectoryName(config) ?? ".", "_site");
                        var effectiveSiteRoot = string.IsNullOrWhiteSpace(siteRoot) ? outPath : siteRoot;

                        if (executeBuild)
                        {
                            WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                            effectiveSiteRoot = outPath;
                        }

                        if (executeAudit && (string.IsNullOrWhiteSpace(effectiveSiteRoot) || !Directory.Exists(effectiveSiteRoot)))
                            throw new InvalidOperationException("doctor audit requires existing siteRoot. Provide siteRoot or enable build.");

                        WebVerifyResult? verify = null;
                        if (executeVerify)
                        {
                            verify = WebSiteVerifier.Verify(spec, plan);
                            if (!verify.Success)
                            {
                                var firstError = verify.Errors.Length > 0 ? verify.Errors[0] : "Web verify failed.";
                                throw new InvalidOperationException(firstError);
                            }
                        }

                        WebAuditResult? audit = null;
                        if (executeAudit)
                        {
                            var include = GetString(step, "include");
                            var exclude = GetString(step, "exclude");
                            var ignoreNav = GetString(step, "ignoreNav") ?? GetString(step, "ignore-nav");
                            var navIgnorePrefixes = GetString(step, "navIgnorePrefixes") ?? GetString(step, "nav-ignore-prefixes") ??
                                                    GetString(step, "navIgnorePrefix") ?? GetString(step, "nav-ignore-prefix");
                            var navRequiredLinks = GetString(step, "navRequiredLinks") ?? GetString(step, "nav-required-links") ??
                                                   GetString(step, "navRequiredLink") ?? GetString(step, "nav-required-link");
                            var navProfilesPath = GetString(step, "navProfiles") ?? GetString(step, "nav-profiles");
                            var requiredRoutes = GetString(step, "requiredRoutes") ?? GetString(step, "required-routes") ??
                                                 GetString(step, "requiredRoute") ?? GetString(step, "required-route");
                            var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
                            var navRequired = GetBool(step, "navRequired");
                            var navOptional = GetBool(step, "navOptional");
                            var minNavCoveragePercent = GetInt(step, "minNavCoveragePercent") ?? GetInt(step, "min-nav-coverage") ?? 0;
                            var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
                            var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
                            var summary = GetBool(step, "summary") ?? false;
                            var summaryPath = GetString(step, "summaryPath");
                            var summaryMax = GetInt(step, "summaryMaxIssues") ?? 10;
                            var sarif = GetBool(step, "sarif") ?? false;
                            var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                            var navCanonicalPath = GetString(step, "navCanonicalPath") ?? GetString(step, "navCanonical");
                            var navCanonicalSelector = GetString(step, "navCanonicalSelector");
                            var navCanonicalRequired = GetBool(step, "navCanonicalRequired") ?? false;
                            var checkUtf8 = GetBool(step, "checkUtf8") ?? true;
                            var checkMetaCharset = GetBool(step, "checkMetaCharset") ?? true;
                            var checkReplacement = GetBool(step, "checkUnicodeReplacementChars") ?? true;
                            var checkHeadingOrder = GetBool(step, "checkHeadingOrder") ?? true;
                            var checkLinkPurpose = GetBool(step, "checkLinkPurposeConsistency") ?? GetBool(step, "checkLinkPurpose") ?? true;
                            var checkNetworkHints = GetBool(step, "checkNetworkHints") ?? true;
                            var checkRenderBlocking = GetBool(step, "checkRenderBlockingResources") ?? GetBool(step, "checkRenderBlocking") ?? true;
                            var maxHeadBlockingResources = GetInt(step, "maxHeadBlockingResources") ?? GetInt(step, "max-head-blocking") ?? new WebAuditOptions().MaxHeadBlockingResources;

                            var requiredRouteList = CliPatternHelper.SplitPatterns(requiredRoutes).ToList();
                            if (requiredRouteList.Count == 0)
                                requiredRouteList.Add("/404.html");
                            var navRequiredLinksList = CliPatternHelper.SplitPatterns(navRequiredLinks).ToList();
                            if (navRequiredLinksList.Count == 0)
                                navRequiredLinksList.Add("/");

                            var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
                            var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
                            var navRequiredValue = navRequired ?? !(navOptional ?? false);
                            var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);
                            var navProfiles = LoadAuditNavProfilesForPipeline(baseDir, navProfilesPath);
                            var resolvedSarifPath = ResolveSarifPathForPipeline(sarif, sarifPath);

                            audit = WebSiteAuditor.Audit(new WebAuditOptions
                            {
                                SiteRoot = effectiveSiteRoot!,
                                Include = CliPatternHelper.SplitPatterns(include),
                                Exclude = CliPatternHelper.SplitPatterns(exclude),
                                UseDefaultExcludes = useDefaultExclude,
                                IgnoreNavFor = ignoreNavPatterns,
                                NavSelector = navSelector,
                                NavRequired = navRequiredValue,
                                NavIgnorePrefixes = navIgnorePrefixList,
                                NavRequiredLinks = navRequiredLinksList.ToArray(),
                                NavProfiles = navProfiles,
                                MinNavCoveragePercent = minNavCoveragePercent,
                                RequiredRoutes = requiredRouteList.ToArray(),
                                CheckLinks = GetBool(step, "checkLinks") ?? true,
                                CheckAssets = GetBool(step, "checkAssets") ?? true,
                                CheckNavConsistency = GetBool(step, "checkNav") ?? true,
                                CheckTitles = GetBool(step, "checkTitles") ?? true,
                                CheckDuplicateIds = GetBool(step, "checkDuplicateIds") ?? true,
                                CheckHtmlStructure = GetBool(step, "checkHtmlStructure") ?? true,
                                SummaryPath = ResolveSummaryPathForPipeline(summary, summaryPath),
                                SarifPath = resolvedSarifPath,
                                SummaryMaxIssues = summaryMax,
                                NavCanonicalPath = navCanonicalPath,
                                NavCanonicalSelector = navCanonicalSelector,
                                NavCanonicalRequired = navCanonicalRequired,
                                CheckUtf8 = checkUtf8,
                                CheckMetaCharset = checkMetaCharset,
                                CheckUnicodeReplacementChars = checkReplacement,
                                CheckHeadingOrder = checkHeadingOrder,
                                CheckLinkPurposeConsistency = checkLinkPurpose,
                                CheckNetworkHints = checkNetworkHints,
                                CheckRenderBlockingResources = checkRenderBlocking,
                                MaxHeadBlockingResources = maxHeadBlockingResources
                            });

                            if (!audit.Success)
                                throw new InvalidOperationException(BuildAuditFailureSummary(audit, GetInt(step, "errorPreviewCount") ?? 5));
                        }

                        stepResult.Success = true;
                        stepResult.Message = BuildDoctorSummary(verify, audit, executeBuild, executeVerify, executeAudit);
                        break;
                    }
                    case "dotnet-build":
                    {
                        var project = ResolvePath(baseDir, GetString(step, "project") ?? GetString(step, "solution") ?? GetString(step, "path"));
                        var configuration = GetString(step, "configuration");
                        var framework = GetString(step, "framework");
                        var runtime = GetString(step, "runtime");
                        var noRestore = GetBool(step, "noRestore") ?? false;
                        if (string.IsNullOrWhiteSpace(project))
                            throw new InvalidOperationException("dotnet-build requires project.");

                        var res = WebDotNetRunner.Build(new WebDotNetBuildOptions
                        {
                            ProjectOrSolution = project,
                            Configuration = configuration,
                            Framework = framework,
                            Runtime = runtime,
                            Restore = !noRestore
                        });
                        var buildError = string.IsNullOrWhiteSpace(res.Error) ? res.Output : res.Error;
                        stepResult.Success = res.Success;
                        stepResult.Message = res.Success ? "dotnet build ok" : buildError;
                        if (!res.Success) throw new InvalidOperationException(buildError);
                        break;
                    }
                    case "dotnet-publish":
                    {
                        var project = ResolvePath(baseDir, GetString(step, "project"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var cleanOutput = GetBool(step, "clean") ?? false;
                        var configuration = GetString(step, "configuration");
                        var framework = GetString(step, "framework");
                        var runtime = GetString(step, "runtime");
                        var selfContained = GetBool(step, "selfContained") ?? false;
                        var noBuild = GetBool(step, "noBuild") ?? false;
                        var noRestore = GetBool(step, "noRestore") ?? false;
                        var baseHref = GetString(step, "baseHref");
                        var defineConstants = GetString(step, "defineConstants") ?? GetString(step, "define-constants");
                        var noBlazorFixes = GetBool(step, "noBlazorFixes") ?? false;
                        var blazorFixes = GetBool(step, "blazorFixes") ?? !noBlazorFixes;

                        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("dotnet-publish requires project and out.");
                        if (cleanOutput)
                            WebCliFileSystem.CleanOutputDirectory(outPath);

                        var res = WebDotNetRunner.Publish(new WebDotNetPublishOptions
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

                        var publishError = string.IsNullOrWhiteSpace(res.Error) ? res.Output : res.Error;
                        if (!res.Success) throw new InvalidOperationException(publishError);
                        if (blazorFixes)
                        {
                            WebBlazorPublishFixer.Apply(new WebBlazorPublishFixOptions
                            {
                                PublishRoot = outPath,
                                BaseHref = baseHref
                            });
                        }

                        stepResult.Success = true;
                        stepResult.Message = "dotnet publish ok";
                        break;
                    }
                    case "overlay":
                    {
                        var source = ResolvePath(baseDir, GetString(step, "source"));
                        var destination = ResolvePath(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
                        var include = GetString(step, "include");
                        var exclude = GetString(step, "exclude");
                        var clean = GetBool(step, "clean") ?? false;
                        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                            throw new InvalidOperationException("overlay requires source and destination.");

                        var res = WebStaticOverlay.Apply(new WebStaticOverlayOptions
                        {
                            SourceRoot = source,
                            DestinationRoot = destination,
                            Clean = clean,
                            Include = CliPatternHelper.SplitPatterns(include),
                            Exclude = CliPatternHelper.SplitPatterns(exclude)
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"overlay {res.CopiedCount} files";
                        break;
                    }
                    default:
                        stepResult.Success = false;
                        stepResult.Message = "Unknown task";
                        break;
                }
            }
            catch (Exception ex)
            {
                stepResult.Success = false;
                stepResult.Message = AppendDuration(ex.Message, stopwatch);
                stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
                result.Steps.Add(stepResult);
                stepResultsByIndex[stepIndex] = stepResult;
                result.StepCount = result.Steps.Count;
                result.Success = false;
                result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
                if (cacheEnabled && cacheState is not null && cacheUpdated)
                    SavePipelineCache(cachePath, cacheState, logger);
                if (!string.IsNullOrWhiteSpace(profilePath))
                {
                    WritePipelineProfile(profilePath, result, logger);
                    result.ProfilePath = profilePath;
                }
                return result;
            }

            stepResult.Message = AppendDuration(stepResult.Message, stopwatch);
            stepResult.DurationMs = (long)Math.Round(stopwatch.Elapsed.TotalMilliseconds);
            if (cacheEnabled && cacheState is not null && !string.IsNullOrWhiteSpace(stepFingerprint))
            {
                cacheState.Entries[cacheKey] = new WebPipelineCacheEntry
                {
                    Fingerprint = stepFingerprint,
                    Message = stepResult.Message
                };
                cacheUpdated = true;
            }
            result.Steps.Add(stepResult);
            stepResultsByIndex[stepIndex] = stepResult;
            if (profileEnabled)
                logger?.Info($"Finished {label} in {FormatDuration(stopwatch.Elapsed)}");
        }

        runStopwatch.Stop();
        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        result.DurationMs = (long)Math.Round(runStopwatch.Elapsed.TotalMilliseconds);
        if (cacheEnabled && cacheState is not null && cacheUpdated)
            SavePipelineCache(cachePath, cacheState, logger);
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            WritePipelineProfile(profilePath, result, logger);
            result.ProfilePath = profilePath;
        }
        return result;
    }

    private static List<PipelineStepDefinition> BuildStepDefinitions(JsonElement stepsElement)
    {
        var steps = new List<PipelineStepDefinition>();
        var aliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var step in stepsElement.EnumerateArray())
        {
            var task = GetString(step, "task")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(task))
                continue;

            index++;
            var id = GetString(step, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = $"{task}-{index}";

            if (aliases.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate pipeline step id '{id}'.");

            aliases[id] = index;
            aliases[$"{task}#{index}"] = index;
            if (!aliases.ContainsKey(task))
                aliases[task] = index;

            steps.Add(new PipelineStepDefinition
            {
                Index = index,
                Task = task,
                Id = id,
                DependsOn = ParseDependsOn(step),
                Element = step
            });
        }

        foreach (var step in steps)
        {
            if (step.DependsOn.Length == 0)
                continue;

            var resolved = new List<int>();
            foreach (var dependency in step.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                    continue;

                if (int.TryParse(dependency, out var numeric))
                {
                    if (numeric <= 0 || numeric > steps.Count)
                        throw new InvalidOperationException($"Step '{step.Id}' has invalid dependsOn reference '{dependency}'.");
                    resolved.Add(numeric);
                    continue;
                }

                if (!aliases.TryGetValue(dependency, out var dependencyIndex))
                    throw new InvalidOperationException($"Step '{step.Id}' has unknown dependsOn reference '{dependency}'.");

                resolved.Add(dependencyIndex);
            }

            step.DependencyIndexes = resolved
                .Distinct()
                .OrderBy(value => value)
                .ToArray();

            if (step.DependencyIndexes.Any(value => value >= step.Index))
                throw new InvalidOperationException($"Step '{step.Id}' has dependsOn reference to current/future step.");
        }

        return steps;
    }

    private static string[] ParseDependsOn(JsonElement step)
    {
        var array = GetArrayOfStrings(step, "dependsOn") ?? GetArrayOfStrings(step, "depends-on");
        if (array is { Length: > 0 })
            return array;

        var value = GetString(step, "dependsOn") ?? GetString(step, "depends-on");
        return CliPatternHelper.SplitPatterns(value);
    }

    private static string AppendDuration(string? message, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var duration = FormatDuration(stopwatch.Elapsed);
        var baseMessage = string.IsNullOrWhiteSpace(message) ? "Completed" : message;
        return $"{baseMessage} ({duration})";
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
            return $"{elapsed.TotalMilliseconds:0} ms";
        if (elapsed.TotalMinutes < 1)
            return $"{elapsed.TotalSeconds:0.0} s";
        return $"{elapsed.TotalMinutes:0.0} min";
    }

    private static string BuildOptimizeSummary(WebOptimizeResult result)
    {
        var parts = new List<string> { $"updated {result.UpdatedCount}" };

        if (result.CriticalCssInlinedCount > 0)
            parts.Add($"critical-css {result.CriticalCssInlinedCount}");
        if (result.HtmlMinifiedCount > 0)
            parts.Add($"html {result.HtmlMinifiedCount}");
        if (result.CssMinifiedCount > 0)
            parts.Add($"css {result.CssMinifiedCount}");
        if (result.JsMinifiedCount > 0)
            parts.Add($"js {result.JsMinifiedCount}");
        if (result.HtmlBytesSaved > 0)
            parts.Add($"html-saved {result.HtmlBytesSaved}B");
        if (result.CssBytesSaved > 0)
            parts.Add($"css-saved {result.CssBytesSaved}B");
        if (result.JsBytesSaved > 0)
            parts.Add($"js-saved {result.JsBytesSaved}B");
        if (result.ImageOptimizedCount > 0)
            parts.Add($"images {result.ImageOptimizedCount}");
        if (result.ImageBytesSaved > 0)
            parts.Add($"images-saved {result.ImageBytesSaved}B");
        if (result.ImageVariantCount > 0)
            parts.Add($"image-variants {result.ImageVariantCount}");
        if (result.ImageHtmlRewriteCount > 0)
            parts.Add($"image-rewrites {result.ImageHtmlRewriteCount}");
        if (result.ImageHintedCount > 0)
            parts.Add($"image-hints {result.ImageHintedCount}");
        if (result.OptimizedImages.Length > 0)
        {
            var top = result.OptimizedImages[0];
            parts.Add($"top-image {top.Path}(-{top.BytesSaved}B)");
        }
        if (result.ImageBudgetExceeded)
            parts.Add("image-budget-exceeded");

        if (result.HashedAssetCount > 0)
            parts.Add($"hashed {result.HashedAssetCount}");
        if (result.CacheHeadersWritten)
            parts.Add("headers");
        if (!string.IsNullOrWhiteSpace(result.ReportPath))
            parts.Add("report");

        return $"Optimize {string.Join(", ", parts)}";
    }

    private static string BuildAuditSummary(WebAuditResult result)
    {
        var parts = new List<string>
        {
            $"pages {result.PageCount}",
            $"links {result.LinkCount}",
            $"assets {result.AssetCount}"
        };

        if (result.BrokenLinkCount > 0)
            parts.Add($"broken-links {result.BrokenLinkCount}");
        if (result.MissingAssetCount > 0)
            parts.Add($"missing-assets {result.MissingAssetCount}");
        parts.Add($"nav-checked {result.NavCheckedCount}");
        if (result.NavIgnoredCount > 0)
            parts.Add($"nav-ignored {result.NavIgnoredCount}");
        parts.Add($"nav-coverage {result.NavCoveragePercent:0.0}%");
        if (result.NavMismatchCount > 0)
            parts.Add($"nav-mismatches {result.NavMismatchCount}");
        if (result.RequiredRouteCount > 0)
            parts.Add($"required-routes {result.RequiredRouteCount}");
        if (result.MissingRequiredRouteCount > 0)
            parts.Add($"missing-required-routes {result.MissingRequiredRouteCount}");
        if (result.WarningCount > 0)
            parts.Add($"warnings {result.WarningCount}");
        if (result.NewIssueCount > 0)
            parts.Add($"new {result.NewIssueCount}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add("sarif");

        return $"Audit ok {string.Join(", ", parts)}";
    }

    private static string BuildDoctorSummary(
        WebVerifyResult? verify,
        WebAuditResult? audit,
        bool buildExecuted,
        bool verifyExecuted,
        bool auditExecuted)
    {
        var parts = new List<string>();
        parts.Add(buildExecuted ? "build" : "no-build");
        parts.Add(verifyExecuted ? "verify" : "no-verify");
        parts.Add(auditExecuted ? "audit" : "no-audit");

        if (verify is not null)
            parts.Add($"verify {verify.Errors.Length}e/{verify.Warnings.Length}w");
        if (audit is not null)
            parts.Add($"audit {audit.ErrorCount}e/{audit.WarningCount}w");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SummaryPath))
            parts.Add("summary");
        if (audit is not null && !string.IsNullOrWhiteSpace(audit.SarifPath))
            parts.Add("sarif");

        return $"Doctor ok {string.Join(", ", parts)}";
    }

    private static string BuildAuditFailureSummary(WebAuditResult result, int previewCount)
    {
        var safePreviewCount = Math.Clamp(previewCount, 0, 50);
        var parts = new List<string>
        {
            $"Audit failed ({result.Errors.Length} errors)"
        };

        if (!string.IsNullOrWhiteSpace(result.SummaryPath))
            parts.Add($"summary {result.SummaryPath}");
        if (!string.IsNullOrWhiteSpace(result.SarifPath))
            parts.Add($"sarif {result.SarifPath}");

        if (safePreviewCount <= 0 || result.Errors.Length == 0)
            return string.Join(", ", parts);

        var preview = result.Errors
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Take(safePreviewCount)
            .Select(error => TruncateForLog(error, 220))
            .ToArray();

        if (preview.Length == 0)
            return string.Join(", ", parts);

        var previewText = string.Join(" | ", preview);
        var remaining = result.Errors.Length - preview.Length;
        if (remaining > 0)
            previewText += $" | +{remaining} more";

        parts.Add($"sample: {previewText}");

        if (result.Issues.Length > 0)
        {
            var issuePreviewCount = Math.Min(safePreviewCount, 5);
            if (issuePreviewCount > 0)
            {
                var candidateIssues = result.Issues
                    .Where(static issue => !IsGateIssue(issue))
                    .ToArray();
                if (candidateIssues.Length == 0)
                    candidateIssues = result.Issues;

                var issueSample = candidateIssues
                    .Where(static issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase))
                    .Take(issuePreviewCount)
                    .ToArray();

                if (issueSample.Length == 0)
                {
                    issueSample = candidateIssues
                        .Where(static issue => !string.IsNullOrWhiteSpace(issue.Message))
                        .Take(issuePreviewCount)
                        .ToArray();
                }

                if (issueSample.Length > 0)
                {
                    var issueText = string.Join(" | ", issueSample.Select(FormatIssueForLog));
                    var issueRemaining = result.Issues.Length - issueSample.Length;
                    if (issueRemaining > 0)
                        issueText += $" | +{issueRemaining} more issues";

                    parts.Add($"issues: {issueText}");
                }
            }
        }

        return string.Join(", ", parts);
    }

    private static bool IsGateIssue(WebAuditIssue issue)
    {
        if (string.Equals(issue.Category, "gate", StringComparison.OrdinalIgnoreCase))
            return true;

        return issue.Message.StartsWith("Audit gate failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatIssueForLog(WebAuditIssue issue)
    {
        var severity = string.IsNullOrWhiteSpace(issue.Severity) ? "warning" : issue.Severity;
        var category = string.IsNullOrWhiteSpace(issue.Category) ? "general" : issue.Category;
        var location = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : $" {issue.Path}";
        var message = string.IsNullOrWhiteSpace(issue.Message) ? "issue reported" : issue.Message;
        return TruncateForLog($"[{severity}] [{category}]{location} {message}", 220);
    }

    private static string TruncateForLog(string text, int maxLength)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..Math.Max(0, maxLength - 3)] + "...";
    }

    private static WebPipelineCacheState LoadPipelineCache(string cachePath, WebConsoleLogger? logger)
    {
        try
        {
            if (!File.Exists(cachePath))
                return new WebPipelineCacheState();

            var fileInfo = new FileInfo(cachePath);
            if (fileInfo.Length > MaxStateFileSizeBytes)
            {
                logger?.Warn($"Pipeline cache file too large ({fileInfo.Length} bytes), ignoring cache.");
                return new WebPipelineCacheState();
            }

            using var stream = File.OpenRead(cachePath);
            var state = JsonSerializer.Deserialize<WebPipelineCacheState>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return state ?? new WebPipelineCacheState();
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache load failed: {ex.Message}");
            return new WebPipelineCacheState();
        }
    }

    private static void SavePipelineCache(string cachePath, WebPipelineCacheState state, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(cachePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline cache save failed: {ex.Message}");
        }
    }

    private static void WritePipelineProfile(string profilePath, WebPipelineResult result, WebConsoleLogger? logger)
    {
        try
        {
            var directory = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(result, WebCliJson.Context.WebPipelineResult);
            File.WriteAllText(profilePath, json);
        }
        catch (Exception ex)
        {
            logger?.Warn($"Pipeline profile write failed: {ex.Message}");
        }
    }

    private static string ComputeStepFingerprint(string baseDir, JsonElement step)
    {
        var parts = new List<string> { step.GetRawText() };
        var paths = EnumerateFingerprintPaths(baseDir, step)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            parts.Add(BuildPathStamp(path));
        }

        var payload = string.Join('\n', parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IEnumerable<string> EnumerateFingerprintPaths(string baseDir, JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in step.EnumerateObject())
        {
            if (!FingerprintPathKeys.Contains(property.Name))
                continue;

            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value) || IsExternalUri(value))
                    continue;
                var resolved = ResolvePath(baseDir, value);
                if (!string.IsNullOrWhiteSpace(resolved))
                    yield return Path.GetFullPath(resolved);
            }
        }
    }

    private static string BuildPathStamp(string path)
    {
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            return $"f|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }

        if (!Directory.Exists(path))
            return $"m|{path}";

        try
        {
            var maxTicks = Directory.GetLastWriteTimeUtc(path).Ticks;
            var fileCount = 0;
            var truncated = false;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (fileCount >= MaxStampFileCount)
                {
                    truncated = true;
                    break;
                }

                fileCount++;
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > maxTicks)
                    maxTicks = ticks;
            }

            return truncated
                ? $"d|{path}|{fileCount}|{maxTicks}|truncated"
                : $"d|{path}|{fileCount}|{maxTicks}";
        }
        catch
        {
            return $"d|{path}|unreadable";
        }
    }

    private static bool IsExternalUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetExpectedStepOutputs(string task, JsonElement step, string baseDir)
    {
        switch (task)
        {
            case "build":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "apidocs":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "dotnet-publish":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "overlay":
                return ResolveOutputCandidates(baseDir, GetString(step, "destination") ?? GetString(step, "dest"));
            case "changelog":
                return ResolveOutputCandidates(baseDir, GetString(step, "out") ?? GetString(step, "output"));
            case "llms":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (string.IsNullOrWhiteSpace(siteRoot))
                    return Array.Empty<string>();
                return new[]
                {
                    Path.Combine(siteRoot, "llms.txt"),
                    Path.Combine(siteRoot, "llms.json"),
                    Path.Combine(siteRoot, "llms-full.txt")
                };
            }
            case "sitemap":
            {
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(siteRoot))
                    outPath = Path.Combine(siteRoot, "sitemap.xml");
                return ResolveOutputCandidates(baseDir, outPath);
            }
            case "optimize":
            {
                var outputs = new List<string>();
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var reportPath = GetString(step, "reportPath") ?? GetString(step, "report-path");
                var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
                var cacheHeaders = GetBool(step, "cacheHeaders") ?? GetBool(step, "headers") ?? false;
                var cacheHeadersOut = GetString(step, "cacheHeadersOut") ?? GetString(step, "headersOut") ?? GetString(step, "headers-out");

                if (!string.IsNullOrWhiteSpace(siteRoot))
                {
                    if (!string.IsNullOrWhiteSpace(reportPath))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, reportPath));
                    if (!string.IsNullOrWhiteSpace(hashManifest))
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, hashManifest));
                    if (cacheHeaders)
                    {
                        var headersPath = string.IsNullOrWhiteSpace(cacheHeadersOut) ? "_headers" : cacheHeadersOut;
                        outputs.AddRange(ResolveOutputCandidates(siteRoot, headersPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "audit":
            {
                var outputs = new List<string>();
                var summaryEnabled = GetBool(step, "summary") ?? false;
                var summaryPath = GetString(step, "summaryPath");
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                {
                    if (string.IsNullOrWhiteSpace(summaryPath))
                        summaryPath = "audit-summary.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(summaryPath))
                        summaryPath = Path.Combine(siteRoot, summaryPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                }

                var sarifEnabled = GetBool(step, "sarif") ?? false;
                var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                {
                    if (string.IsNullOrWhiteSpace(sarifPath))
                        sarifPath = "audit.sarif.json";
                    if (!string.IsNullOrWhiteSpace(siteRoot) && !Path.IsPathRooted(sarifPath))
                        sarifPath = Path.Combine(siteRoot, sarifPath);
                    outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            case "doctor":
            {
                var outputs = new List<string>();
                var configPath = ResolvePath(baseDir, GetString(step, "config"));
                var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                var runBuild = GetBool(step, "build");
                var noBuild = GetBool(step, "noBuild") ?? false;
                var executeBuild = runBuild ?? !noBuild;
                if (string.IsNullOrWhiteSpace(outPath) && !string.IsNullOrWhiteSpace(configPath))
                    outPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", "_site");
                var effectiveSiteRoot = string.IsNullOrWhiteSpace(siteRoot) ? outPath : siteRoot;

                if (executeBuild && !string.IsNullOrWhiteSpace(outPath))
                    outputs.AddRange(ResolveOutputCandidates(baseDir, outPath));

                var runAudit = GetBool(step, "audit");
                var noAudit = GetBool(step, "noAudit") ?? false;
                var executeAudit = runAudit ?? !noAudit;
                if (executeAudit && !string.IsNullOrWhiteSpace(effectiveSiteRoot))
                {
                    var summaryEnabled = GetBool(step, "summary") ?? false;
                    var summaryPath = GetString(step, "summaryPath");
                    if (summaryEnabled || !string.IsNullOrWhiteSpace(summaryPath))
                    {
                        if (string.IsNullOrWhiteSpace(summaryPath))
                            summaryPath = "audit-summary.json";
                        if (!Path.IsPathRooted(summaryPath))
                            summaryPath = Path.Combine(effectiveSiteRoot, summaryPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, summaryPath));
                    }

                    var sarifEnabled = GetBool(step, "sarif") ?? false;
                    var sarifPath = GetString(step, "sarifPath") ?? GetString(step, "sarif-path");
                    if (sarifEnabled || !string.IsNullOrWhiteSpace(sarifPath))
                    {
                        if (string.IsNullOrWhiteSpace(sarifPath))
                            sarifPath = "audit.sarif.json";
                        if (!Path.IsPathRooted(sarifPath))
                            sarifPath = Path.Combine(effectiveSiteRoot, sarifPath);
                        outputs.AddRange(ResolveOutputCandidates(baseDir, sarifPath));
                    }
                }

                return outputs
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            default:
                return Array.Empty<string>();
        }
    }

    private static string[] ResolveOutputCandidates(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();
        if (IsExternalUri(value))
            return Array.Empty<string>();
        var resolved = ResolvePath(baseDir, value);
        if (string.IsNullOrWhiteSpace(resolved))
            return Array.Empty<string>();
        return new[] { Path.GetFullPath(resolved) };
    }

    private static bool AreExpectedOutputsPresent(string[] outputs)
    {
        if (outputs.Length == 0)
            return true;

        foreach (var output in outputs)
        {
            if (File.Exists(output))
                continue;
            if (Directory.Exists(output))
                continue;
            return false;
        }

        return true;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.True ? true :
               value.ValueKind == JsonValueKind.False ? false : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var num)) return num;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed)) return parsed;
        return null;
    }

    private static int[] ParseIntList(string[]? values)
    {
        if (values is null || values.Length == 0)
            return Array.Empty<int>();

        var list = new List<int>();
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            foreach (var token in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token.Trim(), out var parsed) && parsed > 0)
                    list.Add(parsed);
            }
        }

        return list
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    private static WebApiDetailLevel ParseApiDetailLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return WebApiDetailLevel.None;
        return Enum.TryParse<WebApiDetailLevel>(value, true, out var parsed) ? parsed : WebApiDetailLevel.None;
    }

    private static string[]? GetArrayOfStrings(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString() ?? string.Empty);
            else if (item.ValueKind != JsonValueKind.Null)
                list.Add(item.ToString());
        }
        return list.Count == 0 ? null : list.ToArray();
    }

    private static WebSitemapEntry[] GetSitemapEntries(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return Array.Empty<WebSitemapEntry>();
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<WebSitemapEntry>();

        var list = new List<WebSitemapEntry>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var path = GetString(item, "path") ?? GetString(item, "route") ?? GetString(item, "url");
            if (string.IsNullOrWhiteSpace(path)) continue;
            list.Add(new WebSitemapEntry
            {
                Path = path,
                ChangeFrequency = GetString(item, "changefreq") ?? GetString(item, "changeFrequency"),
                Priority = GetString(item, "priority"),
                LastModified = GetString(item, "lastmod") ?? GetString(item, "lastModified")
            });
        }
        return list.ToArray();
    }

    private static string? ResolvePath(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
    }

    private static string ResolvePathWithinRoot(string baseDir, string? value, string defaultRelativePath)
    {
        var normalizedRoot = NormalizeRootPath(baseDir);
        var candidate = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(baseDir, defaultRelativePath)
            : ResolvePath(baseDir, value);
        var resolved = Path.GetFullPath(candidate ?? Path.Combine(baseDir, defaultRelativePath));
        if (!IsPathWithinRoot(normalizedRoot, resolved))
            throw new InvalidOperationException($"Path must resolve under pipeline root: {value}");
        return resolved;
    }

    private static string NormalizeRootPath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static bool IsPathWithinRoot(string normalizedRoot, string candidatePath)
    {
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(normalizedRoot, FileSystemPathComparison);
    }

    private static string[] BuildIgnoreNavPatternsForPipeline(List<string> userPatterns, bool useDefaults)
    {
        if (!useDefaults)
            return userPatterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var defaults = new WebAuditOptions().IgnoreNavFor;
        if (userPatterns.Count == 0)
            return defaults;

        return defaults.Concat(userPatterns)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveSummaryPathForPipeline(bool summaryEnabled, string? summaryPath)
    {
        if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
            return null;

        return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
    }

    private static string? ResolveSarifPathForPipeline(bool sarifEnabled, string? sarifPath)
    {
        if (!sarifEnabled && string.IsNullOrWhiteSpace(sarifPath))
            return null;

        return string.IsNullOrWhiteSpace(sarifPath) ? "audit.sarif.json" : sarifPath;
    }

    private static WebAuditNavProfile[] LoadAuditNavProfilesForPipeline(string baseDir, string? navProfilesPath)
    {
        if (string.IsNullOrWhiteSpace(navProfilesPath))
            return Array.Empty<WebAuditNavProfile>();

        var resolvedPath = ResolvePath(baseDir, navProfilesPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            throw new FileNotFoundException($"Nav profile file not found: {navProfilesPath}", resolvedPath ?? navProfilesPath);

        using var stream = File.OpenRead(resolvedPath);
        var profiles = JsonSerializer.Deserialize(stream, WebCliJson.Context.WebAuditNavProfileArray)
                       ?? Array.Empty<WebAuditNavProfile>();
        return profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Match))
            .ToArray();
    }
}

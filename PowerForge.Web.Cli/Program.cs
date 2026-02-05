using System.Diagnostics;
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
                    if (publishSpec.Optimize.CacheHeadersPaths.Length > 0)
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
                    HashAssets = publishSpec.Optimize.HashAssets,
                    HashExtensions = publishSpec.Optimize.HashExtensions.Length > 0 ? publishSpec.Optimize.HashExtensions : new[] { ".css", ".js" },
                    HashExclude = publishSpec.Optimize.HashExclude,
                    HashManifestPath = publishSpec.Optimize.HashManifest,
                    AssetPolicy = policy
                };
                if (!string.IsNullOrWhiteSpace(publishSpec.Optimize.CssPattern))
                    optimizerOptions.CssLinkPattern = publishSpec.Optimize.CssPattern;

                optimizeUpdated = WebAssetOptimizer.Optimize(optimizerOptions);
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
            var useDefaultExclude = !HasOption(subArgs, "--no-default-exclude");

            var ignoreNavPatterns = BuildIgnoreNavPatterns(ignoreNav, useDefaultIgnoreNav);
            var renderedMaxPages = ParseIntOption(renderedMaxText, 20);
            var renderedTimeoutMs = ParseIntOption(renderedTimeoutText, 30000);
            var renderedPort = ParseIntOption(renderedPortText, 0);
            var summaryMax = ParseIntOption(summaryMaxText, 10);
            var resolvedSummaryPath = ResolveSummaryPath(summaryEnabled, summaryPath);

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
                SummaryMaxIssues = summaryMax
            });

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
            if (result.NavMismatchCount > 0)
                logger.Info($"Nav mismatches: {result.NavMismatchCount}");
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
            if (!string.IsNullOrWhiteSpace(result.SummaryPath))
                logger.Info($"Audit summary: {result.SummaryPath}");

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
            var hashAssets = HasOption(subArgs, "--hash-assets");
            var hashExtensions = ReadOptionList(subArgs, "--hash-ext", "--hash-extensions");
            var hashExclude = ReadOptionList(subArgs, "--hash-exclude");
            var hashManifest = TryGetOptionValue(subArgs, "--hash-manifest");
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

            var updated = WebAssetOptimizer.Optimize(new WebAssetOptimizerOptions
            {
                SiteRoot = siteRoot,
                CriticalCssPath = criticalCss,
                CssLinkPattern = string.IsNullOrWhiteSpace(cssPattern) ? "(app|api-docs)\\.css" : cssPattern,
                MinifyHtml = minifyHtml,
                MinifyCss = minifyCss,
                MinifyJs = minifyJs,
                HashAssets = hashAssets,
                HashExtensions = hashExtensions.Count > 0 ? hashExtensions.ToArray() : new[] { ".css", ".js" },
                HashExclude = hashExclude.Count > 0 ? hashExclude.ToArray() : Array.Empty<string>(),
                HashManifestPath = hashManifest,
                AssetPolicy = policy
            });

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "web.optimize",
                    Success = true,
                    ExitCode = 0,
                    Result = WebCliJson.SerializeToElement(new WebOptimizeResult { UpdatedCount = updated }, WebCliJson.Context.WebOptimizeResult)
                });
                return 0;
            }

            logger.Success($"Optimized HTML files: {updated}");
            return 0;
        }
        case "pipeline":
        {
            var pipelinePath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(pipelinePath))
                return Fail("Missing required --config.", outputJson, logger, "web.pipeline");

            var fullPath = ResolveExistingFilePath(pipelinePath);
            var result = WebPipelineRunner.RunPipeline(fullPath, logger);

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
    Console.WriteLine("  powerforge-web audit --site-root <dir> [--include <glob>] [--exclude <glob>] [--nav-selector <css>]");
    Console.WriteLine("  powerforge-web audit --config <site.json> [--out <path>] [--include <glob>] [--exclude <glob>] [--nav-selector <css>]");
    Console.WriteLine("                     [--no-links] [--no-assets] [--no-nav] [--no-titles] [--no-ids] [--no-structure]");
    Console.WriteLine("                     [--rendered] [--rendered-engine <chromium|firefox|webkit>] [--rendered-max <n>] [--rendered-timeout <ms>]");
    Console.WriteLine("                     [--rendered-headful] [--rendered-base-url <url>] [--rendered-host <host>] [--rendered-port <n>] [--rendered-no-serve]");
    Console.WriteLine("                     [--rendered-no-install]");
    Console.WriteLine("                     [--rendered-no-console-errors] [--rendered-no-console-warnings] [--rendered-no-failures]");
    Console.WriteLine("                     [--rendered-include <glob>] [--rendered-exclude <glob>]");
    Console.WriteLine("                     [--ignore-nav <glob>] [--no-default-ignore-nav] [--nav-ignore-prefix <path>]");
    Console.WriteLine("                     [--nav-optional]");
    Console.WriteLine("                     [--no-default-exclude]");
    Console.WriteLine("                     [--summary] [--summary-path <file>] [--summary-max <n>]");
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
    Console.WriteLine("                     [--hash-assets] [--hash-ext <.css,.js>] [--hash-exclude <glob[,glob]>] [--hash-manifest <file>]");
    Console.WriteLine("                     [--headers] [--headers-out <file>] [--headers-html <value>] [--headers-assets <value>]");
    Console.WriteLine("  powerforge-web dotnet-build --project <path> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--no-restore]");
    Console.WriteLine("  powerforge-web dotnet-publish --project <path> --out <dir> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--define-constants <list>]");
    Console.WriteLine("                     [--self-contained] [--no-build] [--no-restore] [--base-href <path>] [--no-blazor-fixes]");
    Console.WriteLine("  powerforge-web overlay --source <dir> --destination <dir> [--include <glob[,glob...]>] [--exclude <glob[,glob...]>]");
    Console.WriteLine("  powerforge-web pipeline --config <pipeline.json>");
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

static string? ResolveSummaryPath(bool summaryEnabled, string? summaryPath)
{
    if (!summaryEnabled && string.IsNullOrWhiteSpace(summaryPath))
        return null;

    return string.IsNullOrWhiteSpace(summaryPath) ? "audit-summary.json" : summaryPath;
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

internal sealed class WebConsoleLogger
{
    public void Info(string message) => Console.WriteLine($"ℹ  {message}");
    public void Success(string message) => Console.WriteLine($"✅ {message}");
    public void Warn(string message) => Console.WriteLine($"⚠️ {message}");
    public void Error(string message) => Console.WriteLine($"❌ {message}");
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

internal static class WebPipelineRunner
{
    internal static WebPipelineResult RunPipeline(string pipelinePath, WebConsoleLogger? logger)
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
        var result = new WebPipelineResult();
        var steps = new List<JsonElement>();
        var totalSteps = 0;
        foreach (var step in stepsElement.EnumerateArray())
        {
            steps.Add(step);
            var taskName = GetString(step, "task");
            if (!string.IsNullOrWhiteSpace(taskName))
                totalSteps++;
        }

        var stepIndex = 0;
        foreach (var step in steps)
        {
            var task = GetString(step, "task")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(task))
                continue;

            stepIndex++;
            var label = $"[{stepIndex}/{totalSteps}] {task}";
            logger?.Info($"Starting {label}...");
            var stopwatch = Stopwatch.StartNew();
            var stepResult = new WebPipelineStepResult { Task = task };
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
                        var hashAssets = GetBool(step, "hashAssets") ?? false;
                        var hashExtensions = GetArrayOfStrings(step, "hashExtensions") ?? GetArrayOfStrings(step, "hash-ext");
                        var hashExclude = GetArrayOfStrings(step, "hashExclude") ?? GetArrayOfStrings(step, "hash-exclude");
                        var hashManifest = GetString(step, "hashManifest") ?? GetString(step, "hash-manifest");
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

                        var updated = WebAssetOptimizer.Optimize(new WebAssetOptimizerOptions
                        {
                            SiteRoot = siteRoot,
                            CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
                            CssLinkPattern = GetString(step, "cssPattern") ?? "(app|api-docs)\\.css",
                            MinifyHtml = minifyHtml,
                            MinifyCss = minifyCss,
                            MinifyJs = minifyJs,
                            HashAssets = hashAssets,
                            HashExtensions = hashExtensions ?? new[] { ".css", ".js" },
                            HashExclude = hashExclude ?? Array.Empty<string>(),
                            HashManifestPath = hashManifest,
                            AssetPolicy = policy
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"Optimized {updated} files";
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
                        var navSelector = GetString(step, "navSelector") ?? GetString(step, "nav-selector") ?? "nav";
                        var navRequired = GetBool(step, "navRequired");
                        var navOptional = GetBool(step, "navOptional");
                        var checkLinks = GetBool(step, "checkLinks") ?? true;
                        var checkAssets = GetBool(step, "checkAssets") ?? true;
                        var checkNav = GetBool(step, "checkNav") ?? true;
                        var checkTitles = GetBool(step, "checkTitles") ?? true;
                        var checkIds = GetBool(step, "checkDuplicateIds") ?? true;
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
                        var useDefaultExclude = !(GetBool(step, "noDefaultExclude") ?? false);
                        var useDefaultIgnoreNav = !(GetBool(step, "noDefaultIgnoreNav") ?? false);
                        var ignoreNavList = CliPatternHelper.SplitPatterns(ignoreNav).ToList();
                        var ignoreNavPatterns = BuildIgnoreNavPatternsForPipeline(ignoreNavList, useDefaultIgnoreNav);
                        var navRequiredValue = navRequired ?? !(navOptional ?? false);
                        var navIgnorePrefixList = CliPatternHelper.SplitPatterns(navIgnorePrefixes);

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
                            CheckLinks = checkLinks,
                            CheckAssets = checkAssets,
                            CheckNavConsistency = checkNav,
                            CheckTitles = checkTitles,
                            CheckDuplicateIds = checkIds,
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
                            SummaryMaxIssues = summaryMax
                        });

                        stepResult.Success = audit.Success;
                        stepResult.Message = audit.Success
                            ? "Audit ok"
                            : $"Audit failed ({audit.Errors.Length} errors)";
                        if (!audit.Success)
                            throw new InvalidOperationException(stepResult.Message);
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
                        var configuration = GetString(step, "configuration");
                        var framework = GetString(step, "framework");
                        var runtime = GetString(step, "runtime");
                        var selfContained = GetBool(step, "selfContained") ?? false;
                        var noBuild = GetBool(step, "noBuild") ?? false;
                        var noRestore = GetBool(step, "noRestore") ?? false;
                        var baseHref = GetString(step, "baseHref");
                        var defineConstants = GetString(step, "defineConstants") ?? GetString(step, "define-constants");
                        var blazorFixes = GetBool(step, "blazorFixes") ?? true;

                        if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("dotnet-publish requires project and out.");

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
                result.Steps.Add(stepResult);
                result.StepCount = result.Steps.Count;
                result.Success = false;
                return result;
            }

            stepResult.Message = AppendDuration(stepResult.Message, stopwatch);
            result.Steps.Add(stepResult);
        }

        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        return result;
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
}

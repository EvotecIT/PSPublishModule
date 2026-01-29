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

            if (string.IsNullOrWhiteSpace(configPath))
                return Fail("Missing required --config.", outputJson, logger, "web.build");
            if (string.IsNullOrWhiteSpace(outPath))
                return Fail("Missing required --out.", outputJson, logger, "web.build");

            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
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
                NoRestore = publishSpec.Publish.NoRestore
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

                var optimizerOptions = new WebAssetOptimizerOptions
                {
                    SiteRoot = optimizeRoot,
                    CriticalCssPath = string.IsNullOrWhiteSpace(publishSpec.Optimize.CriticalCss)
                        ? null
                        : ResolvePathRelative(baseDir, publishSpec.Optimize.CriticalCss),
                    MinifyHtml = publishSpec.Optimize.MinifyHtml,
                    MinifyCss = publishSpec.Optimize.MinifyCss,
                    MinifyJs = publishSpec.Optimize.MinifyJs
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
            var xmlPath = TryGetOptionValue(subArgs, "--xml");
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
            var includeNamespaces = ReadOptionList(subArgs, "--include-namespace", "--namespace-prefix");
            var excludeNamespaces = ReadOptionList(subArgs, "--exclude-namespace");

            if (string.IsNullOrWhiteSpace(xmlPath))
                return Fail("Missing required --xml.", outputJson, logger, "web.apidocs");
            if (string.IsNullOrWhiteSpace(outPath))
                return Fail("Missing required --out.", outputJson, logger, "web.apidocs");

            var options = new WebApiDocsOptions
            {
                XmlPath = xmlPath,
                AssemblyPath = assemblyPath,
                OutputPath = outPath,
                Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
                BaseUrl = baseUrl,
                Format = format,
                CssHref = cssHref,
                HeaderHtmlPath = headerHtml,
                FooterHtmlPath = footerHtml
            };
            if (includeNamespaces.Count > 0)
                options.IncludeNamespacePrefixes.AddRange(includeNamespaces);
            if (excludeNamespaces.Count > 0)
                options.ExcludeNamespacePrefixes.AddRange(excludeNamespaces);

            var result = WebApiDocsGenerator.Generate(options);

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
        case "optimize":
        {
            var siteRoot = TryGetOptionValue(subArgs, "--site-root") ??
                           TryGetOptionValue(subArgs, "--root") ??
                           TryGetOptionValue(subArgs, "--path");
            var criticalCss = TryGetOptionValue(subArgs, "--critical-css");
            var cssPattern = TryGetOptionValue(subArgs, "--css-pattern");
            var minifyHtml = subArgs.Any(a => a.Equals("--minify-html", StringComparison.OrdinalIgnoreCase));
            var minifyCss = subArgs.Any(a => a.Equals("--minify-css", StringComparison.OrdinalIgnoreCase));
            var minifyJs = subArgs.Any(a => a.Equals("--minify-js", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.optimize");

            var updated = WebAssetOptimizer.Optimize(new WebAssetOptimizerOptions
            {
                SiteRoot = siteRoot,
                CriticalCssPath = criticalCss,
                CssLinkPattern = string.IsNullOrWhiteSpace(cssPattern) ? "(app|api-docs)\\.css" : cssPattern,
                MinifyHtml = minifyHtml,
                MinifyCss = minifyCss,
                MinifyJs = minifyJs
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
            var result = WebPipelineRunner.RunPipeline(fullPath);

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
                NoRestore = noRestore
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

            if (string.IsNullOrWhiteSpace(source))
                return Fail("Missing required --source.", outputJson, logger, "web.overlay");
            if (string.IsNullOrWhiteSpace(destination))
                return Fail("Missing required --destination.", outputJson, logger, "web.overlay");

            var result = WebStaticOverlay.Apply(new WebStaticOverlayOptions
            {
                SourceRoot = source,
                DestinationRoot = destination,
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

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.llms");

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
                ExtraContentPath = extra
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
    Console.WriteLine("  powerforge-web build --config <site.json> --out <path> [--output json]");
    Console.WriteLine("  powerforge-web publish --config <publish.json> [--output json]");
    Console.WriteLine("  powerforge-web verify --config <site.json> [--output json]");
    Console.WriteLine("  powerforge-web scaffold --out <path> [--name <SiteName>] [--base-url <url>] [--engine simple|scriban] [--output json]");
    Console.WriteLine("  powerforge-web new --config <site.json> --title <Title> [--collection <name>] [--slug <slug>] [--out <path>]");
    Console.WriteLine("  powerforge-web serve --path <dir> [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web serve --config <site.json> [--out <path>] [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web apidocs --xml <file> --out <dir> [--assembly <file>] [--title <text>] [--base-url <url>] [--include-namespace <prefix[,prefix]>] [--exclude-namespace <prefix[,prefix]>]");
    Console.WriteLine("                     [--format json|hybrid] [--css <href>] [--header-html <file>] [--footer-html <file>]");
    Console.WriteLine("  powerforge-web optimize --site-root <dir> [--critical-css <file>] [--css-pattern <regex>]");
    Console.WriteLine("                     [--minify-html] [--minify-css] [--minify-js]");
    Console.WriteLine("  powerforge-web dotnet-build --project <path> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>] [--no-restore]");
    Console.WriteLine("  powerforge-web dotnet-publish --project <path> --out <dir> [--configuration <cfg>] [--framework <tfm>] [--runtime <rid>]");
    Console.WriteLine("                     [--self-contained] [--no-build] [--no-restore] [--base-href <path>] [--no-blazor-fixes]");
    Console.WriteLine("  powerforge-web overlay --source <dir> --destination <dir> [--include <glob[,glob...]>] [--exclude <glob[,glob...]>]");
    Console.WriteLine("  powerforge-web pipeline --config <pipeline.json>");
    Console.WriteLine("  powerforge-web llms --site-root <dir> [--project <path>] [--api-index <path>] [--api-base /api]");
    Console.WriteLine("                     [--name <Name>] [--package <Id>] [--version <X.Y.Z>] [--quickstart <file>]");
    Console.WriteLine("                     [--overview <text>] [--license <text>] [--targets <text>] [--extra <file>]");
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
    internal static WebPipelineResult RunPipeline(string pipelinePath)
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

        foreach (var step in stepsElement.EnumerateArray())
        {
            var task = GetString(step, "task")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(task))
                continue;

            var stepResult = new WebPipelineStepResult { Task = task };
            try
            {
                switch (task)
                {
                    case "build":
                    {
                        var config = ResolvePath(baseDir, GetString(step, "config"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        if (string.IsNullOrWhiteSpace(config) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("build requires config and out.");

                        var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(config, WebCliJson.Options);
                        var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
                        var build = WebSiteBuilder.Build(spec, plan, outPath, WebCliJson.Options);
                        stepResult.Success = true;
                        stepResult.Message = $"Built {build.OutputPath}";
                        break;
                    }
                    case "apidocs":
                    {
                        var xml = ResolvePath(baseDir, GetString(step, "xml"));
                        var outPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output"));
                        var assembly = ResolvePath(baseDir, GetString(step, "assembly"));
                        var title = GetString(step, "title");
                        var baseUrl = GetString(step, "baseUrl") ?? GetString(step, "base-url") ?? "/api";
                        var format = GetString(step, "format");
                        var css = GetString(step, "css");
                        var header = ResolvePath(baseDir, GetString(step, "headerHtml") ?? GetString(step, "header-html"));
                        var footer = ResolvePath(baseDir, GetString(step, "footerHtml") ?? GetString(step, "footer-html"));
                        if (string.IsNullOrWhiteSpace(xml) || string.IsNullOrWhiteSpace(outPath))
                            throw new InvalidOperationException("apidocs requires xml and out.");

                        var res = WebApiDocsGenerator.Generate(new WebApiDocsOptions
                        {
                            XmlPath = xml,
                            AssemblyPath = assembly,
                            OutputPath = outPath,
                            Title = string.IsNullOrWhiteSpace(title) ? "API Reference" : title,
                            BaseUrl = baseUrl,
                            Format = format,
                            CssHref = css,
                            HeaderHtmlPath = header,
                            FooterHtmlPath = footer
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"API docs {res.TypeCount} types";
                        break;
                    }
                    case "llms":
                    {
                        var siteRoot = ResolvePath(baseDir, GetString(step, "siteRoot") ?? GetString(step, "site-root"));
                        if (string.IsNullOrWhiteSpace(siteRoot))
                            throw new InvalidOperationException("llms requires siteRoot.");

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
                            ExtraContentPath = ResolvePath(baseDir, GetString(step, "extra"))
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

                        var minifyHtml = GetBool(step, "minifyHtml") ?? false;
                        var minifyCss = GetBool(step, "minifyCss") ?? false;
                        var minifyJs = GetBool(step, "minifyJs") ?? false;
                        var updated = WebAssetOptimizer.Optimize(new WebAssetOptimizerOptions
                        {
                            SiteRoot = siteRoot,
                            CriticalCssPath = ResolvePath(baseDir, GetString(step, "criticalCss") ?? GetString(step, "critical-css")),
                            CssLinkPattern = GetString(step, "cssPattern") ?? "(app|api-docs)\\.css",
                            MinifyHtml = minifyHtml,
                            MinifyCss = minifyCss,
                            MinifyJs = minifyJs
                        });
                        stepResult.Success = true;
                        stepResult.Message = $"Optimized {updated} files";
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
                        stepResult.Success = res.Success;
                        stepResult.Message = res.Success ? "dotnet build ok" : res.Error;
                        if (!res.Success) throw new InvalidOperationException(res.Error);
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
                            NoRestore = noRestore
                        });

                        if (!res.Success) throw new InvalidOperationException(res.Error);
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
                        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                            throw new InvalidOperationException("overlay requires source and destination.");

                        var res = WebStaticOverlay.Apply(new WebStaticOverlayOptions
                        {
                            SourceRoot = source,
                            DestinationRoot = destination,
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
                stepResult.Message = ex.Message;
                result.Steps.Add(stepResult);
                result.StepCount = result.Steps.Count;
                result.Success = false;
                return result;
            }

            result.Steps.Add(stepResult);
        }

        result.StepCount = result.Steps.Count;
        result.Success = result.Steps.All(s => s.Success);
        return result;
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
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;
using static PowerForge.Web.Cli.WebCliHelpers;

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
            return WebCliCommandHandlers.HandlePublish(subArgs, outputJson, logger, OutputSchemaVersion);
        case "verify":
        {
            var configPath = TryGetOptionValue(subArgs, "--config");
            if (string.IsNullOrWhiteSpace(configPath))
                return Fail("Missing required --config.", outputJson, logger, "web.verify");

            var fullConfigPath = ResolveExistingFilePath(configPath);
            var (spec, specPath) = WebSiteSpecLoader.LoadWithPath(fullConfigPath, WebCliJson.Options);
            var failOnWarnings = HasOption(subArgs, "--fail-on-warnings") || (spec.Verify?.FailOnWarnings ?? false);
            var failOnNavLint = HasOption(subArgs, "--fail-on-nav-lint") || (spec.Verify?.FailOnNavLint ?? false);
            var failOnThemeContract = HasOption(subArgs, "--fail-on-theme-contract") || (spec.Verify?.FailOnThemeContract ?? false);
            var plan = WebSitePlanner.Plan(spec, specPath, WebCliJson.Options);
            var verify = WebSiteVerifier.Verify(spec, plan);
            var (verifySuccess, verifyPolicyFailures) = WebVerifyPolicy.EvaluateOutcome(
                verify,
                failOnWarnings,
                failOnNavLint,
                failOnThemeContract);

            if (outputJson)
            {
                WebCliJsonWriter.Write(new WebCliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
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

            if (verifyPolicyFailures.Length > 0)
            {
                foreach (var policy in verifyPolicyFailures)
                    logger.Error($"verify-policy: {policy}");
            }

            if (verifySuccess)
                logger.Success("Web verify passed.");

            return verifySuccess ? 0 : 1;
        }
        case "doctor":
            return WebCliCommandHandlers.HandleDoctor(subArgs, outputJson, logger, OutputSchemaVersion);
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
            return WebCliCommandHandlers.HandleAudit(subArgs, outputJson, logger, OutputSchemaVersion);
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
            return WebCliCommandHandlers.HandleOptimize(subArgs, outputJson, logger, OutputSchemaVersion);
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


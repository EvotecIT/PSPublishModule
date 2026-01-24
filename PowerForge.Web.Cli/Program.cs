using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

const int OutputSchemaVersion = 1;

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

            if (string.IsNullOrWhiteSpace(xmlPath))
                return Fail("Missing required --xml.", outputJson, logger, "web.apidocs");
            if (string.IsNullOrWhiteSpace(outPath))
                return Fail("Missing required --out.", outputJson, logger, "web.apidocs");

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
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
            });

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

            if (string.IsNullOrWhiteSpace(siteRoot))
                return Fail("Missing required --site-root.", outputJson, logger, "web.sitemap");
            if (string.IsNullOrWhiteSpace(baseUrl))
                return Fail("Missing required --base-url.", outputJson, logger, "web.sitemap");

            var result = WebSitemapGenerator.Generate(new WebSitemapOptions
            {
                SiteRoot = siteRoot,
                BaseUrl = baseUrl,
                OutputPath = outputPath,
                ApiSitemapPath = apiSitemap
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
    Console.WriteLine("  powerforge-web verify --config <site.json> [--output json]");
    Console.WriteLine("  powerforge-web scaffold --out <path> [--name <SiteName>] [--base-url <url>] [--engine simple|scriban] [--output json]");
    Console.WriteLine("  powerforge-web serve --path <dir> [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web serve --config <site.json> [--out <path>] [--port 8080] [--host localhost]");
    Console.WriteLine("  powerforge-web apidocs --xml <file> --out <dir> [--assembly <file>] [--title <text>] [--base-url <url>]");
    Console.WriteLine("                     [--format json|hybrid] [--css <href>] [--header-html <file>] [--footer-html <file>]");
    Console.WriteLine("  powerforge-web optimize --site-root <dir> [--critical-css <file>] [--css-pattern <regex>]");
    Console.WriteLine("                     [--minify-html] [--minify-css] [--minify-js]");
    Console.WriteLine("  powerforge-web pipeline --config <pipeline.json>");
    Console.WriteLine("  powerforge-web llms --site-root <dir> [--project <path>] [--api-index <path>] [--api-base /api]");
    Console.WriteLine("                     [--name <Name>] [--package <Id>] [--version <X.Y.Z>] [--quickstart <file>]");
    Console.WriteLine("                     [--overview <text>] [--license <text>] [--targets <text>] [--extra <file>]");
    Console.WriteLine("  powerforge-web sitemap --site-root <dir> --base-url <url> [--api-sitemap <path>] [--out <file>]");
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

static string ResolveExistingFilePath(string path)
{
    var full = Path.GetFullPath(path.Trim().Trim('"'));
    if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");
    return full;
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

                        var res = WebSitemapGenerator.Generate(new WebSitemapOptions
                        {
                            SiteRoot = siteRoot,
                            BaseUrl = baseUrl,
                            OutputPath = ResolvePath(baseDir, GetString(step, "out") ?? GetString(step, "output")),
                            ApiSitemapPath = ResolvePath(baseDir, GetString(step, "apiSitemap") ?? GetString(step, "api-sitemap"))
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

    private static string? ResolvePath(string baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value);
    }
}

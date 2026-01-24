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

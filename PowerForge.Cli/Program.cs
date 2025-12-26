using PowerForge;
using System.Text.Json;

var logger = new ConsoleLogger { IsVerbose = args.Contains("-Verbose", StringComparer.OrdinalIgnoreCase) };
var forge = new PowerForgeFacade(logger);

if (args.Length == 0 || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
{
    PrintHelp();
    return 0;
}

var cmd = args[0].ToLowerInvariant();
switch (cmd)
{
    case "build":
    {
        var argv = args.Skip(1).ToArray();
        var configPath = TryGetOptionValue(argv, "--config");
        var outputJson = IsJsonOutput(argv);

        ModuleBuildSpec spec;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            spec = LoadJson<ModuleBuildSpec>(configPath);
        }
        else
        {
            var parsed = ParseBuildArgs(argv);
            if (parsed is null) { PrintHelp(); return 2; }
            var p = parsed.Value;
            spec = new ModuleBuildSpec
            {
                Name = p.Name,
                SourcePath = p.Source,
                StagingPath = p.Staging,
                CsprojPath = p.Csproj,
                Version = p.Version,
                Configuration = p.Configuration,
                Frameworks = p.Frameworks,
                Author = p.Author,
                CompanyName = p.CompanyName,
                Description = p.Description,
                Tags = p.Tags,
                IconUri = p.IconUri,
                ProjectUri = p.ProjectUri,
            };
        }

        try
        {
            ILogger cmdLogger = outputJson ? new BufferingLogger { IsVerbose = logger.IsVerbose } : logger;
            var pipeline = new ModuleBuildPipeline(cmdLogger);
            var res = pipeline.BuildToStaging(spec);

            if (outputJson)
            {
                WriteJson(new
                {
                    command = "build",
                    success = true,
                    exitCode = 0,
                    spec,
                    result = res,
                    logs = ((BufferingLogger)cmdLogger).Entries
                });
                return 0;
            }

            logger.Success($"Built staging for {spec.Name} {spec.Version} at {res.StagingPath}");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { command = "build", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
    case "normalize":
    {
        var argv = args.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);

        var targets = ParseTargets(argv);
        if (targets.Length == 0) { Console.WriteLine("Usage: powerforge normalize <files...>"); return 2; }

        if (outputJson)
        {
            var results = new List<NormalizationResult>();
            foreach (var f in targets) results.Add(forge.Normalize(f));
            WriteJson(new { command = "normalize", success = true, exitCode = 0, results });
            return 0;
        }

        foreach (var f in targets)
        {
            var res = forge.Normalize(f);
            var msg = res.Changed ? $"Normalized {res.Path} ({res.Replacements} changes, {res.EncodingName})" : $"No changes: {res.Path}";
            logger.Info(msg);
        }
        return 0;
    }
    case "format":
    {
        var argv = args.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);

        var targets = ParseTargets(argv);
        if (targets.Length == 0) { Console.WriteLine("Usage: powerforge format <files...>"); return 2; }

        if (outputJson)
        {
            var cmdLogger = new BufferingLogger { IsVerbose = logger.IsVerbose };
            var jsonForge = new PowerForgeFacade(cmdLogger);
            var jsonResults = jsonForge.Format(targets);
            WriteJson(new { command = "format", success = true, exitCode = 0, results = jsonResults, logs = cmdLogger.Entries });
            return 0;
        }

        var results = forge.Format(targets);
        foreach (var r in results)
        {
            var prefix = r.Changed ? "Formatted" : "Unchanged";
            logger.Info($"{prefix}: {r.Path} ({r.Message})");
        }
        return 0;
    }
    case "install":
    {
        var argv = args.Skip(1).ToArray();
        var configPath = TryGetOptionValue(argv, "--config");
        var outputJson = IsJsonOutput(argv);

        ModuleInstallSpec spec;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            spec = LoadJson<ModuleInstallSpec>(configPath);
        }
        else
        {
            var parsed = ParseInstallArgs(argv);
            if (parsed is null) { PrintHelp(); return 2; }
            var p = parsed.Value;
            spec = new ModuleInstallSpec
            {
                Name = p.Name,
                Version = p.Version,
                StagingPath = p.Staging,
                Strategy = p.Strategy,
                KeepVersions = p.Keep,
                Roots = p.Roots,
            };
        }

        try
        {
            ILogger cmdLogger = outputJson ? new BufferingLogger { IsVerbose = logger.IsVerbose } : logger;
            var pipeline = new ModuleBuildPipeline(cmdLogger);
            var res = pipeline.InstallFromStaging(spec);

            if (outputJson)
            {
                WriteJson(new
                {
                    command = "install",
                    success = true,
                    exitCode = 0,
                    spec,
                    result = res,
                    logs = ((BufferingLogger)cmdLogger).Entries
                });
                return 0;
            }

            logger.Success($"Installed {spec.Name} {res.Version}");
            foreach (var path in res.InstalledPaths) logger.Info($" → {path}");
            if (res.PrunedPaths.Count > 0) logger.Warn($"Pruned versions: {res.PrunedPaths.Count}");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { command = "install", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
    case "find":
    {
        var argv = args.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        var parsed = ParseFindArgs(argv);
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;

        try
        {
            ILogger cmdLogger = outputJson ? new BufferingLogger { IsVerbose = logger.IsVerbose } : logger;
            var runner = new PowerShellRunner();
            var client = new PSResourceGetClient(runner, cmdLogger);
            var opts = new PSResourceFindOptions(p.Names, p.Version, p.Prerelease, p.Repositories);
            var results = client.Find(opts);

            if (outputJson)
            {
                WriteJson(new
                {
                    command = "find",
                    success = true,
                    exitCode = 0,
                    results,
                    logs = ((BufferingLogger)cmdLogger).Entries
                });
                return 0;
            }

            foreach (var r in results)
            {
                Console.WriteLine($"{r.Name}\t{r.Version}\t{r.Repository ?? string.Empty}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { command = "find", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
    case "publish":
    {
        var argv = args.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        var parsed = ParsePublishArgs(argv);
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;

        try
        {
            ILogger cmdLogger = outputJson ? new BufferingLogger { IsVerbose = logger.IsVerbose } : logger;
            var runner = new PowerShellRunner();
            var client = new PSResourceGetClient(runner, cmdLogger);
            var opts = new PSResourcePublishOptions(
                path: Path.GetFullPath(p.Path.Trim().Trim('"')),
                isNupkg: p.IsNupkg,
                repository: p.Repository,
                apiKey: p.ApiKey,
                destinationPath: p.DestinationPath,
                skipDependenciesCheck: p.SkipDependenciesCheck,
                skipModuleManifestValidate: p.SkipModuleManifestValidate);      
            client.Publish(opts);

            if (outputJson)
            {
                WriteJson(new
                {
                    command = "publish",
                    success = true,
                    exitCode = 0,
                    path = opts.Path,
                    isNupkg = opts.IsNupkg,
                    repository = opts.Repository,
                    destinationPath = opts.DestinationPath,
                    skipDependenciesCheck = opts.SkipDependenciesCheck,
                    skipModuleManifestValidate = opts.SkipModuleManifestValidate,
                    logs = ((BufferingLogger)cmdLogger).Entries
                });
                return 0;
            }

            logger.Success($"Published {opts.Path}{(string.IsNullOrWhiteSpace(p.Repository) ? string.Empty : " to " + p.Repository)}");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new { command = "publish", success = false, exitCode = 1, error = ex.Message });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
    default:
        PrintHelp();
        return 2;
}

static void PrintHelp()
{
    Console.WriteLine(@"PowerForge CLI
Usage:
  powerforge build --name <ModuleName> --project-root <path> --version <X.Y.Z> [--csproj <path>] [--staging <path>] [--configuration Release] [--framework <tfm>]* [--author <name>] [--company <name>] [--description <text>] [--tag <tag>]*
  powerforge build --config <BuildSpec.json>
  powerforge normalize <files...>   Normalize encodings and line endings [--output json]
  powerforge format <files...>      Format scripts via PSScriptAnalyzer (out-of-proc) [--output json]
  powerforge install --name <ModuleName> --version <X.Y.Z> --staging <path> [--strategy exact|autorevision] [--keep N] [--root path]*
  powerforge install --config <InstallSpec.json>
  powerforge find --name <Name>[,<Name>...] [--repo <Repo>] [--version <X.Y.Z>] [--prerelease]
  powerforge publish --path <Path> [--repo <Repo>] [--apikey <Key>] [--nupkg] [--destination <Path>] [--skip-dependencies-check] [--skip-manifest-validate]
  powerforge -Verbose               Enable verbose diagnostics
  --output json                     Emit machine-readable JSON output
");
}

static (string Name, string Source, string? Staging, string? Csproj, string Version, string Configuration, string[] Frameworks, string? Author, string? CompanyName, string? Description, string[] Tags, string? IconUri, string? ProjectUri)? ParseBuildArgs(string[] argv)
{
    string? name = null, source = null, staging = null, csproj = null, version = null;
    string configuration = "Release";
    var frameworks = new List<string>();
    string? author = null, companyName = null, description = null, iconUri = null, projectUri = null;
    var tags = new List<string>();

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--name": name = ++i < argv.Length ? argv[i] : null; break;
            case "--project-root":
            case "--source": source = ++i < argv.Length ? argv[i] : null; break;
            case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
            case "--csproj": csproj = ++i < argv.Length ? argv[i] : null; break;
            case "--version": version = ++i < argv.Length ? argv[i] : null; break;
            case "--configuration": configuration = ++i < argv.Length ? argv[i] : configuration; break;
            case "--config": i++; break; // handled before ParseBuildArgs
            case "--output": i++; break; // handled elsewhere
            case "--framework":
                if (++i < argv.Length)
                {
                    foreach (var f in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        frameworks.Add(f.Trim());
                }
                break;
            case "--author": author = ++i < argv.Length ? argv[i] : null; break;
            case "--company": companyName = ++i < argv.Length ? argv[i] : null; break;
            case "--description": description = ++i < argv.Length ? argv[i] : null; break;
            case "--tag": if (++i < argv.Length) tags.Add(argv[i]); break;
            case "--tags":
                if (++i < argv.Length)
                {
                    foreach (var t in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        tags.Add(t.Trim());
                }
                break;
            case "--icon-uri": iconUri = ++i < argv.Length ? argv[i] : null; break;
            case "--project-uri": projectUri = ++i < argv.Length ? argv[i] : null; break;
        }
    }

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(version))
        return null;

    var tfms = frameworks.Count > 0 ? frameworks.ToArray() : new[] { "net472", "net8.0" };
    return (name!, source!, staging, csproj, version!, configuration, tfms, author, companyName, description, tags.ToArray(), iconUri, projectUri);
}

static (string Name, string Version, string Staging, string[] Roots, PowerForge.InstallationStrategy Strategy, int Keep)? ParseInstallArgs(string[] argv)
{
    string? name = null, version = null, staging = null; var roots = new List<string>();
    var strategy = PowerForge.InstallationStrategy.Exact; int keep = 3;
    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        switch (a.ToLowerInvariant())
        {
            case "--name": name = ++i < argv.Length ? argv[i] : null; break;
            case "--version": version = ++i < argv.Length ? argv[i] : null; break;
            case "--staging": staging = ++i < argv.Length ? argv[i] : null; break;
            case "--root": if (++i < argv.Length) roots.Add(argv[i]); break;
            case "--strategy":
                var s = ++i < argv.Length ? argv[i] : null;
                if (string.Equals(s, "autorevision", StringComparison.OrdinalIgnoreCase)) strategy = PowerForge.InstallationStrategy.AutoRevision;
                else strategy = PowerForge.InstallationStrategy.Exact;
                break;
            case "--keep":
                if (++i < argv.Length && int.TryParse(argv[i], out var k)) keep = k;
                break;
            case "--config": i++; break; // handled before ParseInstallArgs
            case "--output": i++; break; // handled elsewhere
        }
    }
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(staging))
        return null;
    return (name!, version!, staging!, roots.ToArray(), strategy, keep);
}

static string? TryGetOptionValue(string[] argv, string optionName)
{
    for (int i = 0; i < argv.Length; i++)
    {
        if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
        return ++i < argv.Length ? argv[i] : null;
    }
    return null;
}

static T LoadJson<T>(string path)
{
    var full = Path.GetFullPath(path.Trim().Trim('"'));
    if (!File.Exists(full)) throw new FileNotFoundException($"Config file not found: {full}");

    var json = File.ReadAllText(full);
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    var obj = JsonSerializer.Deserialize<T>(json, options);
    if (obj is null) throw new InvalidOperationException($"Failed to deserialize config file: {full}");
    return obj;
}

static (string[] Names, string? Version, bool Prerelease, string[] Repositories)? ParseFindArgs(string[] argv)
{
    var names = new List<string>();
    var repos = new List<string>();
    string? version = null;
    bool prerelease = false;

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--name":
                if (++i < argv.Length)
                {
                    foreach (var n in argv[i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        names.Add(n.Trim());
                }
                break;
            case "--output":
                i++;
                break;
            case "--output-json":
            case "--json":
                break;
            case "--repo":
            case "--repository":
                if (++i < argv.Length) repos.Add(argv[i]);
                break;
            case "--version":
                version = ++i < argv.Length ? argv[i] : null;
                break;
            case "--prerelease":
                prerelease = true;
                break;
        }
    }

    if (names.Count == 0) return null;
    return (names.ToArray(), version, prerelease, repos.ToArray());
}

static (string Path, string? Repository, string? ApiKey, bool IsNupkg, string? DestinationPath, bool SkipDependenciesCheck, bool SkipModuleManifestValidate)? ParsePublishArgs(string[] argv)
{
    string? path = null, repo = null, apiKey = null, destination = null;        
    bool isNupkg = false, skipDeps = false, skipManifest = false;

    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        switch (a.ToLowerInvariant())
        {
            case "--path":
                path = ++i < argv.Length ? argv[i] : null;
                break;
            case "--output":
                i++;
                break;
            case "--output-json":
            case "--json":
                break;
            case "--repo":
            case "--repository":
                repo = ++i < argv.Length ? argv[i] : null;
                break;
            case "--apikey":
            case "--api-key":
                apiKey = ++i < argv.Length ? argv[i] : null;
                break;
            case "--destination":
            case "--destination-path":
                destination = ++i < argv.Length ? argv[i] : null;
                break;
            case "--nupkg":
                isNupkg = true;
                break;
            case "--skip-dependencies-check":
                skipDeps = true;
                break;
            case "--skip-manifest-validate":
            case "--skip-module-manifest-validate":
                skipManifest = true;
                break;
        }
    }

    if (string.IsNullOrWhiteSpace(path)) return null;
    return (path!, repo, apiKey, isNupkg, destination, skipDeps, skipManifest);  
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

static string[] ParseTargets(string[] argv)
{
    var list = new List<string>();
    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            continue;

        if (a.Equals("--output", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            continue;
        }

        if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            continue;

        list.Add(a);
    }

    return list.ToArray();
}

static void WriteJson(object obj)
{
    var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });
    Console.WriteLine(json);
}

sealed class LogEntry
{
    public string Level { get; }
    public string Message { get; }

    public LogEntry(string level, string message)
    {
        Level = level;
        Message = message;
    }
}

sealed class BufferingLogger : ILogger
{
    public bool IsVerbose { get; set; }

    public List<LogEntry> Entries { get; } = new();

    public void Info(string message) => Entries.Add(new LogEntry("info", message));
    public void Success(string message) => Entries.Add(new LogEntry("success", message));
    public void Warn(string message) => Entries.Add(new LogEntry("warn", message));
    public void Error(string message) => Entries.Add(new LogEntry("error", message));
    public void Verbose(string message)
    {
        if (!IsVerbose) return;
        Entries.Add(new LogEntry("verbose", message));
    }
}

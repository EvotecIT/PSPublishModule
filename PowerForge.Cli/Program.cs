using PowerForge;

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
        var parsed = ParseBuildArgs(args.Skip(1).ToArray());
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;

        var source = Path.GetFullPath(p.Source.Trim().Trim('"'));
        var staging = string.IsNullOrWhiteSpace(p.Staging)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "build", $"{p.Name}_{Guid.NewGuid():N}")
            : Path.GetFullPath(p.Staging.Trim().Trim('"'));

        if (!Directory.Exists(source))
        {
            logger.Error($"Source directory not found: {source}");
            return 2;
        }

        if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any())
        {
            logger.Error($"Staging directory already exists and is not empty: {staging}");
            return 2;
        }

        Directory.CreateDirectory(staging);
        CopyDirectoryFiltered(source, staging, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".vscode", "bin", "obj", "packages", "node_modules", "Artefacts"
        });

        var builder = new PowerForge.ModuleBuilder(logger);
        builder.BuildInPlace(new PowerForge.ModuleBuilder.Options
        {
            ProjectRoot = staging,
            ModuleName = p.Name,
            CsprojPath = Path.GetFullPath(p.Csproj.Trim().Trim('"')),
            ModuleVersion = p.Version,
            Configuration = p.Configuration,
            Frameworks = p.Frameworks,
            Author = p.Author,
            CompanyName = p.CompanyName,
            Description = p.Description,
            Tags = p.Tags,
            IconUri = p.IconUri,
            ProjectUri = p.ProjectUri,
        });

        logger.Success($"Built staging for {p.Name} {p.Version} at {staging}");
        return 0;
    }
    case "normalize":
    {
        var targets = args.Skip(1).Where(a => !a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (targets.Length == 0) { Console.WriteLine("Usage: powerforge normalize <files...>"); return 2; }
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
        var targets = args.Skip(1).Where(a => !a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (targets.Length == 0) { Console.WriteLine("Usage: powerforge format <files...>"); return 2; }
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
        var parsed = ParseInstallArgs(args.Skip(1).ToArray());
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;
        var resolved = PowerForge.ModuleInstaller.ResolveTargetVersion(p.Roots, p.Name, p.Version, p.Strategy);
        try { ManifestEditor.TrySetTopLevelModuleVersion(Path.Combine(p.Staging, $"{p.Name}.psd1"), resolved); } catch { }
        var installer = new PowerForge.ModuleInstaller(logger);
        var options = new PowerForge.ModuleInstallerOptions(p.Roots, PowerForge.InstallationStrategy.Exact, p.Keep);
        var res = installer.InstallFromStaging(p.Staging, p.Name, resolved, options);
        logger.Success($"Installed {p.Name} {resolved}");
        foreach (var path in res.InstalledPaths) logger.Info($" → {path}");     
        if (res.PrunedPaths.Count > 0) logger.Warn($"Pruned versions: {res.PrunedPaths.Count}");
        return 0;
    }
    case "find":
    {
        var parsed = ParseFindArgs(args.Skip(1).ToArray());
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;

        try
        {
            var runner = new PowerShellRunner();
            var client = new PSResourceGetClient(runner, logger);
            var opts = new PSResourceFindOptions(p.Names, p.Version, p.Prerelease, p.Repositories);
            var results = client.Find(opts);
            foreach (var r in results)
            {
                Console.WriteLine($"{r.Name}\t{r.Version}\t{r.Repository ?? string.Empty}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
            return 1;
        }
    }
    case "publish":
    {
        var parsed = ParsePublishArgs(args.Skip(1).ToArray());
        if (parsed is null) { PrintHelp(); return 2; }
        var p = parsed.Value;

        try
        {
            var runner = new PowerShellRunner();
            var client = new PSResourceGetClient(runner, logger);
            var opts = new PSResourcePublishOptions(
                path: Path.GetFullPath(p.Path.Trim().Trim('"')),
                isNupkg: p.IsNupkg,
                repository: p.Repository,
                apiKey: p.ApiKey,
                destinationPath: p.DestinationPath,
                skipDependenciesCheck: p.SkipDependenciesCheck,
                skipModuleManifestValidate: p.SkipModuleManifestValidate);
            client.Publish(opts);
            logger.Success($"Published {opts.Path}{(string.IsNullOrWhiteSpace(p.Repository) ? string.Empty : " to " + p.Repository)}");
            return 0;
        }
        catch (Exception ex)
        {
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
  powerforge build --name <ModuleName> --project-root <path> --csproj <path> --version <X.Y.Z> [--staging <path>] [--configuration Release] [--framework <tfm>]* [--author <name>] [--company <name>] [--description <text>] [--tag <tag>]*
  powerforge normalize <files...>   Normalize encodings and line endings        
  powerforge format <files...>      Format scripts via PSScriptAnalyzer (out-of-proc)
  powerforge install --name <ModuleName> --version <X.Y.Z> --staging <path> [--strategy exact|autorevision] [--keep N] [--root path]*
  powerforge find --name <Name>[,<Name>...] [--repo <Repo>] [--version <X.Y.Z>] [--prerelease]
  powerforge publish --path <Path> [--repo <Repo>] [--apikey <Key>] [--nupkg] [--destination <Path>] [--skip-dependencies-check] [--skip-manifest-validate]
  powerforge -Verbose               Enable verbose diagnostics
");
}

static (string Name, string Source, string? Staging, string Csproj, string Version, string Configuration, string[] Frameworks, string? Author, string? CompanyName, string? Description, string[] Tags, string? IconUri, string? ProjectUri)? ParseBuildArgs(string[] argv)
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

    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(csproj) || string.IsNullOrWhiteSpace(version))
        return null;

    var tfms = frameworks.Count > 0 ? frameworks.ToArray() : new[] { "net472", "net8.0" };
    return (name!, source!, staging, csproj!, version!, configuration, tfms, author, companyName, description, tags.ToArray(), iconUri, projectUri);
}

static void CopyDirectoryFiltered(string sourceDir, string destDir, ISet<string> excludedDirectoryNames)
{
    var sourceFull = Path.GetFullPath(sourceDir);
    var destFull = Path.GetFullPath(destDir);

    var stack = new Stack<string>();
    stack.Push(sourceFull);

    while (stack.Count > 0)
    {
        var current = stack.Pop();
        var rel = Path.GetRelativePath(sourceFull, current);
        var targetDir = string.IsNullOrEmpty(rel) || rel == "." ? destFull : Path.Combine(destFull, rel);
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name) && excludedDirectoryNames.Contains(name)) continue;
            stack.Push(dir);
        }
    }
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
        }
    }
    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(staging))
        return null;
    return (name!, version!, staging!, roots.ToArray(), strategy, keep);        
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

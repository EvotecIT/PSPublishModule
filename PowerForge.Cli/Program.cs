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
        var installer = new PowerForge.ModuleInstaller(logger);
        var options = new PowerForge.ModuleInstallerOptions(p.Roots, p.Strategy, p.Keep);
        var res = installer.InstallFromStaging(p.Staging, p.Name, p.Version, options);
        logger.Success($"Installed {p.Name} {res.Version}");
        foreach (var path in res.InstalledPaths) logger.Info($" → {path}");
        if (res.PrunedPaths.Count > 0) logger.Warn($"Pruned versions: {res.PrunedPaths.Count}");
        return 0;
    }
    default:
        PrintHelp();
        return 2;
}

static void PrintHelp()
{
    Console.WriteLine(@"PowerForge CLI
Usage:
  powerforge normalize <files...>   Normalize encodings and line endings
  powerforge format <files...>      Format scripts via PSScriptAnalyzer (out-of-proc)
  powerforge install --name <ModuleName> --version <X.Y.Z> --staging <path> [--strategy exact|autorevision] [--keep N] [--root path]*
  powerforge -Verbose               Enable verbose diagnostics
");
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

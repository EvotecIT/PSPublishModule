using PowerForge;
using PowerForge.Cli;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    static void WriteLogTail(BufferingLogger buffer, ILogger logger, int maxEntries = 80)
    {
        if (buffer is null) return;
        if (buffer.Entries.Count == 0) return;

        maxEntries = Math.Max(1, maxEntries);
        var total = buffer.Entries.Count;
        var start = Math.Max(0, total - maxEntries);
        var shown = total - start;

        logger.Warn($"Last {shown}/{total} log lines:");
        for (int i = start; i < total; i++)
        {
            var e = buffer.Entries[i];
            var msg = e.Message ?? string.Empty;
            switch (e.Level)
            {
                case "success": logger.Success(msg); break;
                case "warn": logger.Warn(msg); break;
                case "error": logger.Error(msg); break;
                case "verbose": logger.Verbose(msg); break;
                default: logger.Info(msg); break;
            }
        }
    }

    static void WriteDotNetPublishFailureDetails(DotNetPublishResult result, ILogger logger)
    {
        if (result is null) return;
        if (result.Failure is null) return;

        var f = result.Failure;
        logger.Warn($"Failure step: {f.StepKind} ({f.StepKey})");
        if (!string.IsNullOrWhiteSpace(f.LogPath))
            logger.Warn($"Failure log: {f.LogPath}");

        if (!string.IsNullOrWhiteSpace(f.StdErrTail))
            logger.Warn($"stderr (tail):{Environment.NewLine}{f.StdErrTail.TrimEnd()}");
        else if (!string.IsNullOrWhiteSpace(f.StdOutTail))
            logger.Warn($"stdout (tail):{Environment.NewLine}{f.StdOutTail.TrimEnd()}");
    }

    static T RunWithStatus<T>(bool outputJson, CliOptions cli, string statusText, Func<T> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));

        if (outputJson || cli.Quiet || cli.NoColor)
            return action();

        var view = ResolveConsoleView(cli.View);
        if (view != ConsoleView.Standard)
            return action();

        if (ConsoleEnvironment.IsCI || !Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive)
            return action();

        T? result = default;
        Spectre.Console.AnsiConsole.Status().Start(statusText, _ => { result = action(); });
        return result!;
    }

    static ConsoleView ResolveConsoleView(ConsoleView requested)
    {
        if (requested != ConsoleView.Auto) return requested;

        var interactive = Spectre.Console.AnsiConsole.Profile.Capabilities.Interactive
            && !ConsoleEnvironment.IsCI;

        return interactive ? ConsoleView.Standard : ConsoleView.Ansi;
    }

    static bool TryParseConsoleView(string? value, out ConsoleView view)
    {
        view = ConsoleView.Auto;
        if (string.IsNullOrWhiteSpace(value)) return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                view = ConsoleView.Auto;
                return true;
            case "standard":
            case "interactive":
                view = ConsoleView.Standard;
                return true;
            case "ansi":
            case "plain":
                view = ConsoleView.Ansi;
                return true;
            default:
                return false;
        }
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

    static ModuleTestSuiteSpec? ParseTestArgs(string[] argv)
    {
        var spec = new ModuleTestSuiteSpec { ProjectPath = Directory.GetCurrentDirectory() };

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                continue;

            switch (a.ToLowerInvariant())
            {
                case "--project-root":
                case "--project":
                case "--path":
                    if (++i >= argv.Length) return null;
                    spec.ProjectPath = argv[i];
                    break;
                case "--test-path":
                    if (++i >= argv.Length) return null;
                    spec.TestPath = argv[i];
                    break;
                case "--format":
                case "--output-format":
                    if (++i >= argv.Length) return null;
                    if (!Enum.TryParse<ModuleTestSuiteOutputFormat>(argv[i], ignoreCase: true, out var fmt))
                        return null;
                    spec.OutputFormat = fmt;
                    break;
                case "--additional-modules":
                case "--modules":
                    if (++i >= argv.Length) return null;
                    spec.AdditionalModules = SplitCsv(argv[i]);
                    break;
                case "--skip-modules":
                    if (++i >= argv.Length) return null;
                    spec.SkipModules = SplitCsv(argv[i]);
                    break;
                case "--coverage":
                case "--enable-code-coverage":
                    spec.EnableCodeCoverage = true;
                    break;
                case "--force":
                    spec.Force = true;
                    break;
                case "--skip-dependencies":
                    spec.SkipDependencies = true;
                    break;
                case "--skip-import":
                    spec.SkipImport = true;
                    break;
                case "--keep-xml":
                case "--keep-results-xml":
                    spec.KeepResultsXml = true;
                    break;
                case "--timeout":
                case "--timeout-seconds":
                    if (++i >= argv.Length) return null;
                    if (!int.TryParse(argv[i], out var t)) return null;
                    spec.TimeoutSeconds = t;
                    break;
                case "--prefer-powershell":
                case "--prefer-windows-powershell":
                    spec.PreferPwsh = false;
                    break;
                case "--prefer-pwsh":
                    spec.PreferPwsh = true;
                    break;
                case "--config":
                    i++;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
            }
        }

        return spec;
    }

    static string[] SplitCsv(string value)
        => (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

    static string[] ParseCsvOptionValues(string[] argv, params string[] optionNames)
    {
        if (argv is null || argv.Length == 0) return Array.Empty<string>();
        if (optionNames is null || optionNames.Length == 0) return Array.Empty<string>();

        var values = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            if (!optionNames.Any(o => a.Equals(o, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (++i < argv.Length)
                values.AddRange(SplitCsv(argv[i]));
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static DotNetPublishStyle ParseDotNetPublishStyle(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Style must not be empty.", nameof(value));

        if (Enum.TryParse(raw, ignoreCase: true, out DotNetPublishStyle style))
            return style;

        throw new ArgumentException($"Unknown style: {raw}. Expected one of: {string.Join(", ", Enum.GetNames(typeof(DotNetPublishStyle)))}", nameof(value));
    }

    static void ApplyDotNetPublishOverrides(
        DotNetPublishSpec spec,
        string[] overrideTargets,
        string[] overrideRids,
        string[] overrideFrameworks,
        string? overrideStyle)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));

        var targets = (spec.Targets ?? Array.Empty<DotNetPublishTarget>())
            .Where(t => t is not null)
            .ToArray();

        if (overrideTargets is { Length: > 0 })
        {
            var selected = new HashSet<string>(overrideTargets.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var missing = selected.Where(n => targets.All(t => !t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown target(s): {string.Join(", ", missing)}", nameof(overrideTargets));

            targets = targets.Where(t => selected.Contains(t.Name)).ToArray();
            if (targets.Length == 0)
                throw new ArgumentException("No targets selected.", nameof(overrideTargets));

            spec.Targets = targets;
        }

        if (overrideRids is { Length: > 0 })
        {
            spec.DotNet.Runtimes = overrideRids;
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Runtimes = overrideRids;
            }
        }

        if (overrideFrameworks is { Length: > 0 })
        {
            var frameworks = overrideFrameworks
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (frameworks.Length == 0)
                throw new ArgumentException("Frameworks must not be empty.", nameof(overrideFrameworks));

            var tfm = frameworks[0];
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Framework = tfm;
                t.Publish.Frameworks = frameworks;
            }
        }

        if (!string.IsNullOrWhiteSpace(overrideStyle))
        {
            var style = ParseDotNetPublishStyle(overrideStyle);
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Style = style;
            }
        }
    }

    static void ApplyDotNetPublishSkipFlags(DotNetPublishPlan plan, bool skipRestore, bool skipBuild)
    {
        if (plan is null) return;

        var steps = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        if (skipRestore)
        {
            plan.NoRestoreInPublish = true;
            steps = steps.Where(s => s.Kind != DotNetPublishStepKind.Restore).ToArray();
        }

        if (skipBuild)
        {
            plan.NoBuildInPublish = true;
            steps = steps.Where(s => s.Kind != DotNetPublishStepKind.Build).ToArray();
        }

        plan.Steps = steps;
    }

    static string[] ValidateDotNetPublishPlan(DotNetPublishPlan plan)
    {
        var errors = new List<string>();
        if (plan is null)
        {
            errors.Add("Plan is null.");
            return errors.ToArray();
        }

        if (string.IsNullOrWhiteSpace(plan.ProjectRoot) || !Directory.Exists(plan.ProjectRoot))
            errors.Add($"ProjectRoot not found: {plan.ProjectRoot}");

        if (!string.IsNullOrWhiteSpace(plan.SolutionPath) && !File.Exists(plan.SolutionPath))
            errors.Add($"SolutionPath not found: {plan.SolutionPath}");

        if (plan.Targets is null || plan.Targets.Length == 0)
        {
            errors.Add("Targets must not be empty.");
        }
        else
        {
            foreach (var t in plan.Targets)
            {
                if (t is null) continue;
                if (string.IsNullOrWhiteSpace(t.Name))
                    errors.Add("Target.Name must not be empty.");
                if (string.IsNullOrWhiteSpace(t.ProjectPath) || !File.Exists(t.ProjectPath))
                    errors.Add($"ProjectPath not found for target '{t.Name}': {t.ProjectPath}");

                if (t.Publish is null)
                {
                    errors.Add($"Target.Publish is required for target '{t.Name}'.");
                    continue;
                }

                var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .ToArray();
                if (frameworks.Length == 0 && string.IsNullOrWhiteSpace(t.Publish.Framework))
                    errors.Add($"Target.Publish.Framework is required for target '{t.Name}' (or set Target.Publish.Frameworks).");

                if (t.Publish.Runtimes is null || t.Publish.Runtimes.Length == 0 || t.Publish.Runtimes.All(string.IsNullOrWhiteSpace))
                    errors.Add($"Target.Publish.Runtimes is empty for target '{t.Name}'.");
            }
        }

        if (plan.Steps is null || plan.Steps.Length == 0)
        {
            errors.Add("No steps planned.");
        }
        else
        {
            if (!plan.Steps.Any(s => s.Kind == DotNetPublishStepKind.Publish))
                errors.Add("No publish steps planned.");

            var dupKeys = plan.Steps
                .Where(s => !string.IsNullOrWhiteSpace(s.Key))
                .GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (dupKeys.Length > 0)
                errors.Add($"Duplicate step keys: {string.Join(", ", dupKeys)}");
        }

        return errors.ToArray();
    }
}


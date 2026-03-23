using PowerForge;
using PowerForge.Cli;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;

internal static partial class Program
{
    static void WriteLogTail(BufferedLogger buffer, ILogger logger, int maxEntries = 80)
    {
        new BufferedLogSupportService().WriteTail(buffer.Entries, logger, maxEntries);
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

    static string[] ParseRepeatedOptionValues(string[] argv, params string[] optionNames)
    {
        if (argv is null || argv.Length == 0) return Array.Empty<string>();
        if (optionNames is null || optionNames.Length == 0) return Array.Empty<string>();

        var names = new HashSet<string>(optionNames, StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            if (!names.Contains(argv[i]))
                continue;

            if (++i >= argv.Length)
                break;

            if (!string.IsNullOrWhiteSpace(argv[i]))
                values.Add(argv[i]);
        }

        return values.ToArray();
    }

    static Dictionary<string, string?> ParseKeyValueOptions(string[] argv, params string[] optionNames)
    {
        var values = ParseRepeatedOptionValues(argv, optionNames);
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var separator = value.IndexOf('=');
            if (separator <= 0)
            {
                result[value.Trim()] = string.Empty;
                continue;
            }

            var key = value.Substring(0, separator).Trim();
            var itemValue = value[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key] = itemValue;
        }

        return result;
    }

    static string[] ParseRawOptionValues(string[] argv, params string[] optionNames)
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
                values.Add(argv[i] ?? string.Empty);
        }

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToArray();
    }

    static DotNetPublishMatrixOverrideOptions ParseDotNetPublishMatrixOverrides(string[] argv)
    {
        var rawValues = ParseRawOptionValues(argv, "--matrix");
        if (rawValues.Length == 0)
            return new DotNetPublishMatrixOverrideOptions();

        var runtimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var styles = new HashSet<DotNetPublishStyle>();

        foreach (var raw in rawValues)
        {
            var entries = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                var token = (entry ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                var eq = token.IndexOf('=');
                if (eq <= 0 || eq == token.Length - 1)
                    throw new ArgumentException($"Invalid --matrix entry '{token}'. Expected 'dimension=value[,value]'.");

                var key = token.Substring(0, eq).Trim().ToLowerInvariant();
                var values = SplitCsv(token.Substring(eq + 1));
                if (values.Length == 0)
                    throw new ArgumentException($"Invalid --matrix entry '{token}'. Values must not be empty.");

                switch (key)
                {
                    case "rid":
                    case "runtime":
                    case "runtimes":
                        foreach (var value in values)
                            runtimes.Add(value);
                        break;
                    case "framework":
                    case "frameworks":
                    case "tfm":
                    case "tfms":
                        foreach (var value in values)
                            frameworks.Add(value);
                        break;
                    case "style":
                    case "styles":
                        foreach (var value in values)
                            styles.Add(ParseDotNetPublishStyle(value));
                        break;
                    default:
                        throw new ArgumentException($"Unsupported --matrix dimension '{key}'. Supported: runtime, framework, style.");
                }
            }
        }

        return new DotNetPublishMatrixOverrideOptions
        {
            Runtimes = runtimes.ToArray(),
            Frameworks = frameworks.ToArray(),
            Styles = styles.ToArray()
        };
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

    static void ApplyDotNetPublishPlanOverrides(
        DotNetPublishPlan plan,
        string[] overrideTargets,
        string[] overrideRids,
        string[] overrideFrameworks,
        DotNetPublishStyle[] overrideStyles)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var targets = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(t => t is not null)
            .ToArray();
        var changed = false;

        if (overrideTargets is { Length: > 0 })
        {
            var selected = new HashSet<string>(overrideTargets.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var missing = selected.Where(n => targets.All(t => !t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown target(s): {string.Join(", ", missing)}", nameof(overrideTargets));

            targets = targets.Where(t => selected.Contains(t.Name)).ToArray();
            if (targets.Length == 0)
                throw new ArgumentException("No targets selected.", nameof(overrideTargets));

            plan.Targets = targets;
            if (plan.Installers is { Length: > 0 })
            {
                plan.Installers = plan.Installers
                    .Where(i =>
                        i is not null
                        && (string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                            || selected.Contains(i.PrepareFromTarget)))
                    .ToArray();
            }
            changed = true;
        }

        if (overrideRids is { Length: > 0 })
        {
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Runtimes = overrideRids;
            }
            changed = true;
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
            changed = true;
        }

        if (overrideStyles is { Length: > 0 })
        {
            var styles = overrideStyles.Distinct().ToArray();
            var style = styles[0];
            foreach (var t in targets)
            {
                if (t.Publish is null) continue;
                t.Publish.Style = style;
                t.Publish.Styles = styles;
            }
            changed = true;
        }

        if (changed)
            RebuildDotNetPublishPlanSteps(plan);
    }

    static void RebuildDotNetPublishPlanSteps(DotNetPublishPlan plan)
    {
        var original = plan.Steps ?? Array.Empty<DotNetPublishStep>();
        var rebuilt = new List<DotNetPublishStep>();
        DotNetPublishStep? manifest = null;

        foreach (var step in original)
        {
            if (step.Kind == DotNetPublishStepKind.Publish) continue;
            if (step.Kind == DotNetPublishStepKind.ServiceLifecycle) continue;
            if (step.Kind == DotNetPublishStepKind.MsiPrepare) continue;
            if (step.Kind == DotNetPublishStepKind.MsiBuild) continue;
            if (step.Kind == DotNetPublishStepKind.MsiSign) continue;
            if (step.Kind == DotNetPublishStepKind.StorePackage) continue;
            if (step.Kind == DotNetPublishStepKind.BenchmarkExtract) continue;
            if (step.Kind == DotNetPublishStepKind.BenchmarkGate) continue;
            if (step.Kind == DotNetPublishStepKind.Manifest)
            {
                manifest ??= step;
                continue;
            }
            rebuilt.Add(step);
        }

        foreach (var t in plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
        {
            var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworks.Length == 0 && !string.IsNullOrWhiteSpace(t.Publish.Framework))
                frameworks = new[] { t.Publish.Framework.Trim() };

            var styles = (t.Publish.Styles ?? Array.Empty<DotNetPublishStyle>())
                .Distinct()
                .ToArray();
            if (styles.Length == 0)
                styles = new[] { t.Publish.Style };

            var combinations = new List<DotNetPublishTargetCombination>();
            foreach (var framework in frameworks)
            {
                foreach (var rid in t.Publish.Runtimes ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(rid)) continue;
                    foreach (var style in styles)
                    {
                        var combo = new DotNetPublishTargetCombination
                        {
                            Framework = framework,
                            Runtime = rid.Trim(),
                            Style = style
                        };
                        combinations.Add(combo);

                        rebuilt.Add(new DotNetPublishStep
                        {
                            Key = $"publish:{t.Name}:{framework}:{rid}:{style}",
                            Kind = DotNetPublishStepKind.Publish,
                            Title = "Publish",
                            TargetName = t.Name,
                            Framework = framework,
                            Runtime = rid.Trim(),
                            Style = style
                        });

                        if (t.Publish.Service?.Lifecycle?.Enabled == true
                            && t.Publish.Service.Lifecycle.Mode == DotNetPublishServiceLifecycleMode.Step)
                        {
                            rebuilt.Add(new DotNetPublishStep
                            {
                                Key = $"service.lifecycle:{t.Name}:{framework}:{rid}:{style}",
                                Kind = DotNetPublishStepKind.ServiceLifecycle,
                                Title = "Service lifecycle",
                                TargetName = t.Name,
                                Framework = framework,
                                Runtime = rid.Trim(),
                                Style = style
                            });
                        }

                        AppendMsiPrepareSteps(rebuilt, plan, t, framework, rid.Trim(), style);
                        AppendStorePackageSteps(rebuilt, plan, t, framework, rid.Trim(), style);
                    }
                }
            }

            t.Combinations = combinations.ToArray();
        }

        AppendBenchmarkGateSteps(rebuilt, plan);

        manifest ??= new DotNetPublishStep
        {
            Key = "manifest",
            Kind = DotNetPublishStepKind.Manifest,
            Title = "Write manifest"
        };
        rebuilt.Add(manifest);
        plan.Steps = rebuilt.ToArray();
    }

    static void AppendBenchmarkGateSteps(
        List<DotNetPublishStep> rebuilt,
        DotNetPublishPlan plan)
    {
        if (rebuilt is null || plan is null) return;

        foreach (var gate in plan.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGatePlan>())
        {
            if (gate is null || !gate.Enabled) continue;
            if (string.IsNullOrWhiteSpace(gate.Id)) continue;

            rebuilt.Add(new DotNetPublishStep
            {
                Key = $"benchmark.extract:{gate.Id}",
                Kind = DotNetPublishStepKind.BenchmarkExtract,
                Title = "Benchmark extract",
                GateId = gate.Id
            });
            rebuilt.Add(new DotNetPublishStep
            {
                Key = $"benchmark.gate:{gate.Id}",
                Kind = DotNetPublishStepKind.BenchmarkGate,
                Title = "Benchmark gate",
                GateId = gate.Id
            });
        }
    }

    static void AppendMsiPrepareSteps(
        List<DotNetPublishStep> rebuilt,
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        if (rebuilt is null || plan is null || target is null)
            return;

        foreach (var installer in plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
        {
            if (installer is null) continue;
            if (!string.Equals(installer.PrepareFromTarget, target.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["installer"] = installer.Id ?? string.Empty,
                ["target"] = target.Name ?? string.Empty,
                ["rid"] = runtime ?? string.Empty,
                ["framework"] = framework ?? string.Empty,
                ["style"] = style.ToString(),
                ["configuration"] = plan.Configuration ?? "Release"
            };

            var stagingTemplate = string.IsNullOrWhiteSpace(installer.StagingPath)
                ? "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/payload"
                : installer.StagingPath!;
            var manifestTemplate = string.IsNullOrWhiteSpace(installer.ManifestPath)
                ? "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/prepare.manifest.json"
                : installer.ManifestPath!;
            var harvestTemplate = string.IsNullOrWhiteSpace(installer.HarvestPath)
                ? "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/harvest.wxs"
                : installer.HarvestPath!;

            var stagingPath = ResolveDotNetPublishPath(plan.ProjectRoot, ReplaceDotNetPublishTemplate(stagingTemplate, tokens));
            var manifestPath = ResolveDotNetPublishPath(plan.ProjectRoot, ReplaceDotNetPublishTemplate(manifestTemplate, tokens));
            var harvestPath = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
                ? ResolveDotNetPublishPath(plan.ProjectRoot, ReplaceDotNetPublishTemplate(harvestTemplate, tokens))
                : null;
            var harvestDirectoryRefId = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
                ? SanitizeWixIdentifier(
                    string.IsNullOrWhiteSpace(installer.HarvestDirectoryRefId) ? "INSTALLFOLDER" : installer.HarvestDirectoryRefId!,
                    "INSTALLFOLDER")
                : null;
            var harvestComponentGroupId = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
                ? SanitizeWixIdentifier(
                    ReplaceDotNetPublishTemplate(
                        string.IsNullOrWhiteSpace(installer.HarvestComponentGroupId)
                            ? "Harvest_{installer}_{target}_{framework}_{rid}_{style}"
                            : installer.HarvestComponentGroupId!,
                        tokens),
                    "Harvest")
                : null;

            if (!plan.AllowOutputOutsideProjectRoot)
            {
                EnsureDotNetPublishPathWithinRoot(plan.ProjectRoot, stagingPath, $"Installer '{installer.Id}' staging path");
                EnsureDotNetPublishPathWithinRoot(plan.ProjectRoot, manifestPath, $"Installer '{installer.Id}' manifest path");
                if (!string.IsNullOrWhiteSpace(harvestPath))
                    EnsureDotNetPublishPathWithinRoot(plan.ProjectRoot, harvestPath, $"Installer '{installer.Id}' harvest path");
            }

            rebuilt.Add(new DotNetPublishStep
            {
                Key = $"msi.prepare:{installer.Id}:{target.Name}:{framework}:{runtime}:{style}",
                Kind = DotNetPublishStepKind.MsiPrepare,
                Title = "MSI prepare",
                InstallerId = installer.Id,
                TargetName = target.Name,
                Framework = framework,
                Runtime = runtime,
                Style = style,
                StagingPath = stagingPath,
                ManifestPath = manifestPath,
                HarvestPath = harvestPath,
                HarvestDirectoryRefId = harvestDirectoryRefId,
                HarvestComponentGroupId = harvestComponentGroupId,
                InstallerProjectPath = installer.InstallerProjectPath
            });

            if (!string.IsNullOrWhiteSpace(installer.InstallerProjectPath))
            {
                rebuilt.Add(new DotNetPublishStep
                {
                    Key = $"msi.build:{installer.Id}:{target.Name}:{framework}:{runtime}:{style}",
                    Kind = DotNetPublishStepKind.MsiBuild,
                    Title = "MSI build",
                    InstallerId = installer.Id,
                    TargetName = target.Name,
                    Framework = framework,
                    Runtime = runtime,
                    Style = style,
                    InstallerProjectPath = installer.InstallerProjectPath
                });

                if (installer.Sign?.Enabled == true)
                {
                    rebuilt.Add(new DotNetPublishStep
                    {
                        Key = $"msi.sign:{installer.Id}:{target.Name}:{framework}:{runtime}:{style}",
                        Kind = DotNetPublishStepKind.MsiSign,
                        Title = "MSI sign",
                        InstallerId = installer.Id,
                        TargetName = target.Name,
                        Framework = framework,
                        Runtime = runtime,
                        Style = style
                    });
                }
            }
        }
    }

    static void AppendStorePackageSteps(
        List<DotNetPublishStep> rebuilt,
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        if (rebuilt is null || plan is null || target is null)
            return;

        foreach (var storePackage in plan.StorePackages ?? Array.Empty<DotNetPublishStorePackagePlan>())
        {
            if (storePackage is null) continue;
            if (!string.Equals(storePackage.PrepareFromTarget, target.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var combo = new DotNetPublishTargetCombination
            {
                Framework = framework ?? string.Empty,
                Runtime = runtime ?? string.Empty,
                Style = style
            };

            if ((storePackage.Runtimes?.Length ?? 0) > 0
                && !storePackage.Runtimes!.Any(value => string.Equals(value, combo.Runtime, StringComparison.OrdinalIgnoreCase)))
                continue;
            if ((storePackage.Frameworks?.Length ?? 0) > 0
                && !storePackage.Frameworks!.Any(value => string.Equals(value, combo.Framework, StringComparison.OrdinalIgnoreCase)))
                continue;
            if ((storePackage.Styles?.Length ?? 0) > 0
                && !storePackage.Styles!.Contains(combo.Style))
                continue;

            var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["storePackage"] = storePackage.Id ?? string.Empty,
                ["target"] = target.Name ?? string.Empty,
                ["rid"] = runtime ?? string.Empty,
                ["framework"] = framework ?? string.Empty,
                ["style"] = style.ToString(),
                ["configuration"] = plan.Configuration ?? "Release"
            };

            var outputTemplate = string.IsNullOrWhiteSpace(storePackage.OutputPath)
                ? "Artifacts/DotNetPublish/Store/{storePackage}/{target}/{rid}/{framework}/{style}"
                : storePackage.OutputPath!;
            var outputPath = ResolveDotNetPublishPath(plan.ProjectRoot, ReplaceDotNetPublishTemplate(outputTemplate, tokens));

            if (!plan.AllowOutputOutsideProjectRoot)
                EnsureDotNetPublishPathWithinRoot(plan.ProjectRoot, outputPath, $"Store package '{storePackage.Id}' output path");

            rebuilt.Add(new DotNetPublishStep
            {
                Key = $"store.package:{storePackage.Id}:{target.Name}:{framework}:{runtime}:{style}",
                Kind = DotNetPublishStepKind.StorePackage,
                Title = "Store package",
                StorePackageId = storePackage.Id,
                TargetName = target.Name,
                Framework = framework,
                Runtime = runtime,
                Style = style,
                StorePackageProjectPath = storePackage.PackagingProjectPath,
                StorePackageOutputPath = outputPath
            });
        }
    }

    static string ReplaceDotNetPublishTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var value = template ?? string.Empty;
        foreach (var kv in tokens)
            value = value.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return value;
    }

    static string ResolveDotNetPublishPath(string baseDir, string path)
    {
        var raw = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(raw))
            return Path.GetFullPath(raw);
        return Path.GetFullPath(Path.Combine(baseDir, raw));
    }

    static void EnsureDotNetPublishPathWithinRoot(string rootPath, string path, string label)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(path);
        if (PathsEqual(root, candidate)) return;

        var withSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(withSep, GetPathComparison()))
        {
            throw new InvalidOperationException(
                $"{label} resolves outside ProjectRoot and is blocked by policy. Path='{candidate}', ProjectRoot='{root}'.");
        }
    }

    static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            (left ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            (right ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            GetPathComparison());
    }

    static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static string SanitizeWixIdentifier(string value, string fallback)
    {
        var input = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (string.IsNullOrWhiteSpace(input)) input = fallback;

        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if ((ch >= 'A' && ch <= 'Z')
                || (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch == '_')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        var output = sb.ToString();
        if (string.IsNullOrWhiteSpace(output)) output = fallback;
        if (char.IsDigit(output[0])) output = "_" + output;
        return output;
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

        if (plan.LockedOutputSampleLimit < 1)
            errors.Add("LockedOutputSampleLimit must be at least 1.");

        if (!string.IsNullOrWhiteSpace(plan.Outputs?.RunReportPath) && !plan.AllowManifestOutsideProjectRoot)
        {
            try
            {
                EnsureDotNetPublishPathWithinRoot(plan.ProjectRoot, plan.Outputs.RunReportPath!, "RunReportPath");
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

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

                var styles = (t.Publish.Styles ?? Array.Empty<DotNetPublishStyle>()).Distinct().ToArray();
                if (styles.Length == 0 && !Enum.IsDefined(typeof(DotNetPublishStyle), t.Publish.Style))
                    errors.Add($"Target.Publish.Style is invalid for target '{t.Name}'.");

                if (t.Publish.State?.Enabled == true)
                {
                    var rules = t.Publish.State.Rules ?? Array.Empty<DotNetPublishStateRule>();
                    if (rules.Length == 0)
                    {
                        errors.Add($"Target.Publish.State for target '{t.Name}' requires at least one rule when Enabled=true.");
                    }
                    else
                    {
                        for (var ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                        {
                            var rule = rules[ruleIndex];
                            if (rule is null || string.IsNullOrWhiteSpace(rule.SourcePath))
                            {
                                errors.Add(
                                    $"Target.Publish.State rule[{ruleIndex}] for target '{t.Name}' requires SourcePath.");
                            }
                        }
                    }
                }

                if (t.Combinations is null || t.Combinations.Length == 0)
                    errors.Add($"Target combinations are empty for target '{t.Name}'.");
            }
        }

        var targetNames = new HashSet<string>(
            (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var installer in plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
        {
            if (installer is null) continue;
            if (string.IsNullOrWhiteSpace(installer.Id))
                errors.Add("Installer.Id must not be empty.");
            if (string.IsNullOrWhiteSpace(installer.PrepareFromTarget))
            {
                errors.Add($"Installer '{installer.Id}' PrepareFromTarget must not be empty.");
                continue;
            }

            if (!targetNames.Contains(installer.PrepareFromTarget))
                errors.Add($"Installer '{installer.Id}' references unknown target '{installer.PrepareFromTarget}'.");

            if (!string.IsNullOrWhiteSpace(installer.InstallerProjectPath) && !File.Exists(installer.InstallerProjectPath))
                errors.Add($"Installer project path not found for installer '{installer.Id}': {installer.InstallerProjectPath}");

            if (installer.ClientLicense?.Enabled == true
                && string.IsNullOrWhiteSpace(installer.ClientLicense.Path)
                && string.IsNullOrWhiteSpace(installer.ClientLicense.ClientId))
            {
                errors.Add(
                    $"Installer '{installer.Id}' ClientLicense requires Path or ClientId when Enabled=true.");
            }
        }

        var gateById = new Dictionary<string, DotNetPublishBenchmarkGatePlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var gate in plan.BenchmarkGates ?? Array.Empty<DotNetPublishBenchmarkGatePlan>())
        {
            if (gate is null) continue;
            if (string.IsNullOrWhiteSpace(gate.Id))
            {
                errors.Add("Benchmark gate Id must not be empty.");
                continue;
            }

            if (!gateById.TryAdd(gate.Id, gate))
                errors.Add($"Duplicate benchmark gate ID in plan: {gate.Id}");

            if (string.IsNullOrWhiteSpace(gate.SourcePath))
                errors.Add($"Benchmark gate '{gate.Id}' SourcePath must not be empty.");
            if (string.IsNullOrWhiteSpace(gate.BaselinePath))
                errors.Add($"Benchmark gate '{gate.Id}' BaselinePath must not be empty.");
            else if (gate.BaselineMode == DotNetPublishBaselineMode.Verify && !File.Exists(gate.BaselinePath))
                errors.Add($"Benchmark gate '{gate.Id}' baseline file not found (verify mode): {gate.BaselinePath}");
            if (gate.RelativeTolerance < 0)
                errors.Add($"Benchmark gate '{gate.Id}' RelativeTolerance must be >= 0.");
            if (gate.AbsoluteToleranceMs < 0)
                errors.Add($"Benchmark gate '{gate.Id}' AbsoluteToleranceMs must be >= 0.");
            if (gate.Metrics is null || gate.Metrics.Length == 0)
                errors.Add($"Benchmark gate '{gate.Id}' requires at least one metric.");
        }

        if (plan.Steps is null || plan.Steps.Length == 0)
        {
            errors.Add("No steps planned.");
        }
        else
        {
            if (!plan.Steps.Any(s => s.Kind == DotNetPublishStepKind.Publish))
                errors.Add("No publish steps planned.");

            var publishMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var publishStep in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.Publish))
            {
                var target = publishStep.TargetName ?? string.Empty;
                var framework = publishStep.Framework ?? string.Empty;
                var runtime = publishStep.Runtime ?? string.Empty;
                var style = publishStep.Style?.ToString() ?? string.Empty;
                publishMatches.Add($"{target}|{framework}|{runtime}|{style}");
            }

            foreach (var msiStep in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.MsiPrepare))
            {
                if (string.IsNullOrWhiteSpace(msiStep.InstallerId))
                    errors.Add($"MSI prepare step '{msiStep.Key}' is missing InstallerId.");
                if (string.IsNullOrWhiteSpace(msiStep.StagingPath))
                    errors.Add($"MSI prepare step '{msiStep.Key}' is missing StagingPath.");
                if (string.IsNullOrWhiteSpace(msiStep.ManifestPath))
                    errors.Add($"MSI prepare step '{msiStep.Key}' is missing ManifestPath.");
                if (!msiStep.Style.HasValue)
                    errors.Add($"MSI prepare step '{msiStep.Key}' is missing style.");
                if (!string.IsNullOrWhiteSpace(msiStep.HarvestPath))
                {
                    if (string.IsNullOrWhiteSpace(msiStep.HarvestDirectoryRefId))
                        errors.Add($"MSI prepare step '{msiStep.Key}' is missing HarvestDirectoryRefId.");
                    if (string.IsNullOrWhiteSpace(msiStep.HarvestComponentGroupId))
                        errors.Add($"MSI prepare step '{msiStep.Key}' is missing HarvestComponentGroupId.");
                }

                var key = $"{msiStep.TargetName}|{msiStep.Framework}|{msiStep.Runtime}|{msiStep.Style?.ToString() ?? string.Empty}";
                if (!publishMatches.Contains(key))
                {
                    errors.Add(
                        $"MSI prepare step '{msiStep.Key}' does not have a matching publish step " +
                        $"for target/framework/runtime/style.");
                }
            }

            var msiPrepareMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var msiPrepare in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.MsiPrepare))
            {
                var key = $"{msiPrepare.InstallerId}|{msiPrepare.TargetName}|{msiPrepare.Framework}|{msiPrepare.Runtime}|{msiPrepare.Style?.ToString() ?? string.Empty}";
                msiPrepareMatches.Add(key);
            }

            foreach (var msiBuild in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.MsiBuild))
            {
                if (string.IsNullOrWhiteSpace(msiBuild.InstallerId))
                    errors.Add($"MSI build step '{msiBuild.Key}' is missing InstallerId.");
                if (!msiBuild.Style.HasValue)
                    errors.Add($"MSI build step '{msiBuild.Key}' is missing style.");
                if (string.IsNullOrWhiteSpace(msiBuild.InstallerProjectPath))
                {
                    errors.Add($"MSI build step '{msiBuild.Key}' is missing InstallerProjectPath.");
                }
                else if (!File.Exists(msiBuild.InstallerProjectPath))
                {
                    errors.Add($"MSI build step '{msiBuild.Key}' project path not found: {msiBuild.InstallerProjectPath}");
                }

                var key = $"{msiBuild.InstallerId}|{msiBuild.TargetName}|{msiBuild.Framework}|{msiBuild.Runtime}|{msiBuild.Style?.ToString() ?? string.Empty}";
                if (!msiPrepareMatches.Contains(key))
                {
                    errors.Add(
                        $"MSI build step '{msiBuild.Key}' does not have a matching msi.prepare step " +
                        $"for installer/target/framework/runtime/style.");
                }
            }

            var installerById = (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
                .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Id))
                .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var msiBuildMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var msiBuild in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.MsiBuild))
            {
                var key = $"{msiBuild.InstallerId}|{msiBuild.TargetName}|{msiBuild.Framework}|{msiBuild.Runtime}|{msiBuild.Style?.ToString() ?? string.Empty}";
                msiBuildMatches.Add(key);
            }

            foreach (var msiSign in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.MsiSign))
            {
                if (string.IsNullOrWhiteSpace(msiSign.InstallerId))
                    errors.Add($"MSI sign step '{msiSign.Key}' is missing InstallerId.");
                if (!msiSign.Style.HasValue)
                    errors.Add($"MSI sign step '{msiSign.Key}' is missing style.");

                if (string.IsNullOrWhiteSpace(msiSign.InstallerId) || !installerById.TryGetValue(msiSign.InstallerId!, out var installer))
                {
                    errors.Add($"MSI sign step '{msiSign.Key}' references unknown installer '{msiSign.InstallerId}'.");
                }
                else if (installer.Sign?.Enabled != true)
                {
                    errors.Add($"MSI sign step '{msiSign.Key}' requires installer Sign.Enabled=true.");
                }

                var key = $"{msiSign.InstallerId}|{msiSign.TargetName}|{msiSign.Framework}|{msiSign.Runtime}|{msiSign.Style?.ToString() ?? string.Empty}";
                if (!msiBuildMatches.Contains(key))
                {
                    errors.Add(
                        $"MSI sign step '{msiSign.Key}' does not have a matching msi.build step " +
                        $"for installer/target/framework/runtime/style.");
                }
            }

            foreach (var storeStep in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.StorePackage))
            {
                if (string.IsNullOrWhiteSpace(storeStep.StorePackageId))
                    errors.Add($"Store package step '{storeStep.Key}' is missing StorePackageId.");
                if (!storeStep.Style.HasValue)
                    errors.Add($"Store package step '{storeStep.Key}' is missing style.");
                if (string.IsNullOrWhiteSpace(storeStep.StorePackageProjectPath))
                {
                    errors.Add($"Store package step '{storeStep.Key}' is missing StorePackageProjectPath.");
                }
                else if (!File.Exists(storeStep.StorePackageProjectPath))
                {
                    errors.Add($"Store package step '{storeStep.Key}' project path not found: {storeStep.StorePackageProjectPath}");
                }

                if (string.IsNullOrWhiteSpace(storeStep.StorePackageOutputPath))
                    errors.Add($"Store package step '{storeStep.Key}' is missing StorePackageOutputPath.");

                var key = $"{storeStep.TargetName}|{storeStep.Framework}|{storeStep.Runtime}|{storeStep.Style?.ToString() ?? string.Empty}";
                if (!publishMatches.Contains(key))
                {
                    errors.Add(
                        $"Store package step '{storeStep.Key}' does not have a matching publish step " +
                        $"for target/framework/runtime/style.");
                }
            }

            var benchmarkExtractIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extract in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.BenchmarkExtract))
            {
                var gateId = extract.GateId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(gateId))
                {
                    errors.Add($"Benchmark extract step '{extract.Key}' is missing GateId.");
                    continue;
                }

                if (!gateById.ContainsKey(gateId))
                    errors.Add($"Benchmark extract step '{extract.Key}' references unknown gate '{gateId}'.");

                benchmarkExtractIds.Add(gateId);
            }

            foreach (var gateStep in plan.Steps.Where(s => s.Kind == DotNetPublishStepKind.BenchmarkGate))
            {
                var gateId = gateStep.GateId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(gateId))
                {
                    errors.Add($"Benchmark gate step '{gateStep.Key}' is missing GateId.");
                    continue;
                }

                if (!gateById.ContainsKey(gateId))
                    errors.Add($"Benchmark gate step '{gateStep.Key}' references unknown gate '{gateId}'.");
                if (!benchmarkExtractIds.Contains(gateId))
                {
                    errors.Add(
                        $"Benchmark gate step '{gateStep.Key}' does not have a matching benchmark.extract step for gate '{gateId}'.");
                }
            }

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

sealed class DotNetPublishMatrixOverrideOptions
{
    public string[] Runtimes { get; set; } = Array.Empty<string>();
    public string[] Frameworks { get; set; } = Array.Empty<string>();
    public DotNetPublishStyle[] Styles { get; set; } = Array.Empty<DotNetPublishStyle>();
}

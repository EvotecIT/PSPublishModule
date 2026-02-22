using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private const string DefaultMsiPrepareStagingPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/payload";

    private const string DefaultMsiPrepareManifestPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/prepare.manifest.json";

    private const string DefaultMsiHarvestPathTemplate =
        "Artifacts/DotNetPublish/Msi/{installer}/{target}/{rid}/{framework}/{style}/harvest.wxs";

    /// <summary>
    /// Resolves paths/defaults from <paramref name="spec"/> and produces an ordered execution plan.
    /// </summary>
    /// <param name="spec">Publish spec.</param>
    /// <param name="configPath">
    /// Optional path to the JSON config file. When provided, relative paths are resolved against its directory,
    /// unless <see cref="DotNetPublishDotNetOptions.ProjectRoot"/> is set.
    /// </param>
    public DotNetPublishPlan Plan(DotNetPublishSpec spec, string? configPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        spec = ResolveProfile(spec);

        if (spec.Targets is null || spec.Targets.Length == 0)
            throw new ArgumentException("Targets must not be empty.", nameof(spec));

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var projectRoot = string.IsNullOrWhiteSpace(spec.DotNet.ProjectRoot)
            ? configDir
            : ResolvePath(configDir, spec.DotNet.ProjectRoot!);

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"ProjectRoot not found: {projectRoot}");

        var solutionPath = string.IsNullOrWhiteSpace(spec.DotNet.SolutionPath)
            ? null
            : ResolvePath(projectRoot, spec.DotNet.SolutionPath!);

        if (!string.IsNullOrWhiteSpace(solutionPath) && !File.Exists(solutionPath))
            throw new FileNotFoundException($"SolutionPath not found: {solutionPath}");

        var cfg = string.IsNullOrWhiteSpace(spec.DotNet.Configuration) ? "Release" : spec.DotNet.Configuration.Trim();

        var defaultsRids = (spec.DotNet.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matrixDefaultRids = NormalizeStrings(spec.Matrix?.Runtimes);
        var matrixDefaultFrameworks = NormalizeStrings(spec.Matrix?.Frameworks);
        var matrixDefaultStyles = NormalizeStyles(spec.Matrix?.Styles);
        var projectCatalog = BuildProjectCatalog(spec.Projects, projectRoot);

        var msbuildProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spec.DotNet.MsBuildProperties is not null)
        {
            foreach (var kv in spec.DotNet.MsBuildProperties)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                msbuildProps[kv.Key.Trim()] = kv.Value ?? string.Empty;
            }
        }

        var targets = new List<DotNetPublishTargetPlan>();
        foreach (var t in spec.Targets)
        {
            if (t is null) continue;
            if (string.IsNullOrWhiteSpace(t.Name))
                throw new ArgumentException("Target.Name is required.", nameof(spec));
            if (t.Publish is null)
                throw new ArgumentException($"Target.Publish is required for '{t.Name}'.", nameof(spec));

            var resolvedProjectPath = ResolveTargetProjectPath(projectRoot, t, projectCatalog);
            if (!File.Exists(resolvedProjectPath))
                throw new FileNotFoundException($"ProjectPath not found for '{t.Name}': {resolvedProjectPath}");

            var frameworks = NormalizeStrings(t.Publish.Frameworks);
            if (frameworks.Length == 0)
            {
                if (!string.IsNullOrWhiteSpace(t.Publish.Framework))
                    frameworks = new[] { t.Publish.Framework.Trim() };
                else if (matrixDefaultFrameworks.Length > 0)
                    frameworks = matrixDefaultFrameworks;
            }
            if (frameworks.Length == 0)
                throw new ArgumentException($"Target.Publish.Framework is required for '{t.Name}' (or set Target.Publish.Frameworks/Matrix.Frameworks).", nameof(spec));

            var rids = NormalizeStrings(t.Publish.Runtimes);
            if (rids.Length == 0) rids = matrixDefaultRids;
            if (rids.Length == 0) rids = defaultsRids;
            if (rids.Length == 0)
                throw new ArgumentException($"No runtimes provided for target '{t.Name}'. Set Target.Publish.Runtimes, Matrix.Runtimes or DotNet.Runtimes.", nameof(spec));

            var styles = NormalizeStyles(t.Publish.Styles);
            if (styles.Length == 0 && matrixDefaultStyles.Length > 0)
                styles = matrixDefaultStyles;
            if (styles.Length == 0)
                styles = new[] { t.Publish.Style };

            var combos = BuildPublishCombos(t.Name.Trim(), frameworks, rids, styles, spec.Matrix);
            if (combos.Length == 0)
                throw new ArgumentException($"No publish combinations resolved for target '{t.Name}'. Check Matrix include/exclude filters.", nameof(spec));

            frameworks = combos
                .Select(c => c.Framework)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            rids = combos
                .Select(c => c.Runtime)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            styles = combos
                .Select(c => c.Style)
                .Distinct()
                .ToArray();

            // Clone publish settings for the plan and force resolved runtimes.
            var publish = new DotNetPublishPublishOptions
            {
                Style = styles[0],
                Styles = styles,
                Framework = frameworks[0],
                Frameworks = frameworks,
                Runtimes = rids,
                OutputPath = t.Publish.OutputPath,
                UseStaging = t.Publish.UseStaging,
                ClearOutput = t.Publish.ClearOutput,
                Slim = t.Publish.Slim,
                KeepSymbols = t.Publish.KeepSymbols,
                KeepDocs = t.Publish.KeepDocs,
                PruneReferences = t.Publish.PruneReferences,
                Zip = t.Publish.Zip,
                ZipPath = t.Publish.ZipPath,
                ZipNameTemplate = t.Publish.ZipNameTemplate,
                RenameTo = t.Publish.RenameTo,
                ReadyToRun = t.Publish.ReadyToRun,
                Sign = t.Publish.Sign,
                Service = CloneServicePackageOptions(t.Publish.Service),
                State = NormalizeStatePreservationOptions(t.Name.Trim(), t.Publish.State)
            };

            targets.Add(new DotNetPublishTargetPlan
            {
                Name = t.Name.Trim(),
                Kind = t.Kind,
                ProjectPath = resolvedProjectPath,
                Publish = publish,
                Combinations = combos
            });
        }

        var installers = BuildInstallerPlans(spec.Installers, targets, projectCatalog, projectRoot);

        var outputs = new DotNetPublishOutputs
        {
            ManifestJsonPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestJsonPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.json"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestJsonPath!),
            ManifestTextPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestTextPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.txt"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestTextPath!),
            ChecksumsPath = string.IsNullOrWhiteSpace(spec.Outputs.ChecksumsPath)
                ? null
                : ResolvePath(projectRoot, spec.Outputs.ChecksumsPath!),
            RunReportPath = string.IsNullOrWhiteSpace(spec.Outputs.RunReportPath)
                ? null
                : ResolvePath(projectRoot, spec.Outputs.RunReportPath!)
        };

        var benchmarkGates = BuildBenchmarkGatePlans(spec.BenchmarkGates, projectRoot);

        if (!spec.DotNet.AllowManifestOutsideProjectRoot)
        {
            if (!string.IsNullOrWhiteSpace(outputs.ManifestJsonPath))
                EnsurePathWithinRoot(projectRoot, outputs.ManifestJsonPath!, "ManifestJsonPath");
            if (!string.IsNullOrWhiteSpace(outputs.ManifestTextPath))
                EnsurePathWithinRoot(projectRoot, outputs.ManifestTextPath!, "ManifestTextPath");
            if (!string.IsNullOrWhiteSpace(outputs.ChecksumsPath))
                EnsurePathWithinRoot(projectRoot, outputs.ChecksumsPath!, "ChecksumsPath");
            if (!string.IsNullOrWhiteSpace(outputs.RunReportPath))
                EnsurePathWithinRoot(projectRoot, outputs.RunReportPath!, "RunReportPath");
        }

        if (!spec.DotNet.AllowOutputOutsideProjectRoot)
        {
            foreach (var target in targets)
            {
                foreach (var combo in target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                {
                    var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["target"] = target.Name,
                        ["rid"] = combo.Runtime,
                        ["framework"] = combo.Framework,
                        ["style"] = combo.Style.ToString(),
                        ["configuration"] = cfg
                    };

                    var outputTemplate = string.IsNullOrWhiteSpace(target.Publish.OutputPath)
                        ? Path.Combine("Artifacts", "DotNetPublish", "{target}", "{rid}", "{framework}", "{style}")
                        : target.Publish.OutputPath!;

                    var outputPath = ResolvePath(projectRoot, ApplyTemplate(outputTemplate, tokens));
                    EnsurePathWithinRoot(projectRoot, outputPath, $"Target '{target.Name}' output path");

                    var zipNameTemplate = string.IsNullOrWhiteSpace(target.Publish.ZipNameTemplate)
                        ? "{target}-{framework}-{rid}-{style}.zip"
                        : target.Publish.ZipNameTemplate!;
                    var zipName = ApplyTemplate(zipNameTemplate, tokens);
                    if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        zipName += ".zip";

                    var zipPath = string.IsNullOrWhiteSpace(target.Publish.ZipPath)
                        ? Path.Combine(Path.GetDirectoryName(outputPath)!, zipName)
                        : ResolvePath(projectRoot, ApplyTemplate(target.Publish.ZipPath!, tokens));
                    EnsurePathWithinRoot(projectRoot, zipPath, $"Target '{target.Name}' zip path");
                }
            }
        }

        var steps = new List<DotNetPublishStep>();
        var msiStagingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var msiManifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var msiHarvestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var distinctRuntimes = targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (spec.DotNet.Clean)
            steps.Add(new DotNetPublishStep { Key = "clean", Kind = DotNetPublishStepKind.Clean, Title = "Clean" });

        if (spec.DotNet.Restore)
        {
            if (spec.DotNet.NoRestoreInPublish && distinctRuntimes.Length > 0)
            {
                foreach (var rid in distinctRuntimes)
                {
                    steps.Add(new DotNetPublishStep
                    {
                        Key = $"restore:{rid}",
                        Kind = DotNetPublishStepKind.Restore,
                        Title = "Restore",
                        Runtime = rid
                    });
                }
            }
            else
            {
                steps.Add(new DotNetPublishStep { Key = "restore", Kind = DotNetPublishStepKind.Restore, Title = "Restore" });
            }
        }

        if (spec.DotNet.Build)
        {
            if (spec.DotNet.NoBuildInPublish && distinctRuntimes.Length > 0)
            {
                foreach (var rid in distinctRuntimes)
                {
                    steps.Add(new DotNetPublishStep
                    {
                        Key = $"build:{rid}",
                        Kind = DotNetPublishStepKind.Build,
                        Title = "Build",
                        Runtime = rid
                    });
                }
            }
            else
            {
                steps.Add(new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build" });
            }
        }

        foreach (var t in targets)
        {
            foreach (var combo in t.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
            {
                var key = $"publish:{t.Name}:{combo.Framework}:{combo.Runtime}:{combo.Style}";
                steps.Add(new DotNetPublishStep
                {
                    Key = key,
                    Kind = DotNetPublishStepKind.Publish,
                    Title = "Publish",
                    TargetName = t.Name,
                    Framework = combo.Framework,
                    Runtime = combo.Runtime,
                    Style = combo.Style
                });

                if (t.Publish.Service?.Lifecycle?.Enabled == true
                    && t.Publish.Service.Lifecycle.Mode == DotNetPublishServiceLifecycleMode.Step)
                {
                    steps.Add(new DotNetPublishStep
                    {
                        Key = $"service.lifecycle:{t.Name}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
                        Kind = DotNetPublishStepKind.ServiceLifecycle,
                        Title = "Service lifecycle",
                        TargetName = t.Name,
                        Framework = combo.Framework,
                        Runtime = combo.Runtime,
                        Style = combo.Style
                    });
                }

                foreach (var installer in installers.Where(i => string.Equals(i.PrepareFromTarget, t.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var msiStep = CreateMsiPrepareStep(projectRoot, cfg, installer, t.Name, combo);
                    if (!spec.DotNet.AllowOutputOutsideProjectRoot)
                    {
                        EnsurePathWithinRoot(projectRoot, msiStep.StagingPath!, $"Installer '{installer.Id}' staging path");
                        EnsurePathWithinRoot(projectRoot, msiStep.ManifestPath!, $"Installer '{installer.Id}' manifest path");
                        if (!string.IsNullOrWhiteSpace(msiStep.HarvestPath))
                            EnsurePathWithinRoot(projectRoot, msiStep.HarvestPath!, $"Installer '{installer.Id}' harvest path");
                    }

                    if (!msiStagingPaths.Add(msiStep.StagingPath!))
                    {
                        throw new InvalidOperationException(
                            $"Installer '{installer.Id}' staging path collision detected: {msiStep.StagingPath}. " +
                            "Use unique installer IDs or path templates.");
                    }

                    if (!msiManifestPaths.Add(msiStep.ManifestPath!))
                    {
                        throw new InvalidOperationException(
                            $"Installer '{installer.Id}' manifest path collision detected: {msiStep.ManifestPath}. " +
                            "Use unique installer IDs or path templates.");
                    }

                    if (!string.IsNullOrWhiteSpace(msiStep.HarvestPath) && !msiHarvestPaths.Add(msiStep.HarvestPath!))
                    {
                        throw new InvalidOperationException(
                            $"Installer '{installer.Id}' harvest path collision detected: {msiStep.HarvestPath}. " +
                            "Use unique installer IDs or path templates.");
                    }

                    steps.Add(msiStep);

                    var msiBuildStep = CreateMsiBuildStep(installer, t.Name, combo);
                    if (msiBuildStep is not null)
                    {
                        steps.Add(msiBuildStep);

                        var msiSignStep = CreateMsiSignStep(installer, t.Name, combo);
                        if (msiSignStep is not null)
                            steps.Add(msiSignStep);
                    }
                }
            }
        }

        foreach (var gate in benchmarkGates.Where(g => g.Enabled))
        {
            steps.Add(new DotNetPublishStep
            {
                Key = $"benchmark.extract:{gate.Id}",
                Kind = DotNetPublishStepKind.BenchmarkExtract,
                Title = "Benchmark extract",
                GateId = gate.Id
            });
            steps.Add(new DotNetPublishStep
            {
                Key = $"benchmark.gate:{gate.Id}",
                Kind = DotNetPublishStepKind.BenchmarkGate,
                Title = "Benchmark gate",
                GateId = gate.Id
            });
        }

        steps.Add(new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" });

        return new DotNetPublishPlan
        {
            ProjectRoot = projectRoot,
            AllowOutputOutsideProjectRoot = spec.DotNet.AllowOutputOutsideProjectRoot,
            AllowManifestOutsideProjectRoot = spec.DotNet.AllowManifestOutsideProjectRoot,
            LockedOutputGuard = spec.DotNet.LockedOutputGuard,
            OnLockedOutput = spec.DotNet.OnLockedOutput,
            LockedOutputSampleLimit = spec.DotNet.LockedOutputSampleLimit < 1 ? 1 : spec.DotNet.LockedOutputSampleLimit,
            Configuration = cfg,
            SolutionPath = solutionPath,
            Restore = spec.DotNet.Restore,
            Clean = spec.DotNet.Clean,
            Build = spec.DotNet.Build,
            NoRestoreInPublish = spec.DotNet.NoRestoreInPublish,
            NoBuildInPublish = spec.DotNet.NoBuildInPublish,
            MsBuildProperties = msbuildProps,
            Targets = targets.ToArray(),
            Installers = installers,
            BenchmarkGates = benchmarkGates,
            Outputs = outputs,
            Steps = steps.ToArray()
        };
    }

    private static DotNetPublishSpec ResolveProfile(DotNetPublishSpec spec)
    {
        var profiles = (spec.Profiles ?? Array.Empty<DotNetPublishProfile>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .ToArray();
        if (profiles.Length == 0)
            return spec;

        var profileName = !string.IsNullOrWhiteSpace(spec.Profile)
            ? spec.Profile!.Trim()
            : profiles.FirstOrDefault(p => p.Default)?.Name;
        if (string.IsNullOrWhiteSpace(profileName))
            return spec;

        var profile = profiles.FirstOrDefault(p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            throw new ArgumentException($"Profile '{profileName}' was not found.", nameof(spec));

        var selectedTargets = CloneTargets(spec.Targets ?? Array.Empty<DotNetPublishTarget>());
        var profileTargets = profile.Targets ?? Array.Empty<string>();
        if (profileTargets.Length > 0)
        {
            var names = new HashSet<string>(
                profileTargets.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var missing = names.Where(n => selectedTargets.All(t => !t.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (missing.Length > 0)
                throw new ArgumentException(
                    $"Profile '{profile.Name}' references unknown target(s): {string.Join(", ", missing)}.",
                    nameof(spec));

            selectedTargets = selectedTargets.Where(t => names.Contains(t.Name)).ToArray();
        }

        if (selectedTargets.Length == 0)
            throw new ArgumentException($"Profile '{profile.Name}' did not resolve any targets.", nameof(spec));

        var selectedTargetNames = new HashSet<string>(
            selectedTargets.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        var runtimes = (profile.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var frameworks = (profile.Frameworks ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (runtimes.Length > 0)
        {
            foreach (var t in selectedTargets)
                t.Publish.Runtimes = runtimes;
        }

        if (frameworks.Length > 0)
        {
            var first = frameworks[0];
            foreach (var t in selectedTargets)
            {
                t.Publish.Framework = first;
                t.Publish.Frameworks = frameworks;
            }
        }

        if (profile.Style.HasValue)
        {
            foreach (var t in selectedTargets)
            {
                t.Publish.Style = profile.Style.Value;
                t.Publish.Styles = new[] { profile.Style.Value };
            }
        }

        var dotNet = CloneDotNet(spec.DotNet);
        if (runtimes.Length > 0)
            dotNet.Runtimes = runtimes;

        return new DotNetPublishSpec
        {
            SchemaVersion = spec.SchemaVersion,
            Profile = profile.Name,
            Profiles = spec.Profiles ?? Array.Empty<DotNetPublishProfile>(),
            Projects = CloneProjects(spec.Projects),
            Installers = CloneInstallers(spec.Installers)
                .Where(i =>
                    string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                    || selectedTargetNames.Contains(i.PrepareFromTarget.Trim()))
                .ToArray(),
            BenchmarkGates = CloneBenchmarkGates(spec.BenchmarkGates),
            Matrix = CloneMatrix(spec.Matrix),
            DotNet = dotNet,
            Targets = selectedTargets,
            Outputs = CloneOutputs(spec.Outputs)
        };
    }

    private static DotNetPublishDotNetOptions CloneDotNet(DotNetPublishDotNetOptions dotNet)
    {
        dotNet ??= new DotNetPublishDotNetOptions();
        return new DotNetPublishDotNetOptions
        {
            ProjectRoot = dotNet.ProjectRoot,
            AllowOutputOutsideProjectRoot = dotNet.AllowOutputOutsideProjectRoot,
            AllowManifestOutsideProjectRoot = dotNet.AllowManifestOutsideProjectRoot,
            LockedOutputGuard = dotNet.LockedOutputGuard,
            OnLockedOutput = dotNet.OnLockedOutput,
            LockedOutputSampleLimit = dotNet.LockedOutputSampleLimit,
            SolutionPath = dotNet.SolutionPath,
            Configuration = dotNet.Configuration,
            Restore = dotNet.Restore,
            Clean = dotNet.Clean,
            Build = dotNet.Build,
            NoRestoreInPublish = dotNet.NoRestoreInPublish,
            NoBuildInPublish = dotNet.NoBuildInPublish,
            Runtimes = (dotNet.Runtimes ?? Array.Empty<string>()).ToArray(),
            MsBuildProperties = dotNet.MsBuildProperties is null
                ? null
                : new Dictionary<string, string>(dotNet.MsBuildProperties, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DotNetPublishOutputs CloneOutputs(DotNetPublishOutputs outputs)
    {
        outputs ??= new DotNetPublishOutputs();
        return new DotNetPublishOutputs
        {
            ManifestJsonPath = outputs.ManifestJsonPath,
            ManifestTextPath = outputs.ManifestTextPath,
            ChecksumsPath = outputs.ChecksumsPath,
            RunReportPath = outputs.RunReportPath
        };
    }

    private static DotNetPublishProject[] CloneProjects(DotNetPublishProject[] projects)
    {
        return (projects ?? Array.Empty<DotNetPublishProject>())
            .Where(p => p is not null)
            .Select(p => new DotNetPublishProject
            {
                Id = p.Id,
                Path = p.Path,
                Group = p.Group
            })
            .ToArray();
    }

    private static DotNetPublishInstaller[] CloneInstallers(DotNetPublishInstaller[] installers)
    {
        return (installers ?? Array.Empty<DotNetPublishInstaller>())
            .Where(i => i is not null)
            .Select(i => new DotNetPublishInstaller
            {
                Id = i.Id,
                PrepareFromTarget = i.PrepareFromTarget,
                StagingPath = i.StagingPath,
                ManifestPath = i.ManifestPath,
                InstallerProjectId = i.InstallerProjectId,
                InstallerProjectPath = i.InstallerProjectPath,
                Harvest = i.Harvest,
                HarvestPath = i.HarvestPath,
                HarvestDirectoryRefId = i.HarvestDirectoryRefId,
                HarvestComponentGroupId = i.HarvestComponentGroupId,
                Versioning = CloneMsiVersionOptions(i.Versioning),
                Sign = CloneSignOptions(i.Sign),
                ClientLicense = CloneMsiClientLicenseOptions(i.ClientLicense)
            })
            .ToArray();
    }

    private static DotNetPublishBenchmarkGate[] CloneBenchmarkGates(DotNetPublishBenchmarkGate[]? gates)
    {
        return (gates ?? Array.Empty<DotNetPublishBenchmarkGate>())
            .Where(g => g is not null)
            .Select(g => new DotNetPublishBenchmarkGate
            {
                Id = g.Id,
                Enabled = g.Enabled,
                SourcePath = g.SourcePath,
                BaselinePath = g.BaselinePath,
                BaselineMode = g.BaselineMode,
                FailOnNew = g.FailOnNew,
                RelativeTolerance = g.RelativeTolerance,
                AbsoluteToleranceMs = g.AbsoluteToleranceMs,
                OnRegression = g.OnRegression,
                OnMissingMetric = g.OnMissingMetric,
                Metrics = CloneBenchmarkMetrics(g.Metrics)
            })
            .ToArray();
    }

    private static DotNetPublishBenchmarkMetric[] CloneBenchmarkMetrics(DotNetPublishBenchmarkMetric[]? metrics)
    {
        return (metrics ?? Array.Empty<DotNetPublishBenchmarkMetric>())
            .Where(m => m is not null)
            .Select(m => new DotNetPublishBenchmarkMetric
            {
                Name = m.Name,
                Source = m.Source,
                Path = m.Path,
                Pattern = m.Pattern,
                Group = m.Group,
                Aggregation = m.Aggregation,
                Required = m.Required
            })
            .ToArray();
    }

    private static DotNetPublishMsiVersionOptions? CloneMsiVersionOptions(DotNetPublishMsiVersionOptions? versioning)
    {
        if (versioning is null) return null;
        return new DotNetPublishMsiVersionOptions
        {
            Enabled = versioning.Enabled,
            Major = versioning.Major,
            Minor = versioning.Minor,
            FloorDateUtc = versioning.FloorDateUtc,
            Monotonic = versioning.Monotonic,
            StatePath = versioning.StatePath,
            PropertyName = versioning.PropertyName,
            PatchCap = versioning.PatchCap
        };
    }

    private static DotNetPublishMsiClientLicenseOptions? CloneMsiClientLicenseOptions(DotNetPublishMsiClientLicenseOptions? options)
    {
        if (options is null) return null;
        return new DotNetPublishMsiClientLicenseOptions
        {
            Enabled = options.Enabled,
            ClientId = options.ClientId,
            Path = options.Path,
            PathTemplate = options.PathTemplate,
            PropertyName = options.PropertyName,
            OnMissingFile = options.OnMissingFile
        };
    }

    private static DotNetPublishMatrix CloneMatrix(DotNetPublishMatrix matrix)
    {
        matrix ??= new DotNetPublishMatrix();
        return new DotNetPublishMatrix
        {
            Runtimes = NormalizeStrings(matrix.Runtimes),
            Frameworks = NormalizeStrings(matrix.Frameworks),
            Styles = NormalizeStyles(matrix.Styles),
            Include = CloneMatrixRules(matrix.Include),
            Exclude = CloneMatrixRules(matrix.Exclude)
        };
    }

    private static DotNetPublishMatrixRule[] CloneMatrixRules(DotNetPublishMatrixRule[] rules)
    {
        return (rules ?? Array.Empty<DotNetPublishMatrixRule>())
            .Where(r => r is not null)
            .Select(r => new DotNetPublishMatrixRule
            {
                Targets = NormalizeStrings(r.Targets),
                Runtime = r.Runtime,
                Framework = r.Framework,
                Style = r.Style
            })
            .ToArray();
    }

    private static DotNetPublishTarget[] CloneTargets(DotNetPublishTarget[] targets)
    {
        return (targets ?? Array.Empty<DotNetPublishTarget>())
            .Where(t => t is not null)
            .Select(t => new DotNetPublishTarget
            {
                Name = t.Name,
                ProjectId = t.ProjectId,
                ProjectPath = t.ProjectPath,
                Kind = t.Kind,
                Publish = new DotNetPublishPublishOptions
                {
                    Style = t.Publish?.Style ?? DotNetPublishStyle.Portable,
                    Styles = (t.Publish?.Styles ?? Array.Empty<DotNetPublishStyle>()).ToArray(),
                    Framework = t.Publish?.Framework ?? string.Empty,
                    Frameworks = (t.Publish?.Frameworks ?? Array.Empty<string>()).ToArray(),
                    Runtimes = (t.Publish?.Runtimes ?? Array.Empty<string>()).ToArray(),
                    OutputPath = t.Publish?.OutputPath,
                    UseStaging = t.Publish?.UseStaging ?? true,
                    ClearOutput = t.Publish?.ClearOutput ?? true,
                    Slim = t.Publish?.Slim ?? true,
                    KeepSymbols = t.Publish?.KeepSymbols ?? false,
                    KeepDocs = t.Publish?.KeepDocs ?? false,
                    PruneReferences = t.Publish?.PruneReferences ?? true,
                    Zip = t.Publish?.Zip ?? false,
                    ZipPath = t.Publish?.ZipPath,
                    ZipNameTemplate = t.Publish?.ZipNameTemplate,
                    RenameTo = t.Publish?.RenameTo,
                    ReadyToRun = t.Publish?.ReadyToRun,
                    Sign = CloneSignOptions(t.Publish?.Sign),
                    Service = CloneServicePackageOptions(t.Publish?.Service),
                    State = CloneStatePreservationOptions(t.Publish?.State)
                }
            })
            .ToArray();
    }

    private static DotNetPublishSignOptions? CloneSignOptions(DotNetPublishSignOptions? sign)
    {
        if (sign is null) return null;
        return new DotNetPublishSignOptions
        {
            Enabled = sign.Enabled,
            ToolPath = sign.ToolPath,
            OnMissingTool = sign.OnMissingTool,
            OnSignFailure = sign.OnSignFailure,
            Thumbprint = sign.Thumbprint,
            SubjectName = sign.SubjectName,
            TimestampUrl = sign.TimestampUrl,
            Description = sign.Description,
            Url = sign.Url,
            Csp = sign.Csp,
            KeyContainer = sign.KeyContainer
        };
    }

    private static DotNetPublishServicePackageOptions? CloneServicePackageOptions(DotNetPublishServicePackageOptions? service)
    {
        if (service is null) return null;
        return new DotNetPublishServicePackageOptions
        {
            ServiceName = service.ServiceName,
            DisplayName = service.DisplayName,
            Description = service.Description,
            ExecutablePath = service.ExecutablePath,
            Arguments = service.Arguments,
            GenerateInstallScript = service.GenerateInstallScript,
            GenerateUninstallScript = service.GenerateUninstallScript,
            GenerateRunOnceScript = service.GenerateRunOnceScript,
            Lifecycle = CloneServiceLifecycleOptions(service.Lifecycle),
            Recovery = CloneServiceRecoveryOptions(service.Recovery),
            ConfigBootstrap = CloneConfigBootstrapRules(service.ConfigBootstrap)
        };
    }

    private static DotNetPublishStatePreservationOptions? CloneStatePreservationOptions(DotNetPublishStatePreservationOptions? state)
    {
        if (state is null) return null;
        return new DotNetPublishStatePreservationOptions
        {
            Enabled = state.Enabled,
            StoragePath = state.StoragePath,
            ClearStorage = state.ClearStorage,
            OnMissingSource = state.OnMissingSource,
            OnRestoreFailure = state.OnRestoreFailure,
            Rules = CloneStateRules(state.Rules)
        };
    }

    private static DotNetPublishStateRule[] CloneStateRules(DotNetPublishStateRule[]? rules)
    {
        return (rules ?? Array.Empty<DotNetPublishStateRule>())
            .Where(r => r is not null)
            .Select(r => new DotNetPublishStateRule
            {
                SourcePath = r.SourcePath,
                DestinationPath = r.DestinationPath,
                Overwrite = r.Overwrite
            })
            .ToArray();
    }

    private static DotNetPublishServiceLifecycleOptions? CloneServiceLifecycleOptions(DotNetPublishServiceLifecycleOptions? lifecycle)
    {
        if (lifecycle is null) return null;
        return new DotNetPublishServiceLifecycleOptions
        {
            Enabled = lifecycle.Enabled,
            Mode = lifecycle.Mode,
            StopIfExists = lifecycle.StopIfExists,
            DeleteIfExists = lifecycle.DeleteIfExists,
            Install = lifecycle.Install,
            Start = lifecycle.Start,
            Verify = lifecycle.Verify,
            StopTimeoutSeconds = lifecycle.StopTimeoutSeconds,
            WhatIf = lifecycle.WhatIf,
            OnUnsupportedPlatform = lifecycle.OnUnsupportedPlatform,
            OnExecutionFailure = lifecycle.OnExecutionFailure
        };
    }

    private static DotNetPublishServiceRecoveryOptions? CloneServiceRecoveryOptions(DotNetPublishServiceRecoveryOptions? recovery)
    {
        if (recovery is null) return null;
        return new DotNetPublishServiceRecoveryOptions
        {
            Enabled = recovery.Enabled,
            ResetPeriodSeconds = recovery.ResetPeriodSeconds,
            RestartDelaySeconds = recovery.RestartDelaySeconds,
            ApplyToNonCrashFailures = recovery.ApplyToNonCrashFailures,
            OnFailure = recovery.OnFailure
        };
    }

    private static DotNetPublishConfigBootstrapRule[] CloneConfigBootstrapRules(DotNetPublishConfigBootstrapRule[]? rules)
    {
        return (rules ?? Array.Empty<DotNetPublishConfigBootstrapRule>())
            .Where(r => r is not null)
            .Select(r => new DotNetPublishConfigBootstrapRule
            {
                SourcePath = r.SourcePath,
                DestinationPath = r.DestinationPath,
                Overwrite = r.Overwrite,
                OnMissingSource = r.OnMissingSource
            })
            .ToArray();
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DotNetPublishStyle[] NormalizeStyles(IEnumerable<DotNetPublishStyle>? values)
    {
        return (values ?? Array.Empty<DotNetPublishStyle>())
            .Distinct()
            .ToArray();
    }

    private static Dictionary<string, string> BuildProjectCatalog(IEnumerable<DotNetPublishProject>? projects, string projectRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects ?? Array.Empty<DotNetPublishProject>())
        {
            if (p is null) continue;
            if (string.IsNullOrWhiteSpace(p.Id)) continue;
            if (string.IsNullOrWhiteSpace(p.Path))
                throw new ArgumentException($"Project catalog entry '{p.Id}' is missing Path.");

            var id = p.Id.Trim();
            var path = ResolvePath(projectRoot, p.Path);
            if (map.ContainsKey(id))
                throw new ArgumentException($"Duplicate project ID in Projects catalog: {id}");

            map[id] = path;
        }

        return map;
    }

    private static string ResolveTargetProjectPath(string projectRoot, DotNetPublishTarget target, IReadOnlyDictionary<string, string> catalog)
    {
        var hasProjectPath = !string.IsNullOrWhiteSpace(target.ProjectPath);
        var hasProjectId = !string.IsNullOrWhiteSpace(target.ProjectId);

        if (!hasProjectPath && !hasProjectId)
            throw new ArgumentException($"Target '{target.Name}' requires ProjectPath or ProjectId.");

        if (hasProjectPath)
            return ResolvePath(projectRoot, target.ProjectPath);

        var id = target.ProjectId!.Trim();
        if (!catalog.TryGetValue(id, out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Target '{target.Name}' references unknown ProjectId '{id}'.");
        return path;
    }

    private static DotNetPublishInstallerPlan[] BuildInstallerPlans(
        IEnumerable<DotNetPublishInstaller>? installers,
        IEnumerable<DotNetPublishTargetPlan>? targets,
        IReadOnlyDictionary<string, string> projectCatalog,
        string projectRoot)
    {
        var targetNames = new HashSet<string>(
            (targets ?? Array.Empty<DotNetPublishTargetPlan>())
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);

        var plans = new List<DotNetPublishInstallerPlan>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var installer in installers ?? Array.Empty<DotNetPublishInstaller>())
        {
            if (installer is null) continue;
            var id = (installer.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Installers[].Id is required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Duplicate installer ID detected: {id}");

            var sourceTarget = (installer.PrepareFromTarget ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceTarget))
                throw new ArgumentException($"Installers['{id}'].PrepareFromTarget is required.");
            if (!targetNames.Contains(sourceTarget))
            {
                throw new ArgumentException(
                    $"Installer '{id}' references unknown PrepareFromTarget '{sourceTarget}'.");
            }

            plans.Add(new DotNetPublishInstallerPlan
            {
                Id = id,
                PrepareFromTarget = sourceTarget,
                StagingPath = installer.StagingPath,
                ManifestPath = installer.ManifestPath,
                InstallerProjectId = installer.InstallerProjectId,
                InstallerProjectPath = ResolveInstallerProjectPath(id, installer, projectCatalog, projectRoot),
                Harvest = installer.Harvest,
                HarvestPath = installer.HarvestPath,
                HarvestDirectoryRefId = installer.HarvestDirectoryRefId,
                HarvestComponentGroupId = installer.HarvestComponentGroupId,
                Versioning = NormalizeInstallerVersioning(id, installer.Versioning),
                Sign = CloneSignOptions(installer.Sign),
                ClientLicense = NormalizeInstallerClientLicense(id, installer.ClientLicense)
            });
        }

        return plans.ToArray();
    }

    private static DotNetPublishBenchmarkGatePlan[] BuildBenchmarkGatePlans(
        IEnumerable<DotNetPublishBenchmarkGate>? gates,
        string projectRoot)
    {
        var plans = new List<DotNetPublishBenchmarkGatePlan>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gate in gates ?? Array.Empty<DotNetPublishBenchmarkGate>())
        {
            if (gate is null) continue;
            var id = (gate.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("BenchmarkGates[].Id is required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Duplicate benchmark gate ID detected: {id}");

            var sourcePath = ResolvePath(projectRoot, gate.SourcePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(gate.SourcePath))
                throw new ArgumentException($"Benchmark gate '{id}' SourcePath is required.");

            var baselinePath = ResolvePath(projectRoot, gate.BaselinePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(gate.BaselinePath))
                throw new ArgumentException($"Benchmark gate '{id}' BaselinePath is required.");

            if (gate.RelativeTolerance < 0)
                throw new ArgumentException($"Benchmark gate '{id}' RelativeTolerance must be >= 0.");
            if (gate.AbsoluteToleranceMs < 0)
                throw new ArgumentException($"Benchmark gate '{id}' AbsoluteToleranceMs must be >= 0.");

            var metrics = NormalizeBenchmarkMetrics(id, gate.Metrics);
            if (metrics.Length == 0)
                throw new ArgumentException($"Benchmark gate '{id}' requires at least one metric.");

            plans.Add(new DotNetPublishBenchmarkGatePlan
            {
                Id = id,
                Enabled = gate.Enabled,
                SourcePath = sourcePath,
                BaselinePath = baselinePath,
                BaselineMode = gate.BaselineMode,
                FailOnNew = gate.FailOnNew,
                RelativeTolerance = gate.RelativeTolerance,
                AbsoluteToleranceMs = gate.AbsoluteToleranceMs,
                OnRegression = gate.OnRegression,
                OnMissingMetric = gate.OnMissingMetric,
                Metrics = metrics
            });
        }

        return plans.ToArray();
    }

    private static DotNetPublishBenchmarkMetricPlan[] NormalizeBenchmarkMetrics(
        string gateId,
        IEnumerable<DotNetPublishBenchmarkMetric>? metrics)
    {
        var list = new List<DotNetPublishBenchmarkMetricPlan>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var metric in metrics ?? Array.Empty<DotNetPublishBenchmarkMetric>())
        {
            if (metric is null) continue;
            var name = (metric.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException($"Benchmark gate '{gateId}' has metric with empty Name.");
            if (!names.Add(name))
                throw new ArgumentException($"Benchmark gate '{gateId}' has duplicate metric name '{name}'.");

            var group = metric.Group < 0 ? 0 : metric.Group;
            if (metric.Source == DotNetPublishBenchmarkMetricSource.JsonPath)
            {
                if (string.IsNullOrWhiteSpace(metric.Path))
                    throw new ArgumentException($"Benchmark gate '{gateId}' metric '{name}' requires Path for JsonPath source.");
            }
            else if (metric.Source == DotNetPublishBenchmarkMetricSource.Regex)
            {
                if (string.IsNullOrWhiteSpace(metric.Pattern))
                    throw new ArgumentException($"Benchmark gate '{gateId}' metric '{name}' requires Pattern for Regex source.");
                if (group < 1)
                    throw new ArgumentException($"Benchmark gate '{gateId}' metric '{name}' Group must be >= 1 for Regex source.");
            }

            list.Add(new DotNetPublishBenchmarkMetricPlan
            {
                Name = name,
                Source = metric.Source,
                Path = string.IsNullOrWhiteSpace(metric.Path) ? null : metric.Path!.Trim(),
                Pattern = string.IsNullOrWhiteSpace(metric.Pattern) ? null : metric.Pattern!.Trim(),
                Group = group,
                Aggregation = metric.Aggregation,
                Required = metric.Required
            });
        }

        return list.ToArray();
    }

    private static DotNetPublishStep CreateMsiPrepareStep(
        string projectRoot,
        string configuration,
        DotNetPublishInstallerPlan installer,
        string targetName,
        DotNetPublishTargetCombination combo)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["installer"] = installer.Id,
            ["target"] = targetName,
            ["rid"] = combo.Runtime,
            ["framework"] = combo.Framework,
            ["style"] = combo.Style.ToString(),
            ["configuration"] = configuration
        };

        var stagingTemplate = string.IsNullOrWhiteSpace(installer.StagingPath)
            ? DefaultMsiPrepareStagingPathTemplate
            : installer.StagingPath!;
        var manifestTemplate = string.IsNullOrWhiteSpace(installer.ManifestPath)
            ? DefaultMsiPrepareManifestPathTemplate
            : installer.ManifestPath!;
        var harvestTemplate = string.IsNullOrWhiteSpace(installer.HarvestPath)
            ? DefaultMsiHarvestPathTemplate
            : installer.HarvestPath!;

        var stagingPath = ResolvePath(projectRoot, ApplyTemplate(stagingTemplate, tokens));
        var manifestPath = ResolvePath(projectRoot, ApplyTemplate(manifestTemplate, tokens));
        var harvestPath = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
            ? ResolvePath(projectRoot, ApplyTemplate(harvestTemplate, tokens))
            : null;
        var harvestDirectoryRefId = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
            ? SanitizeWixIdentifier(
                string.IsNullOrWhiteSpace(installer.HarvestDirectoryRefId) ? "INSTALLFOLDER" : installer.HarvestDirectoryRefId!,
                "INSTALLFOLDER")
            : null;
        var harvestComponentGroupId = installer.Harvest == DotNetPublishMsiHarvestMode.Auto
            ? SanitizeWixIdentifier(
                ApplyTemplate(
                    string.IsNullOrWhiteSpace(installer.HarvestComponentGroupId)
                        ? "Harvest_{installer}_{target}_{framework}_{rid}_{style}"
                        : installer.HarvestComponentGroupId!,
                    tokens),
                "Harvest")
            : null;

        return new DotNetPublishStep
        {
            Key = $"msi.prepare:{installer.Id}:{targetName}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
            Kind = DotNetPublishStepKind.MsiPrepare,
            Title = "MSI prepare",
            InstallerId = installer.Id,
            TargetName = targetName,
            Framework = combo.Framework,
            Runtime = combo.Runtime,
            Style = combo.Style,
            StagingPath = stagingPath,
            ManifestPath = manifestPath,
            HarvestPath = harvestPath,
            HarvestDirectoryRefId = harvestDirectoryRefId,
            HarvestComponentGroupId = harvestComponentGroupId,
            InstallerProjectPath = installer.InstallerProjectPath
        };
    }

    private static DotNetPublishStep? CreateMsiBuildStep(
        DotNetPublishInstallerPlan installer,
        string targetName,
        DotNetPublishTargetCombination combo)
    {
        if (string.IsNullOrWhiteSpace(installer.InstallerProjectPath))
            return null;

        return new DotNetPublishStep
        {
            Key = $"msi.build:{installer.Id}:{targetName}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
            Kind = DotNetPublishStepKind.MsiBuild,
            Title = "MSI build",
            InstallerId = installer.Id,
            TargetName = targetName,
            Framework = combo.Framework,
            Runtime = combo.Runtime,
            Style = combo.Style,
            InstallerProjectPath = installer.InstallerProjectPath
        };
    }

    private static DotNetPublishStep? CreateMsiSignStep(
        DotNetPublishInstallerPlan installer,
        string targetName,
        DotNetPublishTargetCombination combo)
    {
        if (installer.Sign?.Enabled != true)
            return null;

        return new DotNetPublishStep
        {
            Key = $"msi.sign:{installer.Id}:{targetName}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
            Kind = DotNetPublishStepKind.MsiSign,
            Title = "MSI sign",
            InstallerId = installer.Id,
            TargetName = targetName,
            Framework = combo.Framework,
            Runtime = combo.Runtime,
            Style = combo.Style
        };
    }

    private static string? ResolveInstallerProjectPath(
        string installerId,
        DotNetPublishInstaller installer,
        IReadOnlyDictionary<string, string> projectCatalog,
        string projectRoot)
    {
        var hasId = !string.IsNullOrWhiteSpace(installer.InstallerProjectId);
        var hasPath = !string.IsNullOrWhiteSpace(installer.InstallerProjectPath);

        if (!hasId && !hasPath) return null;

        string resolvedPath;
        if (hasPath)
        {
            resolvedPath = ResolvePath(projectRoot, installer.InstallerProjectPath!);
        }
        else
        {
            var id = installer.InstallerProjectId!.Trim();
            if (!projectCatalog.TryGetValue(id, out var path) || string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"Installer '{installerId}' references unknown InstallerProjectId '{id}'.");
            resolvedPath = path;
        }

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"Installer project path not found for installer '{installerId}': {resolvedPath}",
                resolvedPath);
        }

        return resolvedPath;
    }

    private static DotNetPublishMsiVersionOptions? NormalizeInstallerVersioning(
        string installerId,
        DotNetPublishMsiVersionOptions? versioning)
    {
        var clone = CloneMsiVersionOptions(versioning);
        if (clone is null) return null;
        if (!clone.Enabled) return clone;

        if (clone.Major < 0 || clone.Major > 255)
            throw new ArgumentException($"Installer '{installerId}' Versioning.Major must be between 0 and 255.");
        if (clone.Minor < 0 || clone.Minor > 255)
            throw new ArgumentException($"Installer '{installerId}' Versioning.Minor must be between 0 and 255.");
        if (clone.PatchCap < 1 || clone.PatchCap > 65535)
            throw new ArgumentException($"Installer '{installerId}' Versioning.PatchCap must be between 1 and 65535.");

        if (!string.IsNullOrWhiteSpace(clone.FloorDateUtc) && !TryParseUtcDate(clone.FloorDateUtc!, out _))
            throw new ArgumentException(
                $"Installer '{installerId}' Versioning.FloorDateUtc must be yyyy-MM-dd or yyyyMMdd.");

        if (string.IsNullOrWhiteSpace(clone.PropertyName))
            clone.PropertyName = "ProductVersion";
        else
            clone.PropertyName = clone.PropertyName!.Trim();

        return clone;
    }

    private static DotNetPublishMsiClientLicenseOptions? NormalizeInstallerClientLicense(
        string installerId,
        DotNetPublishMsiClientLicenseOptions? options)
    {
        var clone = CloneMsiClientLicenseOptions(options);
        if (clone is null) return null;
        if (!clone.Enabled) return clone;

        clone.ClientId = string.IsNullOrWhiteSpace(clone.ClientId)
            ? null
            : clone.ClientId!.Trim();
        clone.Path = string.IsNullOrWhiteSpace(clone.Path)
            ? null
            : clone.Path!.Trim();
        clone.PathTemplate = string.IsNullOrWhiteSpace(clone.PathTemplate)
            ? "Installer/Clients/{clientId}/{target}.txlic"
            : clone.PathTemplate!.Trim();
        clone.PropertyName = string.IsNullOrWhiteSpace(clone.PropertyName)
            ? "ClientLicensePath"
            : clone.PropertyName!.Trim();

        if (string.IsNullOrWhiteSpace(clone.Path) && string.IsNullOrWhiteSpace(clone.ClientId))
        {
            throw new ArgumentException(
                $"Installer '{installerId}' ClientLicense requires Path or ClientId when Enabled=true.");
        }

        return clone;
    }

    private static DotNetPublishStatePreservationOptions? NormalizeStatePreservationOptions(
        string targetName,
        DotNetPublishStatePreservationOptions? state)
    {
        var clone = CloneStatePreservationOptions(state);
        if (clone is null) return null;
        if (!clone.Enabled) return clone;

        clone.StoragePath = string.IsNullOrWhiteSpace(clone.StoragePath)
            ? null
            : clone.StoragePath!.Trim();

        var rules = CloneStateRules(clone.Rules);
        if (rules.Length == 0)
            throw new ArgumentException($"Target '{targetName}' State requires at least one rule when Enabled=true.");

        for (var i = 0; i < rules.Length; i++)
        {
            var rule = rules[i];
            if (string.IsNullOrWhiteSpace(rule.SourcePath))
            {
                throw new ArgumentException(
                    $"Target '{targetName}' State rule at index {i} requires SourcePath.");
            }

            rule.SourcePath = rule.SourcePath.Trim();
            rule.DestinationPath = string.IsNullOrWhiteSpace(rule.DestinationPath)
                ? rule.SourcePath
                : rule.DestinationPath!.Trim();
        }

        clone.Rules = rules;
        return clone;
    }

    private static bool TryParseUtcDate(string value, out DateTime date)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyyMMdd" };
        return DateTime.TryParseExact(
            value.Trim(),
            formats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out date);
    }

    private static DotNetPublishTargetCombination[] BuildPublishCombos(
        string targetName,
        string[] frameworks,
        string[] runtimes,
        DotNetPublishStyle[] styles,
        DotNetPublishMatrix? matrix)
    {
        var combos = new List<DotNetPublishTargetCombination>();
        foreach (var framework in frameworks)
        {
            foreach (var runtime in runtimes)
            {
                foreach (var style in styles)
                {
                    combos.Add(new DotNetPublishTargetCombination
                    {
                        Framework = framework,
                        Runtime = runtime,
                        Style = style
                    });
                }
            }
        }

        var include = matrix?.Include ?? Array.Empty<DotNetPublishMatrixRule>();
        if (include.Length > 0)
        {
            combos = combos
                .Where(c => include.Any(rule => RuleMatches(targetName, c, rule)))
                .ToList();
        }

        var exclude = matrix?.Exclude ?? Array.Empty<DotNetPublishMatrixRule>();
        if (exclude.Length > 0)
        {
            combos = combos
                .Where(c => !exclude.Any(rule => RuleMatches(targetName, c, rule)))
                .ToList();
        }

        return combos
            .OrderBy(c => c.Framework, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Runtime, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Style.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool RuleMatches(string targetName, DotNetPublishTargetCombination combo, DotNetPublishMatrixRule? rule)
    {
        if (rule is null) return false;

        var targetPatterns = NormalizeStrings(rule.Targets);
        if (targetPatterns.Length > 0 && !targetPatterns.Any(p => WildcardMatch(targetName, p)))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Runtime) && !WildcardMatch(combo.Runtime, rule.Runtime!.Trim()))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Framework) && !WildcardMatch(combo.Framework, rule.Framework!.Trim()))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.Style) && !WildcardMatch(combo.Style.ToString(), rule.Style!.Trim()))
            return false;

        return true;
    }

    private static bool WildcardMatch(string value, string pattern)
    {
        var pat = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return Regex.IsMatch(value ?? string.Empty, pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string SanitizeWixIdentifier(string? value, string fallback)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
        if (string.IsNullOrWhiteSpace(candidate)) candidate = fallback;
        candidate = Regex.Replace(candidate, @"[^A-Za-z0-9_]", "_");
        if (string.IsNullOrWhiteSpace(candidate)) candidate = fallback;
        if (char.IsDigit(candidate[0])) candidate = "_" + candidate;
        return candidate;
    }

}

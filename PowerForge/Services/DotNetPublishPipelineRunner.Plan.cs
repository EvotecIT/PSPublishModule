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

    private const string DefaultStorePackageOutputPathTemplate =
        "Artifacts/DotNetPublish/Store/{storePackage}/{target}/{rid}/{framework}/{style}";

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
                MsBuildProperties = t.Publish.MsBuildProperties is null
                    ? null
                    : new Dictionary<string, string>(t.Publish.MsBuildProperties, StringComparer.OrdinalIgnoreCase),
                StyleOverrides = CloneStyleOverrides(t.Publish.StyleOverrides),
                Sign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    spec.SigningProfiles,
                    t.Publish.SignProfile,
                    t.Publish.Sign,
                    t.Publish.SignOverrides,
                    $"Target '{t.Name.Trim()}'"),
                SignProfile = t.Publish.SignProfile,
                SignOverrides = DotNetPublishSigningProfileResolver.CloneSignPatch(t.Publish.SignOverrides),
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

        var bundles = BuildBundlePlans(spec.Bundles, targets, projectRoot, spec.SigningProfiles);
        targets = OrderTargetsForBundleIncludes(targets, bundles);
        var installers = BuildInstallerPlans(spec.Installers, bundles, targets, projectCatalog, projectRoot, spec.SigningProfiles);
        var storePackages = BuildStorePackagePlans(spec.StorePackages, targets, projectCatalog, projectRoot);

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
        var hooks = NormalizeCommandHooks(spec.Hooks);

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

            foreach (var bundle in bundles)
            {
                var sourceTarget = targets.FirstOrDefault(t => string.Equals(t.Name, bundle.PrepareFromTarget, StringComparison.OrdinalIgnoreCase));
                if (sourceTarget is null)
                    continue;

                foreach (var combo in sourceTarget.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                {
                    if (!BundleMatchesCombo(bundle, combo))
                        continue;

                    var bundleStep = CreateBundleStep(projectRoot, cfg, bundle, sourceTarget.Name, combo);
                    EnsurePathWithinRoot(projectRoot, bundleStep.BundleOutputPath!, $"Bundle '{bundle.Id}' output path");
                    if (!string.IsNullOrWhiteSpace(bundleStep.BundleZipPath))
                        EnsurePathWithinRoot(projectRoot, bundleStep.BundleZipPath!, $"Bundle '{bundle.Id}' zip path");
                }
            }

            foreach (var storePackage in storePackages)
            {
                var sourceTarget = targets.FirstOrDefault(t => string.Equals(t.Name, storePackage.PrepareFromTarget, StringComparison.OrdinalIgnoreCase));
                if (sourceTarget is null)
                    continue;

                foreach (var combo in sourceTarget.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                {
                    if (!StorePackageMatchesCombo(storePackage, combo))
                        continue;

                    var storeStep = CreateStorePackageStep(projectRoot, cfg, storePackage, sourceTarget.Name, combo);
                    EnsurePathWithinRoot(projectRoot, storeStep.StorePackageOutputPath!, $"Store package '{storePackage.Id}' output path");
                }
            }
        }

        var steps = new List<DotNetPublishStep>();
        var bundleOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bundleZipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var msiStagingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var msiManifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var msiHarvestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storeOutputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var distinctRuntimes = targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (spec.DotNet.Clean)
            steps.Add(new DotNetPublishStep { Key = "clean", Kind = DotNetPublishStepKind.Clean, Title = "Clean" });

        AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.BeforeRestore, cfg, targetName: null, framework: null, runtime: null, style: null, bundleId: null);

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

        AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.BeforeBuild, cfg, targetName: null, framework: null, runtime: null, style: null, bundleId: null);

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
                AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.BeforeTargetPublish, cfg, t.Name, combo.Framework, combo.Runtime, combo.Style, bundleId: null);

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

                AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.AfterTargetPublish, cfg, t.Name, combo.Framework, combo.Runtime, combo.Style, bundleId: null);

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

                foreach (var bundle in bundles.Where(b => string.Equals(b.PrepareFromTarget, t.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!BundleMatchesCombo(bundle, combo))
                        continue;

                    var bundleStep = CreateBundleStep(projectRoot, cfg, bundle, t.Name, combo);
                    if (!spec.DotNet.AllowOutputOutsideProjectRoot)
                    {
                        EnsurePathWithinRoot(projectRoot, bundleStep.BundleOutputPath!, $"Bundle '{bundle.Id}' output path");
                        if (!string.IsNullOrWhiteSpace(bundleStep.BundleZipPath))
                            EnsurePathWithinRoot(projectRoot, bundleStep.BundleZipPath!, $"Bundle '{bundle.Id}' zip path");
                    }

                    if (!bundleOutputPaths.Add(bundleStep.BundleOutputPath!))
                    {
                        throw new InvalidOperationException(
                            $"Bundle '{bundle.Id}' output path collision detected: {bundleStep.BundleOutputPath}. " +
                            "Use unique bundle IDs or path templates.");
                    }

                    if (!string.IsNullOrWhiteSpace(bundleStep.BundleZipPath) && !bundleZipPaths.Add(bundleStep.BundleZipPath!))
                    {
                        throw new InvalidOperationException(
                            $"Bundle '{bundle.Id}' zip path collision detected: {bundleStep.BundleZipPath}. " +
                            "Use unique bundle IDs or path templates.");
                    }

                    AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.BeforeBundle, cfg, t.Name, combo.Framework, combo.Runtime, combo.Style, bundle.Id);
                    steps.Add(bundleStep);
                    AddCommandHookSteps(steps, hooks, DotNetPublishCommandHookPhase.AfterBundle, cfg, t.Name, combo.Framework, combo.Runtime, combo.Style, bundle.Id);
                }

                foreach (var installer in installers.Where(i => string.Equals(i.PrepareFromTarget, t.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!InstallerMatchesCombo(installer, combo))
                        continue;

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

                foreach (var storePackage in storePackages.Where(i => string.Equals(i.PrepareFromTarget, t.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!StorePackageMatchesCombo(storePackage, combo))
                        continue;

                    var storeStep = CreateStorePackageStep(projectRoot, cfg, storePackage, t.Name, combo);
                    if (!spec.DotNet.AllowOutputOutsideProjectRoot)
                        EnsurePathWithinRoot(projectRoot, storeStep.StorePackageOutputPath!, $"Store package '{storePackage.Id}' output path");

                    if (!storeOutputPaths.Add(storeStep.StorePackageOutputPath!))
                    {
                        throw new InvalidOperationException(
                            $"Store package '{storePackage.Id}' output path collision detected: {storeStep.StorePackageOutputPath}. " +
                            "Use unique store package IDs or path templates.");
                    }

                    steps.Add(storeStep);
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
            Bundles = bundles,
            Installers = installers,
            StorePackages = storePackages,
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
            SigningProfiles = DotNetPublishSigningProfileResolver.CloneSigningProfiles(spec.SigningProfiles),
            Bundles = CloneBundles(spec.Bundles)
                .Where(b =>
                    string.IsNullOrWhiteSpace(b.PrepareFromTarget)
                    || selectedTargetNames.Contains(b.PrepareFromTarget.Trim()))
                .ToArray(),
            Installers = CloneInstallers(spec.Installers)
                .Where(i =>
                    string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                    || selectedTargetNames.Contains(i.PrepareFromTarget.Trim()))
                .ToArray(),
            StorePackages = CloneStorePackages(spec.StorePackages)
                .Where(i =>
                    string.IsNullOrWhiteSpace(i.PrepareFromTarget)
                    || selectedTargetNames.Contains(i.PrepareFromTarget.Trim()))
                .ToArray(),
            BenchmarkGates = CloneBenchmarkGates(spec.BenchmarkGates),
            Hooks = CloneCommandHooks(spec.Hooks),
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
                PrepareFromBundleId = i.PrepareFromBundleId,
                Runtimes = NormalizeStrings(i.Runtimes),
                Frameworks = NormalizeStrings(i.Frameworks),
                Styles = NormalizeStyles(i.Styles),
                StagingPath = i.StagingPath,
                ManifestPath = i.ManifestPath,
                InstallerProjectId = i.InstallerProjectId,
                InstallerProjectPath = i.InstallerProjectPath,
                Harvest = i.Harvest,
                HarvestPath = i.HarvestPath,
                HarvestDirectoryRefId = i.HarvestDirectoryRefId,
                HarvestComponentGroupId = i.HarvestComponentGroupId,
                Versioning = CloneMsiVersionOptions(i.Versioning),
                MsBuildProperties = CloneDictionary(i.MsBuildProperties),
                SignProfile = i.SignProfile,
                Sign = DotNetPublishSigningProfileResolver.CloneSignOptions(i.Sign),
                SignOverrides = DotNetPublishSigningProfileResolver.CloneSignPatch(i.SignOverrides),
                ClientLicense = CloneMsiClientLicenseOptions(i.ClientLicense)
            })
            .ToArray();
    }

    private static DotNetPublishStorePackage[] CloneStorePackages(DotNetPublishStorePackage[] storePackages)
    {
        return (storePackages ?? Array.Empty<DotNetPublishStorePackage>())
            .Where(i => i is not null)
            .Select(i => new DotNetPublishStorePackage
            {
                Id = i.Id,
                PrepareFromTarget = i.PrepareFromTarget,
                Runtimes = NormalizeStrings(i.Runtimes),
                Frameworks = NormalizeStrings(i.Frameworks),
                Styles = NormalizeStyles(i.Styles),
                PackagingProjectId = i.PackagingProjectId,
                PackagingProjectPath = i.PackagingProjectPath,
                OutputPath = i.OutputPath,
                ClearOutput = i.ClearOutput,
                BuildMode = i.BuildMode,
                Bundle = i.Bundle,
                GenerateAppInstaller = i.GenerateAppInstaller,
                MsBuildProperties = i.MsBuildProperties is null
                    ? null
                    : new Dictionary<string, string>(i.MsBuildProperties, StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();
    }

    private static DotNetPublishBundle[] CloneBundles(DotNetPublishBundle[] bundles)
    {
        return (bundles ?? Array.Empty<DotNetPublishBundle>())
            .Where(b => b is not null)
            .Select(b => new DotNetPublishBundle
            {
                Id = b.Id,
                PrepareFromTarget = b.PrepareFromTarget,
                Runtimes = NormalizeStrings(b.Runtimes),
                Frameworks = NormalizeStrings(b.Frameworks),
                Styles = NormalizeStyles(b.Styles),
                OutputPath = b.OutputPath,
                PrimarySubdirectory = b.PrimarySubdirectory,
                ClearOutput = b.ClearOutput,
                Zip = b.Zip,
                ZipPath = b.ZipPath,
                ZipNameTemplate = b.ZipNameTemplate,
                Includes = CloneBundleIncludes(b.Includes),
                CopyItems = CloneBundleCopyItems(b.CopyItems),
                ModuleIncludes = CloneBundleModuleIncludes(b.ModuleIncludes),
                GeneratedScripts = CloneBundleGeneratedScripts(b.GeneratedScripts),
                Scripts = CloneBundleScripts(b.Scripts),
                PostProcess = b.PostProcess
            })
            .ToArray();
    }

    private static DotNetPublishBundleInclude[] CloneBundleIncludes(DotNetPublishBundleInclude[] includes)
    {
        return (includes ?? Array.Empty<DotNetPublishBundleInclude>())
            .Where(i => i is not null)
            .Select(i => new DotNetPublishBundleInclude
            {
                Target = i.Target,
                Subdirectory = i.Subdirectory,
                Framework = i.Framework,
                Runtime = i.Runtime,
                Style = i.Style,
                Required = i.Required
            })
            .ToArray();
    }

    private static DotNetPublishBundleCopyItem[] CloneBundleCopyItems(DotNetPublishBundleCopyItem[] items)
    {
        return (items ?? Array.Empty<DotNetPublishBundleCopyItem>())
            .Where(i => i is not null)
            .Select(i => new DotNetPublishBundleCopyItem
            {
                SourcePath = i.SourcePath,
                DestinationPath = i.DestinationPath,
                Required = i.Required,
                ClearDestination = i.ClearDestination
            })
            .ToArray();
    }

    private static DotNetPublishBundleModuleInclude[] CloneBundleModuleIncludes(DotNetPublishBundleModuleInclude[] modules)
    {
        return (modules ?? Array.Empty<DotNetPublishBundleModuleInclude>())
            .Where(m => m is not null)
            .Select(m => new DotNetPublishBundleModuleInclude
            {
                ModuleName = m.ModuleName,
                SourcePath = m.SourcePath,
                DestinationPath = m.DestinationPath,
                Required = m.Required,
                ClearDestination = m.ClearDestination
            })
            .ToArray();
    }

    private static DotNetPublishBundleGeneratedScript[] CloneBundleGeneratedScripts(DotNetPublishBundleGeneratedScript[] scripts)
    {
        return (scripts ?? Array.Empty<DotNetPublishBundleGeneratedScript>())
            .Where(s => s is not null)
            .Select(s => new DotNetPublishBundleGeneratedScript
            {
                TemplatePath = s.TemplatePath,
                Template = s.Template,
                OutputPath = s.OutputPath,
                Tokens = CloneDictionary(s.Tokens),
                Overwrite = s.Overwrite,
                SignProfile = s.SignProfile,
                Sign = DotNetPublishSigningProfileResolver.CloneSignOptions(s.Sign),
                SignOverrides = DotNetPublishSigningProfileResolver.CloneSignPatch(s.SignOverrides)
            })
            .ToArray();
    }

    private static DotNetPublishBundleScript[] CloneBundleScripts(DotNetPublishBundleScript[] scripts)
    {
        return (scripts ?? Array.Empty<DotNetPublishBundleScript>())
            .Where(s => s is not null)
            .Select(s => new DotNetPublishBundleScript
            {
                Path = s.Path,
                Arguments = NormalizeStrings(s.Arguments),
                WorkingDirectory = s.WorkingDirectory,
                TimeoutSeconds = s.TimeoutSeconds,
                PreferPwsh = s.PreferPwsh,
                Required = s.Required
            })
            .ToArray();
    }

    private static DotNetPublishCommandHook[] CloneCommandHooks(DotNetPublishCommandHook[]? hooks)
    {
        return (hooks ?? Array.Empty<DotNetPublishCommandHook>())
            .Where(h => h is not null)
            .Select(h => new DotNetPublishCommandHook
            {
                Id = h.Id,
                Phase = h.Phase,
                Command = h.Command,
                Arguments = NormalizeArguments(h.Arguments),
                WorkingDirectory = h.WorkingDirectory,
                Environment = CloneDictionary(h.Environment),
                TimeoutSeconds = h.TimeoutSeconds,
                Required = h.Required,
                Targets = NormalizeStrings(h.Targets),
                Runtimes = NormalizeStrings(h.Runtimes),
                Frameworks = NormalizeStrings(h.Frameworks),
                Styles = NormalizeStyles(h.Styles)
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

    private static Dictionary<string, string>? CloneDictionary(Dictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
            return null;

        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
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
                    MsBuildProperties = t.Publish?.MsBuildProperties is null
                        ? null
                        : new Dictionary<string, string>(t.Publish.MsBuildProperties, StringComparer.OrdinalIgnoreCase),
                    StyleOverrides = CloneStyleOverrides(t.Publish?.StyleOverrides),
                    Sign = DotNetPublishSigningProfileResolver.CloneSignOptions(t.Publish?.Sign),
                    SignProfile = t.Publish?.SignProfile,
                    SignOverrides = DotNetPublishSigningProfileResolver.CloneSignPatch(t.Publish?.SignOverrides),
                    Service = CloneServicePackageOptions(t.Publish?.Service),
                    State = CloneStatePreservationOptions(t.Publish?.State)
                }
            })
            .ToArray();
    }

    private static Dictionary<string, DotNetPublishStyleOverride>? CloneStyleOverrides(
        Dictionary<string, DotNetPublishStyleOverride>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
            return null;

        var clone = new Dictionary<string, DotNetPublishStyleOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in overrides)
        {
            if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value is null)
                continue;

            clone[kv.Key.Trim()] = new DotNetPublishStyleOverride
            {
                MsBuildProperties = kv.Value.MsBuildProperties is null
                    ? null
                    : new Dictionary<string, string>(kv.Value.MsBuildProperties, StringComparer.OrdinalIgnoreCase)
            };
        }

        return clone.Count == 0 ? null : clone;
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

    private static string[] NormalizeArguments(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => v is not null)
            .Select(v => v ?? string.Empty)
            .ToArray();
    }

    private static DotNetPublishStyle[] NormalizeStyles(IEnumerable<DotNetPublishStyle>? values)
    {
        return (values ?? Array.Empty<DotNetPublishStyle>())
            .Distinct()
            .ToArray();
    }

    private static DotNetPublishCommandHook[] NormalizeCommandHooks(IEnumerable<DotNetPublishCommandHook>? hooks)
    {
        var result = new List<DotNetPublishCommandHook>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hook in hooks ?? Array.Empty<DotNetPublishCommandHook>())
        {
            if (hook is null) continue;
            var id = (hook.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Hooks[].Id is required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Duplicate hook ID detected: {id}");
            if (string.IsNullOrWhiteSpace(hook.Command))
                throw new ArgumentException($"Hook '{id}' requires Command.");

            result.Add(new DotNetPublishCommandHook
            {
                Id = id,
                Phase = hook.Phase,
                Command = hook.Command.Trim(),
                Arguments = NormalizeArguments(hook.Arguments),
                WorkingDirectory = string.IsNullOrWhiteSpace(hook.WorkingDirectory) ? null : hook.WorkingDirectory!.Trim(),
                Environment = CloneDictionary(hook.Environment),
                TimeoutSeconds = Math.Max(1, hook.TimeoutSeconds),
                Required = hook.Required,
                Targets = NormalizeStrings(hook.Targets),
                Runtimes = NormalizeStrings(hook.Runtimes),
                Frameworks = NormalizeStrings(hook.Frameworks),
                Styles = NormalizeStyles(hook.Styles)
            });
        }

        return result.ToArray();
    }

    private static void AddCommandHookSteps(
        List<DotNetPublishStep> steps,
        IReadOnlyList<DotNetPublishCommandHook> hooks,
        DotNetPublishCommandHookPhase phase,
        string configuration,
        string? targetName,
        string? framework,
        string? runtime,
        DotNetPublishStyle? style,
        string? bundleId)
    {
        foreach (var hook in hooks.Where(hook => hook.Phase == phase))
        {
            if (!CommandHookMatches(hook, targetName, framework, runtime, style))
                continue;

            var suffixParts = new[] { targetName, framework, runtime, style?.ToString(), bundleId }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .ToArray();
            var suffix = suffixParts.Length == 0 ? string.Empty : ":" + string.Join(":", suffixParts);

            steps.Add(new DotNetPublishStep
            {
                Key = $"hook:{phase}:{hook.Id}{suffix}",
                Kind = DotNetPublishStepKind.CommandHook,
                Title = $"Hook {phase}: {hook.Id}",
                HookId = hook.Id,
                HookPhase = phase,
                HookCommand = hook.Command,
                HookArguments = hook.Arguments,
                HookWorkingDirectory = hook.WorkingDirectory,
                HookEnvironment = hook.Environment ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                HookTimeoutSeconds = Math.Max(1, hook.TimeoutSeconds),
                HookRequired = hook.Required,
                TargetName = targetName,
                Framework = framework,
                Runtime = runtime,
                Style = style,
                BundleId = bundleId
            });
        }
    }

    private static bool CommandHookMatches(
        DotNetPublishCommandHook hook,
        string? targetName,
        string? framework,
        string? runtime,
        DotNetPublishStyle? style)
    {
        if (hook.Targets.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(targetName) || !hook.Targets.Any(pattern => WildcardMatch(targetName!, pattern)))
                return false;
        }

        if (hook.Frameworks.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(framework) || !hook.Frameworks.Any(pattern => WildcardMatch(framework!, pattern)))
                return false;
        }

        if (hook.Runtimes.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(runtime) || !hook.Runtimes.Any(pattern => WildcardMatch(runtime!, pattern)))
                return false;
        }

        if (hook.Styles.Length > 0)
        {
            if (!style.HasValue || !hook.Styles.Contains(style.Value))
                return false;
        }

        return true;
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

    private static DotNetPublishBundlePlan[] BuildBundlePlans(
        IEnumerable<DotNetPublishBundle>? bundles,
        IEnumerable<DotNetPublishTargetPlan>? targets,
        string projectRoot,
        IReadOnlyDictionary<string, DotNetPublishSignOptions>? signingProfiles)
    {
        var targetPlans = (targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
            .ToArray();
        var targetMap = targetPlans.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var plans = new List<DotNetPublishBundlePlan>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in bundles ?? Array.Empty<DotNetPublishBundle>())
        {
            if (bundle is null) continue;
            var id = (bundle.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Bundles[].Id is required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Duplicate bundle ID detected: {id}");

            var sourceTarget = (bundle.PrepareFromTarget ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceTarget))
                throw new ArgumentException($"Bundles['{id}'].PrepareFromTarget is required.");
            if (!targetMap.TryGetValue(sourceTarget, out var sourceTargetPlan))
                throw new ArgumentException($"Bundle '{id}' references unknown PrepareFromTarget '{sourceTarget}'.");

            var runtimes = NormalizeStrings(bundle.Runtimes);
            var frameworks = NormalizeStrings(bundle.Frameworks);
            var styles = NormalizeStyles(bundle.Styles);
            var matchingCombinations = (sourceTargetPlan.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                .Where(combo => InstallerMatchesCombo(runtimes, frameworks, styles, combo))
                .ToArray();
            if (matchingCombinations.Length == 0)
            {
                throw new ArgumentException(
                    $"Bundle '{id}' does not match any publish combination for target '{sourceTarget}'. " +
                    $"{BuildInstallerFilterSummary(runtimes, frameworks, styles)}");
            }

            var includePlans = new List<DotNetPublishBundleIncludePlan>();
            foreach (var include in bundle.Includes ?? Array.Empty<DotNetPublishBundleInclude>())
            {
                if (include is null) continue;
                var includeTarget = (include.Target ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(includeTarget))
                    throw new ArgumentException($"Bundle '{id}' include Target is required.");
                if (!targetMap.ContainsKey(includeTarget))
                    throw new ArgumentException($"Bundle '{id}' references unknown include target '{includeTarget}'.");

                includePlans.Add(new DotNetPublishBundleIncludePlan
                {
                    Target = includeTarget,
                    Subdirectory = string.IsNullOrWhiteSpace(include.Subdirectory) ? null : include.Subdirectory!.Trim(),
                    Framework = string.IsNullOrWhiteSpace(include.Framework) ? null : include.Framework!.Trim(),
                    Runtime = string.IsNullOrWhiteSpace(include.Runtime) ? null : include.Runtime!.Trim(),
                    Style = include.Style,
                    Required = include.Required
                });
            }

            var copyItemPlans = new List<DotNetPublishBundleCopyItemPlan>();
            foreach (var item in bundle.CopyItems ?? Array.Empty<DotNetPublishBundleCopyItem>())
            {
                if (item is null) continue;
                if (string.IsNullOrWhiteSpace(item.SourcePath))
                    throw new ArgumentException($"Bundle '{id}' CopyItems[] requires SourcePath.");
                if (string.IsNullOrWhiteSpace(item.DestinationPath))
                    throw new ArgumentException($"Bundle '{id}' CopyItems[] requires DestinationPath.");

                copyItemPlans.Add(new DotNetPublishBundleCopyItemPlan
                {
                    SourcePath = item.SourcePath.Trim(),
                    DestinationPath = item.DestinationPath.Trim(),
                    Required = item.Required,
                    ClearDestination = item.ClearDestination
                });
            }

            var moduleIncludePlans = new List<DotNetPublishBundleModuleIncludePlan>();
            foreach (var module in bundle.ModuleIncludes ?? Array.Empty<DotNetPublishBundleModuleInclude>())
            {
                if (module is null) continue;
                var moduleName = (module.ModuleName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(moduleName))
                    throw new ArgumentException($"Bundle '{id}' ModuleIncludes[] requires ModuleName.");
                if (string.IsNullOrWhiteSpace(module.SourcePath))
                    throw new ArgumentException($"Bundle '{id}' ModuleIncludes['{moduleName}'] requires SourcePath.");

                moduleIncludePlans.Add(new DotNetPublishBundleModuleIncludePlan
                {
                    ModuleName = moduleName,
                    SourcePath = module.SourcePath.Trim(),
                    DestinationPath = string.IsNullOrWhiteSpace(module.DestinationPath)
                        ? $"Modules/{{moduleName}}"
                        : module.DestinationPath!.Trim(),
                    Required = module.Required,
                    ClearDestination = module.ClearDestination
                });
            }

            var generatedScriptPlans = new List<DotNetPublishBundleGeneratedScriptPlan>();
            foreach (var generated in bundle.GeneratedScripts ?? Array.Empty<DotNetPublishBundleGeneratedScript>())
            {
                if (generated is null) continue;
                if (string.IsNullOrWhiteSpace(generated.OutputPath))
                    throw new ArgumentException($"Bundle '{id}' GeneratedScripts[] requires OutputPath.");
                if (string.IsNullOrWhiteSpace(generated.TemplatePath) && string.IsNullOrWhiteSpace(generated.Template))
                    throw new ArgumentException($"Bundle '{id}' GeneratedScripts['{generated.OutputPath}'] requires TemplatePath or Template.");

                generatedScriptPlans.Add(new DotNetPublishBundleGeneratedScriptPlan
                {
                    TemplatePath = string.IsNullOrWhiteSpace(generated.TemplatePath)
                        ? null
                        : generated.TemplatePath!.Trim(),
                    Template = generated.Template,
                    OutputPath = generated.OutputPath.Trim(),
                    Tokens = CloneDictionary(generated.Tokens) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    Overwrite = generated.Overwrite,
                    Sign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                        signingProfiles,
                        generated.SignProfile,
                        generated.Sign,
                        generated.SignOverrides,
                        $"Bundle '{id}' generated script '{generated.OutputPath}'")
                });
            }

            var scriptPlans = new List<DotNetPublishBundleScriptPlan>();
            foreach (var script in bundle.Scripts ?? Array.Empty<DotNetPublishBundleScript>())
            {
                if (script is null) continue;
                var scriptPath = ResolvePath(projectRoot, script.Path ?? string.Empty);
                if (string.IsNullOrWhiteSpace(script.Path))
                    throw new ArgumentException($"Bundle '{id}' script Path is required.");
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException($"Bundle script path not found for '{id}': {scriptPath}", scriptPath);

                scriptPlans.Add(new DotNetPublishBundleScriptPlan
                {
                    Path = scriptPath,
                    Arguments = NormalizeStrings(script.Arguments),
                    WorkingDirectory = string.IsNullOrWhiteSpace(script.WorkingDirectory)
                        ? null
                        : script.WorkingDirectory!.Trim(),
                    TimeoutSeconds = Math.Max(1, script.TimeoutSeconds),
                    PreferPwsh = script.PreferPwsh,
                    Required = script.Required
                });
            }

            plans.Add(new DotNetPublishBundlePlan
            {
                Id = id,
                PrepareFromTarget = sourceTarget,
                Runtimes = runtimes,
                Frameworks = frameworks,
                Styles = styles,
                OutputPath = bundle.OutputPath,
                PrimarySubdirectory = string.IsNullOrWhiteSpace(bundle.PrimarySubdirectory)
                    ? null
                    : bundle.PrimarySubdirectory!.Trim(),
                ClearOutput = bundle.ClearOutput,
                Zip = bundle.Zip,
                ZipPath = bundle.ZipPath,
                ZipNameTemplate = bundle.ZipNameTemplate,
                Includes = includePlans.ToArray(),
                CopyItems = copyItemPlans.ToArray(),
                ModuleIncludes = moduleIncludePlans.ToArray(),
                GeneratedScripts = generatedScriptPlans.ToArray(),
                Scripts = scriptPlans.ToArray(),
                PostProcess = NormalizeBundlePostProcess(id, bundle.PostProcess)
            });
        }

        return plans.ToArray();
    }

    private static DotNetPublishInstallerPlan[] BuildInstallerPlans(
        IEnumerable<DotNetPublishInstaller>? installers,
        IEnumerable<DotNetPublishBundlePlan>? bundles,
        IEnumerable<DotNetPublishTargetPlan>? targets,
        IReadOnlyDictionary<string, string> projectCatalog,
        string projectRoot,
        IReadOnlyDictionary<string, DotNetPublishSignOptions>? signingProfiles)
    {
        var targetPlans = (targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
            .ToArray();
        var bundlePlans = (bundles ?? Array.Empty<DotNetPublishBundlePlan>())
            .Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Id))
            .ToArray();
        var targetNames = new HashSet<string>(
            targetPlans.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);
        var targetMap = targetPlans.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var bundleMap = bundlePlans.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);

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

            var prepareFromBundleId = string.IsNullOrWhiteSpace(installer.PrepareFromBundleId)
                ? null
                : installer.PrepareFromBundleId!.Trim();
            if (!string.IsNullOrWhiteSpace(prepareFromBundleId))
            {
                if (!bundleMap.TryGetValue(prepareFromBundleId!, out var bundlePlan))
                    throw new ArgumentException($"Installer '{id}' references unknown PrepareFromBundleId '{prepareFromBundleId}'.");
                if (!string.Equals(bundlePlan.PrepareFromTarget, sourceTarget, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Installer '{id}' PrepareFromBundleId '{prepareFromBundleId}' is bound to target '{bundlePlan.PrepareFromTarget}', not '{sourceTarget}'.");
                }
            }

            var runtimes = NormalizeStrings(installer.Runtimes);
            var frameworks = NormalizeStrings(installer.Frameworks);
            var styles = NormalizeStyles(installer.Styles);
            var sourceTargetPlan = targetMap[sourceTarget];
            var matchingCombinations = (sourceTargetPlan.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                .Where(combo => InstallerMatchesCombo(runtimes, frameworks, styles, combo))
                .ToArray();

            if (matchingCombinations.Length == 0)
            {
                throw new ArgumentException(
                    $"Installer '{id}' does not match any publish combination for target '{sourceTarget}'. " +
                    $"{BuildInstallerFilterSummary(runtimes, frameworks, styles)}");
            }

            plans.Add(new DotNetPublishInstallerPlan
            {
                Id = id,
                PrepareFromTarget = sourceTarget,
                PrepareFromBundleId = prepareFromBundleId,
                Runtimes = runtimes,
                Frameworks = frameworks,
                Styles = styles,
                StagingPath = installer.StagingPath,
                ManifestPath = installer.ManifestPath,
                InstallerProjectId = installer.InstallerProjectId,
                InstallerProjectPath = ResolveInstallerProjectPath(id, installer, projectCatalog, projectRoot),
                Harvest = installer.Harvest,
                HarvestPath = installer.HarvestPath,
                HarvestDirectoryRefId = installer.HarvestDirectoryRefId,
                HarvestComponentGroupId = installer.HarvestComponentGroupId,
                Versioning = NormalizeInstallerVersioning(id, installer.Versioning),
                MsBuildProperties = CloneDictionary(installer.MsBuildProperties),
                Sign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
                    signingProfiles,
                    installer.SignProfile,
                    installer.Sign,
                    installer.SignOverrides,
                    $"Installer '{id}'"),
                ClientLicense = NormalizeInstallerClientLicense(id, installer.ClientLicense)
            });
        }

        return plans.ToArray();
    }

    private static List<DotNetPublishTargetPlan> OrderTargetsForBundleIncludes(
        List<DotNetPublishTargetPlan> targets,
        IReadOnlyList<DotNetPublishBundlePlan> bundles)
    {
        if (targets is null || targets.Count < 2 || bundles is null || bundles.Count == 0)
            return targets ?? new List<DotNetPublishTargetPlan>();

        var targetMap = targets
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
            .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var dependencies = targetMap.Keys.ToDictionary(
            name => name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var bundle in bundles)
        {
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.PrepareFromTarget))
                continue;
            if (!dependencies.TryGetValue(bundle.PrepareFromTarget, out var sourceDependencies))
                continue;

            foreach (var include in bundle.Includes ?? Array.Empty<DotNetPublishBundleIncludePlan>())
            {
                if (include is null || string.IsNullOrWhiteSpace(include.Target))
                    continue;
                if (targetMap.ContainsKey(include.Target))
                    sourceDependencies.Add(include.Target);
            }
        }

        if (dependencies.Values.All(set => set.Count == 0))
            return targets;

        var ordered = new List<DotNetPublishTargetPlan>(targets.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string targetName)
        {
            if (visited.Contains(targetName))
                return;
            if (!visiting.Add(targetName))
                throw new ArgumentException($"Bundle include target dependency cycle detected at '{targetName}'.");

            if (dependencies.TryGetValue(targetName, out var deps))
            {
                foreach (var dependency in deps)
                {
                    if (targetMap.ContainsKey(dependency))
                        Visit(dependency);
                }
            }

            visiting.Remove(targetName);
            visited.Add(targetName);
            ordered.Add(targetMap[targetName]);
        }

        foreach (var target in targets)
            Visit(target.Name);

        return ordered;
    }

    private static DotNetPublishStorePackagePlan[] BuildStorePackagePlans(
        IEnumerable<DotNetPublishStorePackage>? storePackages,
        IEnumerable<DotNetPublishTargetPlan>? targets,
        IReadOnlyDictionary<string, string> projectCatalog,
        string projectRoot)
    {
        var targetPlans = (targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name))
            .ToArray();
        var targetNames = new HashSet<string>(
            targetPlans.Select(t => t.Name),
            StringComparer.OrdinalIgnoreCase);
        var targetMap = targetPlans.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var plans = new List<DotNetPublishStorePackagePlan>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var storePackage in storePackages ?? Array.Empty<DotNetPublishStorePackage>())
        {
            if (storePackage is null) continue;
            var id = (storePackage.Id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("StorePackages[].Id is required.");
            if (!ids.Add(id))
                throw new ArgumentException($"Duplicate store package ID detected: {id}");

            var sourceTarget = (storePackage.PrepareFromTarget ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sourceTarget))
                throw new ArgumentException($"StorePackages['{id}'].PrepareFromTarget is required.");
            if (!targetNames.Contains(sourceTarget))
                throw new ArgumentException($"Store package '{id}' references unknown PrepareFromTarget '{sourceTarget}'.");

            var runtimes = NormalizeStrings(storePackage.Runtimes);
            var frameworks = NormalizeStrings(storePackage.Frameworks);
            var styles = NormalizeStyles(storePackage.Styles);
            var sourceTargetPlan = targetMap[sourceTarget];
            var matchingCombinations = (sourceTargetPlan.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                .Where(combo => StorePackageMatchesCombo(runtimes, frameworks, styles, combo))
                .ToArray();

            if (matchingCombinations.Length == 0)
            {
                throw new ArgumentException(
                    $"Store package '{id}' does not match any publish combination for target '{sourceTarget}'. " +
                    $"{BuildInstallerFilterSummary(runtimes, frameworks, styles)}");
            }

            plans.Add(new DotNetPublishStorePackagePlan
            {
                Id = id,
                PrepareFromTarget = sourceTarget,
                Runtimes = runtimes,
                Frameworks = frameworks,
                Styles = styles,
                PackagingProjectId = storePackage.PackagingProjectId,
                PackagingProjectPath = ResolveStorePackagingProjectPath(id, storePackage, projectCatalog, projectRoot),
                OutputPath = storePackage.OutputPath,
                ClearOutput = storePackage.ClearOutput,
                BuildMode = storePackage.BuildMode,
                Bundle = storePackage.Bundle,
                GenerateAppInstaller = storePackage.GenerateAppInstaller,
                MsBuildProperties = storePackage.MsBuildProperties is null
                    ? null
                    : new Dictionary<string, string>(storePackage.MsBuildProperties, StringComparer.OrdinalIgnoreCase)
            });
        }

        return plans.ToArray();
    }

    private static string ResolveStorePackagingProjectPath(
        string storePackageId,
        DotNetPublishStorePackage storePackage,
        IReadOnlyDictionary<string, string> projectCatalog,
        string projectRoot)
    {
        var hasProjectPath = !string.IsNullOrWhiteSpace(storePackage.PackagingProjectPath);
        var hasProjectId = !string.IsNullOrWhiteSpace(storePackage.PackagingProjectId);

        if (!hasProjectPath && !hasProjectId)
            throw new ArgumentException($"Store package '{storePackageId}' requires PackagingProjectPath or PackagingProjectId.");

        if (hasProjectPath)
        {
            var resolvedPath = ResolvePath(projectRoot, storePackage.PackagingProjectPath!);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    $"Store packaging project path not found for store package '{storePackageId}': {resolvedPath}",
                    resolvedPath);
            }

            return resolvedPath;
        }

        var id = storePackage.PackagingProjectId!.Trim();
        if (!projectCatalog.TryGetValue(id, out var path) || string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Store package '{storePackageId}' references unknown PackagingProjectId '{id}'.");

        var catalogResolvedPath = path;
        if (!File.Exists(catalogResolvedPath))
        {
            throw new FileNotFoundException(
                $"Store packaging project path not found for store package '{storePackageId}': {catalogResolvedPath}",
                catalogResolvedPath);
        }

        return catalogResolvedPath;
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
            BundleId = installer.PrepareFromBundleId,
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

    private static DotNetPublishStep CreateBundleStep(
        string projectRoot,
        string configuration,
        DotNetPublishBundlePlan bundle,
        string targetName,
        DotNetPublishTargetCombination combo)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundle"] = bundle.Id,
            ["target"] = targetName,
            ["rid"] = combo.Runtime,
            ["framework"] = combo.Framework,
            ["style"] = combo.Style.ToString(),
            ["configuration"] = configuration
        };

        var outputTemplate = string.IsNullOrWhiteSpace(bundle.OutputPath)
            ? Path.Combine("Artifacts", "DotNetPublish", "Bundles", "{bundle}", "{rid}", "{framework}", "{style}")
            : bundle.OutputPath!;
        var outputPath = ResolvePath(projectRoot, ApplyTemplate(outputTemplate, tokens));

        string? zipPath = null;
        if (bundle.Zip)
        {
            var zipNameTemplate = string.IsNullOrWhiteSpace(bundle.ZipNameTemplate)
                ? "{bundle}-{framework}-{rid}-{style}.zip"
                : bundle.ZipNameTemplate!;
            var zipName = ApplyTemplate(zipNameTemplate, tokens);
            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipName += ".zip";

            zipPath = string.IsNullOrWhiteSpace(bundle.ZipPath)
                ? Path.Combine(Path.GetDirectoryName(outputPath)!, zipName)
                : ResolvePath(projectRoot, ApplyTemplate(bundle.ZipPath!, tokens));
        }

        return new DotNetPublishStep
        {
            Key = $"bundle:{bundle.Id}:{targetName}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
            Kind = DotNetPublishStepKind.Bundle,
            Title = "Bundle",
            BundleId = bundle.Id,
            TargetName = targetName,
            Framework = combo.Framework,
            Runtime = combo.Runtime,
            Style = combo.Style,
            BundleOutputPath = outputPath,
            BundleZipPath = zipPath
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

    private static DotNetPublishStep CreateStorePackageStep(
        string projectRoot,
        string configuration,
        DotNetPublishStorePackagePlan storePackage,
        string targetName,
        DotNetPublishTargetCombination combo)
    {
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["storePackage"] = storePackage.Id,
            ["target"] = targetName,
            ["rid"] = combo.Runtime,
            ["framework"] = combo.Framework,
            ["style"] = combo.Style.ToString(),
            ["configuration"] = configuration
        };

        var outputTemplate = string.IsNullOrWhiteSpace(storePackage.OutputPath)
            ? DefaultStorePackageOutputPathTemplate
            : storePackage.OutputPath!;
        var outputPath = ResolvePath(projectRoot, ApplyTemplate(outputTemplate, tokens));

        return new DotNetPublishStep
        {
            Key = $"store.package:{storePackage.Id}:{targetName}:{combo.Framework}:{combo.Runtime}:{combo.Style}",
            Kind = DotNetPublishStepKind.StorePackage,
            Title = "Store package",
            StorePackageId = storePackage.Id,
            TargetName = targetName,
            Framework = combo.Framework,
            Runtime = combo.Runtime,
            Style = combo.Style,
            StorePackageProjectPath = storePackage.PackagingProjectPath,
            StorePackageOutputPath = outputPath
        };
    }

    private static bool InstallerMatchesCombo(
        DotNetPublishInstallerPlan installer,
        DotNetPublishTargetCombination combo)
    {
        return InstallerMatchesCombo(
            installer.Runtimes ?? Array.Empty<string>(),
            installer.Frameworks ?? Array.Empty<string>(),
            installer.Styles ?? Array.Empty<DotNetPublishStyle>(),
            combo);
    }

    private static bool InstallerMatchesCombo(
        string[] runtimes,
        string[] frameworks,
        DotNetPublishStyle[] styles,
        DotNetPublishTargetCombination combo)
    {
        if (combo is null) return false;

        if ((runtimes?.Length ?? 0) > 0
            && !runtimes!.Any(runtime => string.Equals(runtime, combo.Runtime, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if ((frameworks?.Length ?? 0) > 0
            && !frameworks!.Any(framework => string.Equals(framework, combo.Framework, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if ((styles?.Length ?? 0) > 0 && !styles!.Contains(combo.Style))
            return false;

        return true;
    }

    private static bool StorePackageMatchesCombo(
        DotNetPublishStorePackagePlan storePackage,
        DotNetPublishTargetCombination combo)
    {
        return StorePackageMatchesCombo(
            storePackage.Runtimes ?? Array.Empty<string>(),
            storePackage.Frameworks ?? Array.Empty<string>(),
            storePackage.Styles ?? Array.Empty<DotNetPublishStyle>(),
            combo);
    }

    private static bool StorePackageMatchesCombo(
        string[] runtimes,
        string[] frameworks,
        DotNetPublishStyle[] styles,
        DotNetPublishTargetCombination combo)
    {
        return InstallerMatchesCombo(runtimes, frameworks, styles, combo);
    }

    private static bool BundleMatchesCombo(
        DotNetPublishBundlePlan bundle,
        DotNetPublishTargetCombination combo)
    {
        return InstallerMatchesCombo(
            bundle.Runtimes ?? Array.Empty<string>(),
            bundle.Frameworks ?? Array.Empty<string>(),
            bundle.Styles ?? Array.Empty<DotNetPublishStyle>(),
            combo);
    }

    private static string BuildInstallerFilterSummary(
        string[] runtimes,
        string[] frameworks,
        DotNetPublishStyle[] styles)
    {
        var parts = new List<string>();

        if (runtimes.Length > 0)
            parts.Add($"Runtimes=[{string.Join(", ", runtimes)}]");
        if (frameworks.Length > 0)
            parts.Add($"Frameworks=[{string.Join(", ", frameworks)}]");
        if (styles.Length > 0)
            parts.Add($"Styles=[{string.Join(", ", styles)}]");

        return parts.Count == 0
            ? "The source target may not produce any combinations."
            : $"Resolved filters: {string.Join("; ", parts)}";
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

    private static DotNetPublishBundlePostProcessOptions? NormalizeBundlePostProcess(
        string bundleId,
        DotNetPublishBundlePostProcessOptions? options)
    {
        var clone = CloneBundlePostProcessOptions(options);
        if (clone is null)
            return null;

        clone.ArchiveDirectories = (clone.ArchiveDirectories ?? Array.Empty<DotNetPublishBundleArchiveRule>())
            .Where(rule => rule is not null)
            .Select(rule => new DotNetPublishBundleArchiveRule
            {
                Path = string.IsNullOrWhiteSpace(rule.Path)
                    ? throw new ArgumentException($"Bundle '{bundleId}' ArchiveDirectories[] requires Path.")
                    : rule.Path.Trim(),
                Mode = rule.Mode,
                ArchiveNameTemplate = string.IsNullOrWhiteSpace(rule.ArchiveNameTemplate)
                    ? null
                    : rule.ArchiveNameTemplate!.Trim(),
                DeleteSource = rule.DeleteSource
            })
            .ToArray();

        clone.DeletePatterns = NormalizeStrings(clone.DeletePatterns);

        if (clone.Metadata is not null)
        {
            clone.Metadata = new DotNetPublishBundleMetadataOptions
            {
                Path = string.IsNullOrWhiteSpace(clone.Metadata.Path)
                    ? throw new ArgumentException($"Bundle '{bundleId}' PostProcess.Metadata.Path is required.")
                    : clone.Metadata.Path.Trim(),
                IncludeStandardProperties = clone.Metadata.IncludeStandardProperties,
                Properties = clone.Metadata.Properties is null
                    ? null
                    : clone.Metadata.Properties
                        .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                        .ToDictionary(
                            kv => kv.Key.Trim(),
                            kv => kv.Value ?? string.Empty,
                            StringComparer.OrdinalIgnoreCase)
            };
        }

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

    private static DotNetPublishBundlePostProcessOptions? CloneBundlePostProcessOptions(
        DotNetPublishBundlePostProcessOptions? options)
    {
        if (options is null)
            return null;

        return new DotNetPublishBundlePostProcessOptions
        {
            ArchiveDirectories = (options.ArchiveDirectories ?? Array.Empty<DotNetPublishBundleArchiveRule>())
                .Where(rule => rule is not null)
                .Select(rule => new DotNetPublishBundleArchiveRule
                {
                    Path = rule.Path,
                    Mode = rule.Mode,
                    ArchiveNameTemplate = rule.ArchiveNameTemplate,
                    DeleteSource = rule.DeleteSource
                })
                .ToArray(),
            DeletePatterns = NormalizeStrings(options.DeletePatterns),
            Metadata = options.Metadata is null
                ? null
                : new DotNetPublishBundleMetadataOptions
                {
                    Path = options.Metadata.Path,
                    IncludeStandardProperties = options.Metadata.IncludeStandardProperties,
                    Properties = options.Metadata.Properties is null
                        ? null
                        : new Dictionary<string, string>(options.Metadata.Properties, StringComparer.OrdinalIgnoreCase)
                }
        };
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

    internal static bool WildcardMatch(string value, string pattern)
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

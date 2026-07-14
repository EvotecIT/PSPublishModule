using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private void Restore(DotNetPublishPlan plan, string? runtime)
    {
        var workDir = plan.ProjectRoot;

        if (!string.IsNullOrWhiteSpace(runtime))
        {
            var runtimeValue = runtime!;
            var restoreRequests = new HashSet<(string ProjectPath, string Framework)>();
            foreach (var target in plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            {
                var combinations = (target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                    .Where(combination => string.Equals(combination.Runtime, runtimeValue, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (combinations.Length == 0)
                {
                    restoreRequests.Add((target.ProjectPath, string.Empty));
                    continue;
                }

                foreach (var framework in combinations.Select(combination => combination.Framework).Distinct(StringComparer.OrdinalIgnoreCase))
                    restoreRequests.Add((target.ProjectPath, framework ?? string.Empty));
            }

            foreach (var request in restoreRequests)
            {
                var framework = request.Framework;
                var label = string.IsNullOrWhiteSpace(framework) ? runtimeValue : $"{runtimeValue}, {framework}";
                _logger.Info($"Restore ({label}) -> {request.ProjectPath}");

                RunDotnet(workDir, BuildRestoreArguments(plan, request.ProjectPath, runtimeValue, framework), plan.EnvironmentVariables);
            }

            return;
        }

        var props = BuildMsBuildPropertyArgs(plan.MsBuildProperties);
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
        {
            _logger.Info($"Restore -> {plan.SolutionPath}");
            RunDotnet(workDir, new[] { "restore", plan.SolutionPath!, "--nologo" }.Concat(props).ToArray(), plan.EnvironmentVariables);
            return;
        }

        foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"Restore -> {p}");
            RunDotnet(workDir, new[] { "restore", p, "--nologo" }.Concat(props).ToArray(), plan.EnvironmentVariables);
        }
    }

    internal static Dictionary<string, string> BuildRestoreMsBuildProperties(
        DotNetPublishPlan plan,
        string projectPath,
        string runtime,
        string? framework = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var merged = new Dictionary<string, string>(plan.MsBuildProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var target in plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
        {
            if (!string.Equals(target.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var styles = (target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
                .Where(combination => string.Equals(combination.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
                .Where(combination => string.IsNullOrWhiteSpace(framework)
                    || string.Equals(combination.Framework, framework, StringComparison.OrdinalIgnoreCase))
                .Select(combination => combination.Style)
                .Distinct()
                .ToArray();

            foreach (var style in styles)
            {
                foreach (var property in BuildPublishMsBuildProperties(plan, target, style))
                    merged[property.Key] = property.Value;

                if (IsPortableStyle(style))
                {
                    if (!merged.ContainsKey("SelfContained"))
                        merged["SelfContained"] = "true";
                    if (!merged.ContainsKey("PublishSingleFile"))
                        merged["PublishSingleFile"] = "true";
                    if (!merged.ContainsKey("IncludeNativeLibrariesForSelfExtract"))
                        merged["IncludeNativeLibrariesForSelfExtract"] = "true";
                    if (!merged.ContainsKey("PortableTrim"))
                        merged["PortableTrim"] = (style == DotNetPublishStyle.PortableSize).ToString().ToLowerInvariant();
                    if (!merged.ContainsKey("PortableTrimMode"))
                        merged["PortableTrimMode"] = style == DotNetPublishStyle.PortableSize ? "full" : "partial";
                    if (target.Publish.ReadyToRun.HasValue && !merged.ContainsKey("PublishReadyToRun"))
                        merged["PublishReadyToRun"] = target.Publish.ReadyToRun.Value.ToString().ToLowerInvariant();
                }

                if (style == DotNetPublishStyle.AotSpeed || style == DotNetPublishStyle.AotSize)
                {
                    if (!merged.ContainsKey("SelfContained"))
                        merged["SelfContained"] = "true";
                    if (!merged.ContainsKey("PublishAot"))
                        merged["PublishAot"] = "true";
                }
            }
        }

        return merged;
    }

    internal static List<string> BuildRestoreArguments(
        DotNetPublishPlan plan,
        string projectPath,
        string runtime,
        string? framework = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var args = new List<string> { "restore", projectPath, "--nologo" };
        var runtimeIdentifiers = BuildRestoreRuntimeIdentifiers(plan, projectPath, runtime, framework);
        if (runtimeIdentifiers.Length <= 1)
            args.AddRange(new[] { "-r", runtime });
        else
            args.Add($"/p:RuntimeIdentifiers={BuildMsBuildListPropertyValue(runtimeIdentifiers)}");
        args.AddRange(BuildMsBuildPropertyArgs(BuildRestoreMsBuildProperties(plan, projectPath, runtime, framework)));
        return args;
    }

    internal static string[] BuildRestoreRuntimeIdentifiers(
        DotNetPublishPlan plan,
        string projectPath,
        string runtime,
        string? framework = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var baselineProperties = BuildRestoreMsBuildProperties(plan, projectPath, runtime, framework);
        var runtimes = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(target => string.Equals(target.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(target => target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>())
            .Where(combination => string.IsNullOrWhiteSpace(framework)
                || string.Equals(combination.Framework, framework, StringComparison.OrdinalIgnoreCase))
            .Select(combination => combination.Runtime)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(candidate => RestorePropertiesEquivalent(
                baselineProperties,
                BuildRestoreMsBuildProperties(plan, projectPath, candidate!, framework)))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (runtimes.Length == 0 && !string.IsNullOrWhiteSpace(runtime))
            return new[] { runtime };

        return runtimes;
    }

    private static bool RestorePropertiesEquivalent(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var property in left)
        {
            if (!right.TryGetValue(property.Key, out var value))
                return false;
            if (!string.Equals(property.Value, value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    internal static string BuildMsBuildListPropertyValue(IEnumerable<string> values)
        => string.Join(";", (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));

    private static bool IsPortableStyle(DotNetPublishStyle style)
    {
        return style == DotNetPublishStyle.Portable
            || style == DotNetPublishStyle.PortableCompat
            || style == DotNetPublishStyle.PortableSize;
    }

    private void Clean(DotNetPublishPlan plan)
    {
        var workDir = plan.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
        {
            _logger.Info($"Clean -> {plan.SolutionPath}");
            RunDotnet(workDir, new[] { "clean", plan.SolutionPath!, "-c", plan.Configuration, "--nologo" }, plan.EnvironmentVariables);
            return;
        }

        foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"Clean -> {p}");
            RunDotnet(workDir, new[] { "clean", p, "-c", plan.Configuration, "--nologo" }, plan.EnvironmentVariables);
        }
    }

    private void Build(DotNetPublishPlan plan, string? runtime)
    {
        var workDir = plan.ProjectRoot;
        var built = false;
        foreach (var target in plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
        {
            var allCombinations = target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>();
            var combinations = allCombinations
                .Where(combination => string.IsNullOrWhiteSpace(runtime)
                    || string.Equals(combination.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (combinations.Length == 0)
            {
                if (allCombinations.Length > 0)
                    continue;

                if (!TargetRequiresPreBuild(plan, target, runtime))
                    continue;

                _logger.Info($"Build -> {target.ProjectPath}");
                var fallbackArgs = new List<string> { "build", target.ProjectPath, "-c", plan.Configuration, "--nologo" };
                if (!string.IsNullOrWhiteSpace(runtime)) fallbackArgs.AddRange(new[] { "-r", runtime! });
                if (plan.Restore) fallbackArgs.Add("--no-restore");
                fallbackArgs.AddRange(BuildMsBuildPropertyArgs(plan.MsBuildProperties));
                RunDotnet(workDir, fallbackArgs, plan.EnvironmentVariables);
                built = true;
                continue;
            }

            foreach (var combination in combinations)
            {
                var framework = combination.Framework ?? string.Empty;
                var targetRuntime = combination.Runtime ?? string.Empty;
                if (TargetUsesPublishMsiVersionProperties(plan, target.Name, framework, targetRuntime, combination.Style))
                    continue;

                _logger.Info($"Build {target.Name} ({framework}, {targetRuntime}, {combination.Style}) -> {target.ProjectPath}");
                RunDotnet(
                    workDir,
                    BuildPreBuildArguments(plan, target, framework, targetRuntime, combination.Style),
                    plan.EnvironmentVariables);
                built = true;
            }
        }

        if (!built)
            _logger.Info("Build skipped because every target builds an isolated versioned payload during publish.");
    }

    internal static List<string> BuildPreBuildArguments(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (target is null) throw new ArgumentNullException(nameof(target));

        var args = new List<string> { "build", target.ProjectPath, "-c", plan.Configuration, "--nologo" };
        if (!string.IsNullOrWhiteSpace(framework)) args.AddRange(new[] { "-f", framework });
        if (!string.IsNullOrWhiteSpace(runtime)) args.AddRange(new[] { "-r", runtime });
        if (plan.Restore) args.Add("--no-restore");
        AppendPublishStyleArgs(args, target.Publish, style);
        args.AddRange(BuildMsBuildPropertyArgs(BuildPublishMsBuildProperties(plan, target, framework, runtime, style)));
        return args;
    }

    internal static string[] GetPreBuildProjectPaths(DotNetPublishPlan plan, string? runtime)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        return (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .Where(target => TargetRequiresPreBuild(plan, target, runtime))
            .Select(target => target.ProjectPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TargetRequiresPreBuild(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string? runtime)
    {
        var allCombinations = target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>();
        if (allCombinations.Length == 0)
            return true;

        var combinations = allCombinations
            .Where(combination => string.IsNullOrWhiteSpace(runtime)
                || string.Equals(combination.Runtime, runtime, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (combinations.Length == 0)
            return false;

        return combinations.Any(combination => !TargetUsesPublishMsiVersionProperties(
            plan,
            target.Name,
            combination.Framework ?? string.Empty,
            combination.Runtime ?? string.Empty,
            combination.Style));
    }

    private DotNetPublishArtefactResult Publish(DotNetPublishPlan plan, string targetName, string framework, string rid, DotNetPublishStyle? styleOverride)
    {
        var target = plan.Targets.FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Target not found: {targetName}");

        var cfg = plan.Configuration;
        var tfm = string.IsNullOrWhiteSpace(framework) ? target.Publish.Framework : framework.Trim();
        var style = styleOverride ?? target.Publish.Style;

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target.Name,
            ["rid"] = rid,
            ["framework"] = tfm,
            ["style"] = style.ToString(),
            ["configuration"] = cfg
        };

        var outputDirTemplate = string.IsNullOrWhiteSpace(target.Publish.OutputPath)
            ? Path.Combine("Artifacts", "DotNetPublish", "{target}", "{rid}", "{framework}", "{style}")
            : target.Publish.OutputPath!;

        var outputDir = ResolvePath(plan.ProjectRoot, ApplyTemplate(outputDirTemplate, tokens));
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, outputDir, $"Target '{target.Name}' output path");

        EnsureOutputDirectoryUnlocked(
            plan,
            outputDir,
            contextLabel: $"{target.Name} ({tfm}, {rid}, {style})",
            serviceName: target.Publish.Service?.ServiceName);
        Directory.CreateDirectory(outputDir);

        var lifecycle = target.Publish.Service?.Lifecycle;
        if (target.Publish.Service is not null
            && lifecycle is not null
            && lifecycle.Enabled
            && lifecycle.Mode == DotNetPublishServiceLifecycleMode.InlineRebuild)
        {
            ExecuteServiceLifecycleInlineBeforePublish(outputDir, target.Name, target.Publish.Service, lifecycle);
        }

        var stateTransfer = PreserveStateBeforePublish(
            plan,
            outputDir,
            target.Publish.State,
            tokens,
            $"{target.Name} ({tfm}, {rid}, {style})");

        var publishDir = target.Publish.UseStaging
            ? Path.Combine(Path.GetTempPath(), "PowerForge.DotNetPublish", Guid.NewGuid().ToString("N"))
            : outputDir;

        if (target.Publish.UseStaging)
        {
            if (Directory.Exists(publishDir))
            {
                try { Directory.Delete(publishDir, recursive: true); }
                catch { /* best effort */ }
            }
            Directory.CreateDirectory(publishDir);
            _logger.Info($"Using staging publish dir -> {publishDir}");
        }

        var publishArgs = BuildPublishArguments(plan, target, tfm, rid, style, publishDir);

        _logger.Info($"Publishing {target.Name} ({rid}) -> {publishDir}");
        RunDotnet(plan.ProjectRoot, publishArgs, plan.EnvironmentVariables);

        var cleanup = ApplyCleanup(publishDir, target.Publish);

        if (!string.IsNullOrWhiteSpace(target.Publish.RenameTo))
            TryRenameMainExecutable(publishDir, rid, target.Publish.RenameTo!.Trim());

        if (target.Publish.UseStaging)
        {
            if (target.Publish.ClearOutput && Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); }
                catch { /* best effort */ }
                Directory.CreateDirectory(outputDir);
            }

            DirectoryCopy(publishDir, outputDir);
        }

        if (stateTransfer is not null)
            RestorePreservedState(outputDir, stateTransfer);

        DotNetPublishServicePackageResult? servicePackage = null;
        if (target.Publish.Service is not null)
            servicePackage = TryCreateServicePackage(outputDir, target.Name, rid, target.Publish.Service);

        var signedFiles = 0;
        if (target.Publish.Sign?.Enabled == true)
            signedFiles = TrySignOutput(outputDir, target.Publish.Sign);

        string? zipPath = null;
        if (target.Publish.Zip)
            zipPath = CreateZip(outputDir, plan, target, rid, tokens);

        if (servicePackage is not null
            && lifecycle is not null
            && lifecycle.Enabled
            && lifecycle.Mode == DotNetPublishServiceLifecycleMode.InlineRebuild)
        {
            ExecuteServiceLifecycleInlineAfterPublish(outputDir, servicePackage, lifecycle);
        }

        var summary = SummarizeDirectory(outputDir, rid);
        return new DotNetPublishArtefactResult
        {
            Target = target.Name,
            Kind = target.Kind,
            Runtime = rid,
            Framework = tfm,
            Style = style,
            PublishDir = publishDir,
            OutputDir = outputDir,
            ZipPath = zipPath,
            Files = summary.Files,
            TotalBytes = summary.TotalBytes,
            ExePath = summary.ExePath,
            ExeBytes = summary.ExeBytes,
            Cleanup = cleanup,
            ServicePackage = servicePackage,
            StateTransfer = stateTransfer,
            SignedFiles = signedFiles
        };
    }

    internal static List<string> BuildPublishArguments(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style,
        string outputDir)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (target is null) throw new ArgumentNullException(nameof(target));

        var publishArgs = new List<string>
        {
            "publish",
            target.ProjectPath,
            "-c", plan.Configuration,
            "--nologo",
            "-f", framework,
            "--runtime", runtime,
            "--output", outputDir
        };

        if (plan.NoRestoreInPublish) publishArgs.Add("--no-restore");
        if (plan.NoBuildInPublish && !TargetUsesPublishMsiVersionProperties(plan, target.Name, framework, runtime, style))
            publishArgs.Add("--no-build");

        AppendPublishStyleArgs(publishArgs, target.Publish, style);
        publishArgs.AddRange(BuildMsBuildPropertyArgs(BuildPublishMsBuildPropertiesForRun(plan, target, framework, runtime, style)));
        return publishArgs;
    }

    internal static Dictionary<string, string> BuildPublishMsBuildProperties(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        DotNetPublishStyle style)
        => BuildPublishMsBuildProperties(plan, target, target.Publish.Framework, string.Empty, style);

    internal static Dictionary<string, string> BuildPublishMsBuildProperties(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (target is null) throw new ArgumentNullException(nameof(target));

        var merged = new Dictionary<string, string>(plan.MsBuildProperties, StringComparer.OrdinalIgnoreCase);

        if (target.Publish.MsBuildProperties is not null)
        {
            foreach (var kv in target.Publish.MsBuildProperties)
                merged[kv.Key] = kv.Value;
        }

        if (target.Publish.StyleOverrides is not null
            && target.Publish.StyleOverrides.TryGetValue(style.ToString(), out var styleOverride)
            && styleOverride?.MsBuildProperties is not null)
        {
            foreach (var kv in styleOverride.MsBuildProperties)
                merged[kv.Key] = kv.Value;
        }

        ApplyPublishMsiVersionProperties(merged, plan, target.Name, framework, runtime, style);
        return merged;
    }

    private static Dictionary<string, string> BuildPublishMsBuildPropertiesForRun(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (target is null) throw new ArgumentNullException(nameof(target));

        var merged = BuildPublishMsBuildProperties(plan, target, framework, runtime, style);
        ApplyPublishMsiVersionProperties(merged, plan, target.Name, framework, runtime, style, reserveMonotonicVersions: true);
        return merged;
    }

    private static void ApplyPublishMsiVersionProperties(
        Dictionary<string, string> properties,
        DotNetPublishPlan plan,
        string targetName,
        string framework,
        string runtime,
        DotNetPublishStyle style,
        bool reserveMonotonicVersions = false)
    {
        foreach (var installer in plan.Installers.Where(i =>
                     string.Equals(i.PrepareFromTarget, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            var versioning = installer.Versioning;
            if (versioning is null || !versioning.Enabled || !versioning.ApplyToPublish)
                continue;

            var resolved = FindResolvedMsiVersion(plan, installer.Id, targetName, framework, runtime, style);
            if (resolved is null)
                continue;

            if (reserveMonotonicVersions)
                ReserveMsiVersionState(resolved, $"publish for installer '{installer.Id}'");

            foreach (var propertyName in ResolvePublishVersionProperties(versioning))
            {
                var value = ResolvePublishVersionPropertyValue(propertyName, resolved);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (properties.TryGetValue(propertyName, out var existing))
                {
                    if (!string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Installer '{installer.Id}' resolved publish property '{propertyName}' to '{value}', " +
                            $"but the target already has '{existing}'. Align installer versioning or publish the target separately.");
                    }

                    continue;
                }

                properties[propertyName] = value!;
            }
        }
    }

    private static bool TargetUsesPublishMsiVersionProperties(
        DotNetPublishPlan plan,
        string targetName,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        return (plan.Installers ?? Array.Empty<DotNetPublishInstallerPlan>())
            .Where(installer => string.Equals(installer.PrepareFromTarget, targetName, StringComparison.OrdinalIgnoreCase))
            .Any(installer =>
                installer.Versioning?.Enabled == true
                && installer.Versioning.ApplyToPublish
                && FindResolvedMsiVersion(plan, installer.Id, targetName, framework, runtime, style) is not null);
    }

    private static string[] ResolvePublishVersionProperties(DotNetPublishMsiVersionOptions versioning)
    {
        var configured = versioning.PublishProperties ?? Array.Empty<string>();
        if (configured.Length > 0)
        {
            return configured
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return new[]
        {
            "Version",
            "PackageVersion",
            "FileVersion",
            "AssemblyVersion",
            "InformationalVersion"
        };
    }

    private static string? ResolvePublishVersionPropertyValue(
        string propertyName,
        DotNetPublishMsiVersionPlan resolved)
    {
        return propertyName.Equals("AssemblyVersion", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals("FileVersion", StringComparison.OrdinalIgnoreCase)
            ? resolved.AssemblyVersion
            : resolved.Version;
    }

}

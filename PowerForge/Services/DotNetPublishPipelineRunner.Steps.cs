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
        var props = BuildMsBuildPropertyArgs(plan.MsBuildProperties);

        if (!string.IsNullOrWhiteSpace(runtime))
        {
            foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _logger.Info($"Restore ({runtime}) -> {p}");

                var args = new List<string> { "restore", p, "--nologo" };
                args.AddRange(new[] { "-r", runtime! });
                args.AddRange(props);

                RunDotnet(workDir, args);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
        {
            _logger.Info($"Restore -> {plan.SolutionPath}");
            RunDotnet(workDir, new[] { "restore", plan.SolutionPath!, "--nologo" }.Concat(props).ToArray());
            return;
        }

        foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"Restore -> {p}");
            RunDotnet(workDir, new[] { "restore", p, "--nologo" }.Concat(props).ToArray());
        }
    }

    private void Clean(DotNetPublishPlan plan)
    {
        var workDir = plan.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
        {
            _logger.Info($"Clean -> {plan.SolutionPath}");
            RunDotnet(workDir, new[] { "clean", plan.SolutionPath!, "-c", plan.Configuration, "--nologo" });
            return;
        }

        foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"Clean -> {p}");
            RunDotnet(workDir, new[] { "clean", p, "-c", plan.Configuration, "--nologo" });
        }
    }

    private void Build(DotNetPublishPlan plan, string? runtime)
    {
        var workDir = plan.ProjectRoot;
        var props = BuildMsBuildPropertyArgs(plan.MsBuildProperties);

        if (!string.IsNullOrWhiteSpace(runtime))
        {
            foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _logger.Info($"Build ({runtime}) -> {p}");

                var args = new List<string> { "build", p, "-c", plan.Configuration, "--nologo" };
                args.AddRange(new[] { "-r", runtime! });
                if (plan.Restore) args.Add("--no-restore");
                args.AddRange(props);

                RunDotnet(workDir, args);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(plan.SolutionPath))
        {
            _logger.Info($"Build -> {plan.SolutionPath}");
            var args = new List<string> { "build", plan.SolutionPath!, "-c", plan.Configuration, "--nologo" };
            if (plan.Restore) args.Add("--no-restore");
            args.AddRange(props);
            RunDotnet(workDir, args);
            return;
        }

        foreach (var p in plan.Targets.Select(t => t.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"Build -> {p}");
            RunDotnet(workDir, new[] { "build", p, "-c", plan.Configuration, "--nologo" }.Concat(props).ToArray());
        }
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
        RunDotnet(plan.ProjectRoot, publishArgs);

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
        if (plan.NoBuildInPublish) publishArgs.Add("--no-build");

        AppendPublishStyleArgs(publishArgs, target.Publish, style);
        publishArgs.AddRange(BuildMsBuildPropertyArgs(BuildPublishMsBuildProperties(plan, target, style)));
        return publishArgs;
    }

    internal static Dictionary<string, string> BuildPublishMsBuildProperties(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
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

        return merged;
    }

}

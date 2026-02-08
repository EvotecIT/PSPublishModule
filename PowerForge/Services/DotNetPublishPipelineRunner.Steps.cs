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

    private DotNetPublishArtefactResult Publish(DotNetPublishPlan plan, string targetName, string framework, string rid)
    {
        var target = plan.Targets.FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Target not found: {targetName}");

        var cfg = plan.Configuration;
        var tfm = string.IsNullOrWhiteSpace(framework) ? target.Publish.Framework : framework.Trim();
        var style = target.Publish.Style;

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
        Directory.CreateDirectory(outputDir);

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

        var publishArgs = new List<string>
        {
            "publish",
            target.ProjectPath,
            "-c", cfg,
            "--nologo",
            "-f", tfm,
            "--runtime", rid,
            "--output", publishDir
        };

        if (plan.NoRestoreInPublish) publishArgs.Add("--no-restore");
        if (plan.NoBuildInPublish) publishArgs.Add("--no-build");

        AppendPublishStyleArgs(publishArgs, target.Publish, style);
        publishArgs.AddRange(BuildMsBuildPropertyArgs(plan.MsBuildProperties));

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

        if (target.Publish.Sign?.Enabled == true)
            TrySignOutput(outputDir, target.Publish.Sign);

        string? zipPath = null;
        if (target.Publish.Zip)
            zipPath = CreateZip(outputDir, plan, target, rid, tokens);

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
            Cleanup = cleanup
        };
    }

}

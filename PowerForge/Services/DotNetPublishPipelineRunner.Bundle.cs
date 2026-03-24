using System.IO.Compression;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private DotNetPublishArtefactResult BuildBundle(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));
        if (step is null) throw new ArgumentNullException(nameof(step));

        var bundleId = (step.BundleId ?? string.Empty).Trim();
        var target = (step.TargetName ?? string.Empty).Trim();
        var framework = (step.Framework ?? string.Empty).Trim();
        var runtime = (step.Runtime ?? string.Empty).Trim();
        var style = step.Style;

        if (string.IsNullOrWhiteSpace(bundleId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing BundleId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");
        if (string.IsNullOrWhiteSpace(step.BundleOutputPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing bundle output path.");

        var bundle = (plan.Bundles ?? Array.Empty<DotNetPublishBundlePlan>())
            .FirstOrDefault(b => string.Equals(b.Id, bundleId, StringComparison.OrdinalIgnoreCase));
        if (bundle is null)
            throw new InvalidOperationException($"Bundle '{bundleId}' was not found in the plan.");

        var sourceArtefact = ResolveBundleSourceArtefact(
            artefacts,
            bundle.PrepareFromTarget,
            framework,
            runtime,
            style.Value,
            bundleId: null);

        if (sourceArtefact is null)
        {
            throw new InvalidOperationException(
                $"Bundle step '{step.Key}' could not find matching publish artefact for " +
                $"target='{bundle.PrepareFromTarget}', framework='{framework}', runtime='{runtime}', style='{style.Value}'.");
        }

        var outputDir = Path.GetFullPath(step.BundleOutputPath!);
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, outputDir, $"Bundle '{bundleId}' output path");

        if (bundle.ClearOutput && Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { /* best effort */ }
        }

        Directory.CreateDirectory(outputDir);
        DirectoryCopy(sourceArtefact.OutputDir, outputDir);

        foreach (var include in bundle.Includes ?? Array.Empty<DotNetPublishBundleIncludePlan>())
        {
            var includeFramework = string.IsNullOrWhiteSpace(include.Framework) ? framework : include.Framework!.Trim();
            var includeRuntime = string.IsNullOrWhiteSpace(include.Runtime) ? runtime : include.Runtime!.Trim();
            var includeStyle = include.Style ?? style.Value;

            var includeArtefact = ResolveBundleSourceArtefact(
                artefacts,
                include.Target,
                includeFramework,
                includeRuntime,
                includeStyle,
                bundleId: null);

            if (includeArtefact is null)
            {
                var message =
                    $"Bundle '{bundleId}' include '{include.Target}' has no matching publish artefact for " +
                    $"framework='{includeFramework}', runtime='{includeRuntime}', style='{includeStyle}'.";
                if (include.Required)
                    throw new InvalidOperationException(message);

                _logger.Warn(message);
                continue;
            }

            var includeDestination = string.IsNullOrWhiteSpace(include.Subdirectory)
                ? outputDir
                : ResolvePath(outputDir, include.Subdirectory!);
            EnsurePathWithinRoot(outputDir, includeDestination, $"Bundle '{bundleId}' include destination");
            Directory.CreateDirectory(includeDestination);
            DirectoryCopy(includeArtefact.OutputDir, includeDestination);
        }

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundle"] = bundleId,
            ["target"] = target,
            ["rid"] = runtime,
            ["framework"] = framework,
            ["style"] = style.Value.ToString(),
            ["configuration"] = plan.Configuration,
            ["projectRoot"] = plan.ProjectRoot,
            ["output"] = outputDir,
            ["sourceOutput"] = sourceArtefact.OutputDir,
            ["zip"] = step.BundleZipPath ?? string.Empty
        };

        var sourceTargetPlan = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .FirstOrDefault(entry => string.Equals(entry.Name, target, StringComparison.OrdinalIgnoreCase));
        tokens["keepSymbols"] = (sourceTargetPlan?.Publish?.KeepSymbols ?? false).ToString();
        tokens["keepDocs"] = (sourceTargetPlan?.Publish?.KeepDocs ?? false).ToString();
        tokens["signEnabled"] = (sourceTargetPlan?.Publish?.Sign?.Enabled ?? false).ToString();

        RunBundleScripts(plan, bundle, tokens);

        string? zipPath = null;
        if (bundle.Zip)
            zipPath = CreateBundleZip(plan, bundle, outputDir, step.BundleZipPath);

        var summary = SummarizeDirectory(outputDir, runtime);
        return new DotNetPublishArtefactResult
        {
            Category = DotNetPublishArtefactCategory.Bundle,
            Target = target,
            BundleId = bundleId,
            Kind = sourceArtefact.Kind,
            Runtime = runtime,
            Framework = framework,
            Style = style.Value,
            PublishDir = outputDir,
            OutputDir = outputDir,
            ZipPath = zipPath,
            Files = summary.Files,
            TotalBytes = summary.TotalBytes,
            ExePath = summary.ExePath,
            ExeBytes = summary.ExeBytes
        };
    }

    private static DotNetPublishArtefactResult? ResolveBundleSourceArtefact(
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        string target,
        string framework,
        string runtime,
        DotNetPublishStyle style,
        string? bundleId)
    {
        return (artefacts ?? Array.Empty<DotNetPublishArtefactResult>())
            .LastOrDefault(a =>
                string.Equals(a.Target, target, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Framework, framework, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Runtime, runtime, StringComparison.OrdinalIgnoreCase)
                && a.Style == style
                && string.Equals(a.BundleId, bundleId, StringComparison.OrdinalIgnoreCase)
                && (bundleId is null
                    ? a.Category == DotNetPublishArtefactCategory.Publish
                    : a.Category == DotNetPublishArtefactCategory.Bundle));
    }

    private void RunBundleScripts(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var script in bundle.Scripts ?? Array.Empty<DotNetPublishBundleScriptPlan>())
        {
            if (script is null) continue;

            var scriptPath = Path.GetFullPath(script.Path);
            var args = (script.Arguments ?? Array.Empty<string>())
                .Select(arg => ApplyTemplate(arg, tokens))
                .ToArray();
            var workingDirectory = string.IsNullOrWhiteSpace(script.WorkingDirectory)
                ? plan.ProjectRoot
                : ResolvePath(plan.ProjectRoot, ApplyTemplate(script.WorkingDirectory!, tokens));

            try
            {
                _logger.Info($"Bundle script -> {Path.GetFileName(scriptPath)} ({bundle.Id})");
                RunPowerShellScript(
                    scriptPath,
                    args,
                    workingDirectory,
                    TimeSpan.FromSeconds(Math.Max(1, script.TimeoutSeconds)),
                    script.PreferPwsh);
            }
            catch when (!script.Required)
            {
                _logger.Warn($"Bundle script failed but is optional: {scriptPath}");
            }
        }
    }

    private string? CreateBundleZip(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        string outputDir,
        string? zipPathOverride)
    {
        try
        {
            var zipPath = string.IsNullOrWhiteSpace(zipPathOverride)
                ? null
                : Path.GetFullPath(zipPathOverride!);
            if (string.IsNullOrWhiteSpace(zipPath))
                throw new InvalidOperationException($"Bundle '{bundle.Id}' zip path was not resolved.");

            if (!plan.AllowOutputOutsideProjectRoot)
                EnsurePathWithinRoot(plan.ProjectRoot, zipPath!, $"Bundle '{bundle.Id}' zip path");

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath!)!);
            if (File.Exists(zipPath!))
                File.Delete(zipPath!);

            ZipFile.CreateFromDirectory(outputDir, zipPath!);
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to create zip for bundle '{bundle.Id}'. Error: {ex.Message}");
            return null;
        }
    }

    private void RunPowerShellScript(
        string scriptPath,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        bool preferPwsh)
    {
        var runner = new PowerShellRunner();
        var result = runner.Run(new PowerShellRunRequest(
            scriptPath,
            args,
            timeout,
            preferPwsh: preferPwsh,
            workingDirectory: workingDirectory));

        if (result.ExitCode != 0)
        {
            var stderr = (result.StdErr ?? string.Empty).TrimEnd();
            var stdout = (result.StdOut ?? string.Empty).TrimEnd();
            var stderrTail = TailLines(stderr, maxLines: 80, maxChars: 8000);
            var stdoutTail = TailLines(stdout, maxLines: 80, maxChars: 8000);
            var message = ExtractLastNonEmptyLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
            if (string.IsNullOrWhiteSpace(message))
                message = $"PowerShell script failed: {scriptPath}";

            throw new DotNetPublishCommandException(
                message,
                result.Executable,
                string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                args.Prepend(scriptPath).Prepend("-File").ToArray(),
                result.ExitCode,
                stdout,
                stderr);
        }

        if (_logger.IsVerbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.TrimEnd());
        }
    }
}

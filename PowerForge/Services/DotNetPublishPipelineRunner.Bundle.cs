using System.IO.Compression;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal DotNetPublishArtefactResult BuildBundle(
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
        var primaryDestination = string.IsNullOrWhiteSpace(bundle.PrimarySubdirectory)
            ? outputDir
            : ResolvePath(outputDir, bundle.PrimarySubdirectory!);
        EnsurePathWithinRoot(outputDir, primaryDestination, $"Bundle '{bundleId}' primary destination");
        Directory.CreateDirectory(primaryDestination);
        DirectoryCopy(sourceArtefact.OutputDir, primaryDestination);

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
            ["primaryOutput"] = primaryDestination,
            ["zip"] = step.BundleZipPath ?? string.Empty
        };

        var sourceTargetPlan = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
            .FirstOrDefault(entry => string.Equals(entry.Name, target, StringComparison.OrdinalIgnoreCase));
        tokens["keepSymbols"] = (sourceTargetPlan?.Publish?.KeepSymbols ?? false).ToString();
        tokens["keepDocs"] = (sourceTargetPlan?.Publish?.KeepDocs ?? false).ToString();
        tokens["signEnabled"] = (sourceTargetPlan?.Publish?.Sign?.Enabled ?? false).ToString();

        CopyBundleItems(plan, bundle, outputDir, tokens);
        CopyBundleModules(plan, bundle, outputDir, tokens);
        GenerateBundleScripts(plan, bundle, outputDir, tokens);
        RunBundleScripts(plan, bundle, tokens);
        if (bundle.PostProcess is not null)
        {
            _ = new PowerForgeBundlePostProcessService(_logger).Run(new PowerForgeBundlePostProcessRequest
            {
                ProjectRoot = plan.ProjectRoot,
                AllowOutputOutsideProjectRoot = plan.AllowOutputOutsideProjectRoot,
                BundleRoot = outputDir,
                BundleId = bundle.Id,
                TargetName = step.TargetName,
                Runtime = step.Runtime,
                Framework = step.Framework,
                Style = step.Style?.ToString(),
                Configuration = plan.Configuration,
                ZipPath = step.BundleZipPath,
                SourceOutputPath = sourceArtefact.OutputDir,
                PostProcess = bundle.PostProcess
            });

            SignBundlePostProcessFiles(plan, bundle, outputDir);
        }

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

    private void CopyBundleItems(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        string outputDir,
        IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var item in bundle.CopyItems ?? Array.Empty<DotNetPublishBundleCopyItemPlan>())
        {
            if (item is null) continue;

            var source = ResolvePath(plan.ProjectRoot, ApplyTemplate(item.SourcePath, tokens));
            var destination = ResolvePath(outputDir, ApplyTemplate(item.DestinationPath, tokens));
            EnsurePathWithinRoot(outputDir, destination, $"Bundle '{bundle.Id}' copy destination");

            CopyBundlePath(source, destination, item.Required, item.ClearDestination, $"Bundle '{bundle.Id}' copy item");
        }
    }

    private void CopyBundleModules(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        string outputDir,
        IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var module in bundle.ModuleIncludes ?? Array.Empty<DotNetPublishBundleModuleIncludePlan>())
        {
            if (module is null) continue;

            var moduleTokens = tokens.ToDictionary(
                token => token.Key,
                token => token.Value,
                StringComparer.OrdinalIgnoreCase);
            moduleTokens["moduleName"] = module.ModuleName;
            var source = ResolvePath(plan.ProjectRoot, ApplyTemplate(module.SourcePath, moduleTokens));
            var destination = ResolvePath(outputDir, ApplyTemplate(module.DestinationPath, moduleTokens));
            EnsurePathWithinRoot(outputDir, destination, $"Bundle '{bundle.Id}' module include destination");

            CopyBundlePath(source, destination, module.Required, module.ClearDestination, $"Bundle '{bundle.Id}' module include '{module.ModuleName}'");
        }
    }

    private void GenerateBundleScripts(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        string outputDir,
        IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var script in bundle.GeneratedScripts ?? Array.Empty<DotNetPublishBundleGeneratedScriptPlan>())
        {
            if (script is null) continue;

            var outputPath = ResolvePath(outputDir, ApplyTemplate(script.OutputPath, tokens));
            EnsurePathWithinRoot(outputDir, outputPath, $"Bundle '{bundle.Id}' generated script output path");
            if (File.Exists(outputPath) && !script.Overwrite)
                throw new IOException($"Generated script already exists and Overwrite=false: {outputPath}");

            var templateName = script.TemplatePath ?? script.OutputPath;
            var template = script.Template;
            if (!string.IsNullOrWhiteSpace(script.TemplatePath))
            {
                var templatePath = ResolvePath(plan.ProjectRoot, ApplyTemplate(script.TemplatePath!, tokens));
                EnsurePathWithinRoot(plan.ProjectRoot, templatePath, $"Bundle '{bundle.Id}' generated script template path");
                if (!File.Exists(templatePath))
                    throw new FileNotFoundException($"Generated script template not found for bundle '{bundle.Id}': {templatePath}", templatePath);
                templateName = templatePath;
                template = File.ReadAllText(templatePath, Encoding.UTF8);
            }

            var renderTokens = tokens.ToDictionary(
                token => token.Key,
                token => token.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var token in script.Tokens ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                renderTokens[token.Key] = ApplyTemplate(token.Value ?? string.Empty, tokens);

            var rendered = ScriptTemplateRenderer.Render(templateName ?? "bundle generated script", template ?? string.Empty, renderTokens);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            _logger.Info($"Generated bundle script -> {FrameworkCompatibility.GetRelativePath(outputDir, outputPath)} ({bundle.Id})");
            if (script.Sign is not null && script.Sign.Enabled)
                _ = TrySignFiles(new[] { outputPath }, outputDir, script.Sign, scope: $"bundle '{bundle.Id}' generated scripts");
        }
    }

    private void CopyBundlePath(
        string source,
        string destination,
        bool required,
        bool clearDestination,
        string description)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);

        if (Directory.Exists(source))
        {
            if (clearDestination && Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            else if (clearDestination && File.Exists(destination))
                File.Delete(destination);
            else if (!clearDestination && (Directory.Exists(destination) || File.Exists(destination)))
                throw new IOException($"{description}: destination already exists and ClearDestination=false: {destination}");

            DirectoryCopy(source, destination);
            return;
        }

        if (File.Exists(source))
        {
            if (clearDestination && Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);

            var destinationFile = destination;
            if (Directory.Exists(destinationFile))
                destinationFile = Path.Combine(destinationFile, Path.GetFileName(source));

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (clearDestination && File.Exists(destinationFile))
                File.Delete(destinationFile);
            if (!clearDestination && File.Exists(destinationFile))
                throw new IOException($"{description}: destination already exists and ClearDestination=false: {destinationFile}");
            File.Copy(source, destinationFile, overwrite: clearDestination);
            return;
        }

        var message = $"{description} source not found: {source}";
        if (required)
            throw new FileNotFoundException(message, source);

        _logger.Warn(message);
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

    private void SignBundlePostProcessFiles(
        DotNetPublishPlan plan,
        DotNetPublishBundlePlan bundle,
        string outputDir)
    {
        var sign = bundle.PostProcess?.Sign;
        if (sign is null || !sign.Enabled)
            return;

        var patterns = NormalizeBundleSignPatterns(bundle.PostProcess?.SignPatterns, sign);
        var targets = FindBundleSignTargets(outputDir, patterns);
        var signed = TrySignFiles(
            targets,
            outputDir,
            sign,
            scope: $"bundle '{bundle.Id}' files");

        _logger.Info($"Bundle sign completed for '{bundle.Id}' -> {signed.Length}/{targets.Length} signed.");
    }

    internal static string[] NormalizeBundleSignPatterns(
        IReadOnlyList<string>? patterns,
        DotNetPublishSignOptions sign)
    {
        var normalized = (patterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern.Trim().Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length > 0)
            return normalized;

        var defaults = new List<string> { "**/*.exe" };
        if (sign.IncludeDlls)
            defaults.Add("**/*.dll");
        return defaults.ToArray();
    }

    internal static string[] FindBundleSignTargets(string bundleRoot, IReadOnlyList<string>? patterns)
    {
        var root = Path.GetFullPath(bundleRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Bundle root was not found: {root}");

        var matches = new List<string>();
        foreach (var pattern in patterns ?? Array.Empty<string>())
        {
            var normalizedPattern = (pattern ?? string.Empty)
                .Trim()
                .Replace('\\', '/');
            if (normalizedPattern.Length == 0)
                continue;

            var exactPath = ResolvePath(root, normalizedPattern);
            if (File.Exists(exactPath))
            {
                EnsurePathWithinRoot(root, exactPath, "Bundle signing target");
                matches.Add(exactPath);
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = GetRelativePath(root, file).Replace('\\', '/');
                if (BundleSignPatternMatches(relative, normalizedPattern))
                    matches.Add(Path.GetFullPath(file));
            }
        }

        return matches
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool BundleSignPatternMatches(string relativePath, string pattern)
    {
        if (WildcardMatch(relativePath, pattern))
            return true;

        var fileName = Path.GetFileName(relativePath);
        if (WildcardMatch(fileName, pattern))
            return true;

        if (pattern.StartsWith("**/", StringComparison.Ordinal))
            return WildcardMatch(relativePath, pattern.Substring("**/".Length));

        return false;
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven dotnet publish workflow using <c>dotnet</c>.
/// </summary>
public sealed class DotNetPublishPipelineRunner
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public DotNetPublishPipelineRunner(ILogger logger) => _logger = logger;

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
            if (string.IsNullOrWhiteSpace(t.ProjectPath))
                throw new ArgumentException($"Target.ProjectPath is required for '{t.Name}'.", nameof(spec));
            if (t.Publish is null)
                throw new ArgumentException($"Target.Publish is required for '{t.Name}'.", nameof(spec));

            var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworks.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(t.Publish.Framework))
                    throw new ArgumentException($"Target.Publish.Framework is required for '{t.Name}' (or set Target.Publish.Frameworks).", nameof(spec));
                frameworks = new[] { t.Publish.Framework.Trim() };
            }

            var projectPath = ResolvePath(projectRoot, t.ProjectPath);
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"ProjectPath not found for '{t.Name}': {projectPath}");

            var rids = (t.Publish.Runtimes ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (rids.Length == 0) rids = defaultsRids;
            if (rids.Length == 0)
                throw new ArgumentException($"No runtimes provided for target '{t.Name}'. Set Target.Publish.Runtimes or DotNet.Runtimes.", nameof(spec));

            // Clone publish settings for the plan and force resolved runtimes.
            var publish = new DotNetPublishPublishOptions
            {
                Style = t.Publish.Style,
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
                Sign = t.Publish.Sign
            };

            targets.Add(new DotNetPublishTargetPlan
            {
                Name = t.Name.Trim(),
                Kind = t.Kind,
                ProjectPath = projectPath,
                Publish = publish
            });
        }

        var outputs = new DotNetPublishOutputs
        {
            ManifestJsonPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestJsonPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.json"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestJsonPath!),
            ManifestTextPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestTextPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.txt"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestTextPath!)
        };

        var steps = new List<DotNetPublishStep>();

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
            var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworks.Length == 0 && !string.IsNullOrWhiteSpace(t.Publish.Framework))
                frameworks = new[] { t.Publish.Framework.Trim() };

            foreach (var framework in frameworks)
            {
                foreach (var rid in t.Publish.Runtimes)
                {
                    var key = $"publish:{t.Name}:{framework}:{rid}";
                    steps.Add(new DotNetPublishStep
                    {
                        Key = key,
                        Kind = DotNetPublishStepKind.Publish,
                        Title = "Publish",
                        TargetName = t.Name,
                        Framework = framework,
                        Runtime = rid
                    });
                }
            }
        }

        steps.Add(new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" });

        return new DotNetPublishPlan
        {
            ProjectRoot = projectRoot,
            Configuration = cfg,
            SolutionPath = solutionPath,
            Restore = spec.DotNet.Restore,
            Clean = spec.DotNet.Clean,
            Build = spec.DotNet.Build,
            NoRestoreInPublish = spec.DotNet.NoRestoreInPublish,
            NoBuildInPublish = spec.DotNet.NoBuildInPublish,
            MsBuildProperties = msbuildProps,
            Targets = targets.ToArray(),
            Outputs = outputs,
            Steps = steps.ToArray()
        };
    }

    /// <summary>
    /// Executes the provided <paramref name="plan"/>.
    /// </summary>
    public DotNetPublishResult Run(DotNetPublishPlan plan, IDotNetPublishProgressReporter? progress)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        progress ??= NullDotNetPublishProgressReporter.Instance;

        var artefacts = new List<DotNetPublishArtefactResult>();
        string? manifestJson = null;
        string? manifestText = null;

        try
        {
            foreach (var step in plan.Steps ?? Array.Empty<DotNetPublishStep>())
            {
                progress.StepStarting(step);
                try
                {
                    switch (step.Kind)
                    {
                        case DotNetPublishStepKind.Restore:
                            Restore(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Clean:
                            Clean(plan);
                            break;
                        case DotNetPublishStepKind.Build:
                            Build(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Publish:
                            artefacts.Add(Publish(plan, step.TargetName!, step.Framework ?? string.Empty, step.Runtime!));
                            break;
                        case DotNetPublishStepKind.Manifest:
                            (manifestJson, manifestText) = WriteManifests(plan, artefacts);
                            break;
                    }

                    progress.StepCompleted(step);
                }
                catch (Exception ex)
                {
                    progress.StepFailed(step, ex);
                    throw new DotNetPublishStepException(step, ex);
                }
            }

            return new DotNetPublishResult
            {
                Succeeded = true,
                Artefacts = artefacts.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText
            };
        }
        catch (Exception ex)
        {
            var failure = BuildFailure(plan, ex, out var errorMessage);

            _logger.Error(errorMessage);
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return new DotNetPublishResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage,
                Failure = failure,
                Artefacts = artefacts.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText
            };
        }
    }

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

    private static void AppendPublishStyleArgs(List<string> args, DotNetPublishPublishOptions publish, DotNetPublishStyle style)
    {
        switch (style)
        {
            case DotNetPublishStyle.Portable:
            case DotNetPublishStyle.PortableCompat:
            case DotNetPublishStyle.PortableSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:PublishSingleFile=true");
                args.Add("/p:IncludeNativeLibrariesForSelfExtract=true");

                // Match TestimoX pattern: use project-controlled trimming via PortableTrim.
                var trim = style == DotNetPublishStyle.PortableSize;
                var trimMode = style == DotNetPublishStyle.PortableSize ? "full" : "partial";
                args.Add($"/p:PortableTrim={trim.ToString().ToLowerInvariant()}");
                args.Add($"/p:PortableTrimMode={trimMode}");

                if (publish.ReadyToRun.HasValue)
                    args.Add($"/p:PublishReadyToRun={publish.ReadyToRun.Value.ToString().ToLowerInvariant()}");

                break;
            }
            case DotNetPublishStyle.AotSpeed:
            case DotNetPublishStyle.AotSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:NativeAotPublish=true");
                args.Add("/p:StripSymbols=true");
                args.Add($"/p:IlcOptimizationPreference={(style == DotNetPublishStyle.AotSize ? "Size" : "Speed")}");
                args.Add("/p:InvariantGlobalization=false");
                break;
            }
        }
    }

    private DotNetPublishCleanupResult ApplyCleanup(string publishDir, DotNetPublishPublishOptions publish)
    {
        int pdbRemoved = 0;
        int docsRemoved = 0;
        bool refPruned = false;

        try
        {
            var recurse = publish.Slim ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (!publish.KeepSymbols)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdb", recurse))
                    TryDeleteFile(file, ref pdbRemoved);
            }

            if (!publish.KeepDocs)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.xml", recurse))
                    TryDeleteFile(file, ref docsRemoved);
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdf", recurse))
                    TryDeleteFile(file, ref docsRemoved);
            }

            if (publish.PruneReferences)
            {
                var refDir = Path.Combine(publishDir, "ref");
                if (Directory.Exists(refDir))
                {
                    try
                    {
                        Directory.Delete(refDir, recursive: true);
                        refPruned = true;
                    }
                    catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Cleanup failed. Error: {ex.Message}");
        }

        return new DotNetPublishCleanupResult { PdbRemoved = pdbRemoved, DocsRemoved = docsRemoved, RefPruned = refPruned };
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
    {
        try { return Directory.EnumerateFiles(root, pattern, option); }
        catch { return Array.Empty<string>(); }
    }

    private static void TryDeleteFile(string path, ref int counter)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            counter++;
        }
        catch { /* best effort */ }
    }

    private void TryRenameMainExecutable(string publishDir, string rid, string renameTo)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        var desired = renameTo;
        if (isWindowsRid && !desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired += ".exe";
        if (!isWindowsRid && desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired = Path.GetFileNameWithoutExtension(desired);

        var candidate = FindMainExecutable(publishDir, rid);
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            return;

        var dest = Path.Combine(Path.GetDirectoryName(candidate)!, desired);
        if (string.Equals(candidate, dest, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(candidate, dest);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to rename executable ({rid}). Error: {ex.Message}");
        }
    }

    private static string? FindMainExecutable(string root, string rid)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (isWindowsRid)
            {
                var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Length)
                    .ToArray();
                return exes.FirstOrDefault()?.FullName;
            }

            var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(f => string.IsNullOrWhiteSpace(f.Extension))
                .OrderByDescending(f => f.Length)
                .ToArray();
            return files.FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static (int Files, long TotalBytes, string? ExePath, long? ExeBytes) SummarizeDirectory(string dir, string rid)
    {
        try
        {
            var all = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .ToArray();

            long total = 0;
            foreach (var f in all) total += f.Length;

            var exe = FindMainExecutable(dir, rid);
            long? exeBytes = null;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                exeBytes = new FileInfo(exe).Length;

            return (all.Length, total, exe, exeBytes);
        }
        catch
        {
            return (0, 0, null, null);
        }
    }

    private string? CreateZip(string outputDir, DotNetPublishPlan plan, DotNetPublishTargetPlan target, string rid, IReadOnlyDictionary<string, string> tokens)
    {
        try
        {
            var nameTemplate = string.IsNullOrWhiteSpace(target.Publish.ZipNameTemplate)
                ? "{target}-{framework}-{rid}-{style}.zip"
                : target.Publish.ZipNameTemplate!;

            var zipName = ApplyTemplate(nameTemplate, tokens);
            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipName += ".zip";

            var zipPath = string.IsNullOrWhiteSpace(target.Publish.ZipPath)
                ? Path.Combine(Path.GetDirectoryName(outputDir)!, zipName)
                : ResolvePath(plan.ProjectRoot, ApplyTemplate(target.Publish.ZipPath!, tokens));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            ZipFile.CreateFromDirectory(outputDir, zipPath);
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to create zip for '{target.Name}' ({rid}). Error: {ex.Message}");
            return null;
        }
    }

    private void TrySignOutput(string outputDir, DotNetPublishSignOptions sign)
    {
        if (sign is null || !sign.Enabled) return;
        if (!IsWindows())
        {
            _logger.Warn("Signing requested but current OS is not Windows. Skipping signing.");
            return;
        }

        var signTool = ResolveSignToolPath(sign.ToolPath);
        if (string.IsNullOrWhiteSpace(signTool))
        {
            _logger.Warn("Signing requested but signtool.exe was not found. Skipping signing.");
            return;
        }

        var signToolPath = signTool!;
        if (!File.Exists(signToolPath))
        {
            _logger.Warn($"Signing requested but signtool.exe was not found: {signToolPath}. Skipping signing.");
            return;
        }

        var targets = new List<string>();
        try
        {
            targets.AddRange(Directory.EnumerateFiles(outputDir, "*.exe", SearchOption.AllDirectories));
            targets.AddRange(Directory.EnumerateFiles(outputDir, "*.dll", SearchOption.AllDirectories));
        }
        catch
        {
            // ignore
        }

        if (targets.Count == 0) return;

        _logger.Info($"Signing {targets.Count} file(s) using {Path.GetFileName(signToolPath)}");
        foreach (var file in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var args = new List<string> { "sign", "/fd", "SHA256" };
            if (!string.IsNullOrWhiteSpace(sign.TimestampUrl))
                args.AddRange(new[] { "/tr", sign.TimestampUrl!, "/td", "SHA256" });
            if (!string.IsNullOrWhiteSpace(sign.Description))
                args.AddRange(new[] { "/d", sign.Description! });
            if (!string.IsNullOrWhiteSpace(sign.Url))
                args.AddRange(new[] { "/du", sign.Url! });

            if (!string.IsNullOrWhiteSpace(sign.Thumbprint))
                args.AddRange(new[] { "/sha1", sign.Thumbprint! });
            else if (!string.IsNullOrWhiteSpace(sign.SubjectName))
                args.AddRange(new[] { "/n", sign.SubjectName! });
            else
                args.Add("/a");

            if (!string.IsNullOrWhiteSpace(sign.Csp))
                args.AddRange(new[] { "/csp", sign.Csp! });
            if (!string.IsNullOrWhiteSpace(sign.KeyContainer))
                args.AddRange(new[] { "/kc", sign.KeyContainer! });

            args.Add(file);
            var res = RunProcess(signToolPath, outputDir, args);
            if (res.ExitCode != 0)
                _logger.Warn($"Signing failed for '{file}'. {res.StdErr}".Trim());
        }
    }

    private static bool IsWindows()
    {
#if NET472
        return true;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    private static string? ResolveSignToolPath(string? toolPath)
    {
        if (!string.IsNullOrWhiteSpace(toolPath))
        {
            var raw = toolPath!.Trim().Trim('\"');
            if (File.Exists(raw)) return Path.GetFullPath(raw);

            var onPath = ResolveOnPath(raw);
            if (!string.IsNullOrWhiteSpace(onPath)) return onPath;
        }

        try
        {
            var kitsRoot = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (string.IsNullOrWhiteSpace(kitsRoot)) return null;
            var baseDir = Path.Combine(kitsRoot, "Windows Kits", "10", "bin");
            if (!Directory.Exists(baseDir)) return null;

            var versions = Directory.EnumerateDirectories(baseDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name)
                .ToArray();

            foreach (var ver in versions)
            {
                foreach (var arch in new[] { "x64", "x86" })
                {
                    var candidate = Path.Combine(ver.FullName, arch, "signtool.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ResolveOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    private static void DirectoryCopy(string sourceDir, string destDir)
    {
        var source = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        Directory.CreateDirectory(dest);

        var sourcePrefix = source + Path.DirectorySeparatorChar;
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(dir);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
        }
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var t = template ?? string.Empty;
        foreach (var kv in tokens)
            t = ReplaceOrdinalIgnoreCase(t, "{" + kv.Key + "}", kv.Value ?? string.Empty);
        return t;
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        if (string.IsNullOrEmpty(oldValue)) return input;

        var startIndex = 0;
        var idx = input.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return input;

        var sb = new StringBuilder(input.Length);
        while (idx >= 0)
        {
            sb.Append(input, startIndex, idx - startIndex);
            sb.Append(newValue ?? string.Empty);
            startIndex = idx + oldValue.Length;
            idx = input.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        sb.Append(input, startIndex, input.Length - startIndex);
        return sb.ToString();
    }

    private void RunDotnet(string workingDir, IReadOnlyList<string> args)
    {
        var result = RunProcess("dotnet", workingDir, args);
        if (result.ExitCode != 0)
        {
            var stderr = (result.StdErr ?? string.Empty).TrimEnd();
            var stdout = (result.StdOut ?? string.Empty).TrimEnd();

            var stderrTail = TailLines(stderr, maxLines: 80, maxChars: 8000);
            var stdoutTail = TailLines(stdout, maxLines: 80, maxChars: 8000);

            var msg = ExtractLastNonEmptyLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
            if (string.IsNullOrWhiteSpace(msg)) msg = "dotnet failed.";

            throw new DotNetPublishCommandException(
                message: msg,
                fileName: "dotnet",
                workingDirectory: string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
                args: args,
                exitCode: result.ExitCode,
                stdOut: stdout,
                stdErr: stderr);
        }

        if (_logger.IsVerbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.TrimEnd());
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string workingDir, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var a in args) psi.ArgumentList.Add(a);
#endif

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    // Based on .NET's internal ProcessStartInfo quoting behavior for Windows CreateProcess.
    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        bool needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new StringBuilder();
        sb.Append('"');

        int backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif

    private static string? TailLines(string? text, int maxLines, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = (text ?? string.Empty).Replace("\r\n", "\n");
        var end = normalized.Length;
        if (end == 0) return null;

        maxLines = Math.Max(1, maxLines);
        maxChars = Math.Max(1, maxChars);

        int lines = 0;
        int start = 0;
        for (int i = end - 1; i >= 0; i--)
        {
            if (normalized[i] == '\n')
            {
                lines++;
                if (lines > maxLines)
                {
                    start = i + 1;
                    break;
                }
            }
        }

        var tail = normalized.Substring(start).TrimEnd();
        if (tail.Length > maxChars)
            tail = tail.Substring(tail.Length - maxChars);
        return tail;
    }

    private static string ExtractLastNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var lines = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = (lines[i] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }

        return (text ?? string.Empty).Trim();
    }

    private static string ToSafeFileName(string? input, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(input) ? fallback : input!.Trim();
        if (string.IsNullOrWhiteSpace(s)) s = fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var c = ch == ':' ? '_' : ch;
            if (Array.IndexOf(invalid, c) >= 0) sb.Append('_');
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private static DotNetPublishFailure? BuildFailure(DotNetPublishPlan plan, Exception ex, out string errorMessage)
    {
        errorMessage = ex?.Message ?? "dotnet publish failed.";

        if (ex is not DotNetPublishStepException stepEx)
            return null;

        var inner = stepEx.InnerException ?? stepEx;
        errorMessage = inner.Message;

        var failure = new DotNetPublishFailure
        {
            StepKey = stepEx.Step.Key ?? string.Empty,
            StepKind = stepEx.Step.Kind,
            TargetName = stepEx.Step.TargetName,
            Framework = stepEx.Step.Framework,
            Runtime = stepEx.Step.Runtime,
        };

        if (inner is not DotNetPublishCommandException cmdEx)
            return failure;

        failure.ExitCode = cmdEx.ExitCode;
        failure.CommandLine = cmdEx.CommandLine;
        failure.WorkingDirectory = cmdEx.WorkingDirectory;
        failure.StdOutTail = TailLines(cmdEx.StdOut, maxLines: 80, maxChars: 8000);
        failure.StdErrTail = TailLines(cmdEx.StdErr, maxLines: 80, maxChars: 8000);
        failure.LogPath = TryWriteFailureLog(plan, stepEx.Step, cmdEx);

        return failure;
    }

    private static string? TryWriteFailureLog(DotNetPublishPlan plan, DotNetPublishStep step, DotNetPublishCommandException ex)
    {
        try
        {
            var logDir = Path.Combine(plan.ProjectRoot, "Artifacts", "DotNetPublish", "logs");
            Directory.CreateDirectory(logDir);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var safeKey = ToSafeFileName(step?.Key, "step");
            var fileName = $"dotnetpublish-failure-{stamp}-{safeKey}.log";
            var fullPath = Path.Combine(logDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("=== dotnet publish failure ===");
            sb.AppendLine($"Step: {step?.Key} ({step?.Kind})");
            if (!string.IsNullOrWhiteSpace(step?.TargetName)) sb.AppendLine($"Target: {step!.TargetName}");
            if (!string.IsNullOrWhiteSpace(step?.Framework)) sb.AppendLine($"Framework: {step!.Framework}");
            if (!string.IsNullOrWhiteSpace(step?.Runtime)) sb.AppendLine($"Runtime: {step!.Runtime}");
            sb.AppendLine($"ExitCode: {ex.ExitCode}");
            sb.AppendLine($"WorkingDirectory: {ex.WorkingDirectory}");
            sb.AppendLine($"CommandLine: {ex.CommandLine}");
            sb.AppendLine();
            sb.AppendLine("--- stdout ---");
            if (!string.IsNullOrWhiteSpace(ex.StdOut)) sb.AppendLine(ex.StdOut.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("--- stderr ---");
            if (!string.IsNullOrWhiteSpace(ex.StdErr)) sb.AppendLine(ex.StdErr.TrimEnd());

            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> BuildMsBuildPropertyArgs(IReadOnlyDictionary<string, string> props)
    {
        if (props is null || props.Count == 0) return Array.Empty<string>();
        return props
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => $"/p:{kv.Key}={kv.Value}");
    }

    private static string ResolvePath(string baseDir, string path)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static (string? ManifestJson, string? ManifestText) WriteManifests(DotNetPublishPlan plan, List<DotNetPublishArtefactResult> artefacts)
    {
        var jsonPath = plan.Outputs.ManifestJsonPath;
        var txtPath = plan.Outputs.ManifestTextPath;

        if (!string.IsNullOrWhiteSpace(jsonPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath))!);
            var json = JsonSerializer.Serialize(artefacts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        if (!string.IsNullOrWhiteSpace(txtPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(txtPath))!);
            var lines = new List<string>();
            foreach (var a in artefacts)
            {
                var mb = a.TotalBytes / 1024d / 1024d;
                var exeMb = a.ExeBytes.HasValue ? (a.ExeBytes.Value / 1024d / 1024d) : 0;
                var zip = string.IsNullOrWhiteSpace(a.ZipPath) ? string.Empty : $" zip={a.ZipPath}";
                lines.Add($"{a.Target} ({a.Framework}, {a.Runtime}) -> {a.OutputDir} ({a.Files} files, {mb:N1} MB; exe {exeMb:N1} MB){zip}");
            }
            File.WriteAllLines(txtPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return (jsonPath, txtPath);
    }

    private sealed class NullDotNetPublishProgressReporter : IDotNetPublishProgressReporter
    {
        public static readonly NullDotNetPublishProgressReporter Instance = new();
        private NullDotNetPublishProgressReporter() { }
        public void StepStarting(DotNetPublishStep step) { }
        public void StepCompleted(DotNetPublishStep step) { }
        public void StepFailed(DotNetPublishStep step, Exception error) { }
    }

    private sealed class DotNetPublishStepException : Exception
    {
        public DotNetPublishStep Step { get; }

        public DotNetPublishStepException(DotNetPublishStep step, Exception inner)
            : base($"Step '{(step ?? throw new ArgumentNullException(nameof(step))).Key}' failed. {(inner ?? throw new ArgumentNullException(nameof(inner))).Message}", inner)
        {
            Step = step;
        }
    }

    private sealed class DotNetPublishCommandException : Exception
    {
        public int ExitCode { get; }
        public string FileName { get; }
        public string WorkingDirectory { get; }
        public string StdOut { get; }
        public string StdErr { get; }
        public string[] Args { get; }

        public string CommandLine
        {
            get
            {
#if NET472
                return FileName + " " + BuildWindowsArgumentString(Args);
#else
                return FileName + " " + string.Join(" ", Args.Select(EscapeForCommandLine));
#endif
            }
        }

        public DotNetPublishCommandException(
            string message,
            string fileName,
            string workingDirectory,
            IReadOnlyList<string> args,
            int exitCode,
            string stdOut,
            string stdErr)
            : base(message)
        {
            ExitCode = exitCode;
            FileName = fileName ?? string.Empty;
            WorkingDirectory = workingDirectory ?? string.Empty;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
            Args = args is null ? Array.Empty<string>() : args.ToArray();
        }

#if !NET472
        private static string EscapeForCommandLine(string arg)
        {
            if (arg is null) return "\"\"";
            if (arg.Length == 0) return "\"\"";
            return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
                ? "\"" + arg.Replace("\"", "\\\"") + "\""
                : arg;
        }
#endif
    }
}

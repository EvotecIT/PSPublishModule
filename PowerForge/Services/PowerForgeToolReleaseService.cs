using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Builds downloadable runtime-specific tool executables from a typed configuration.
/// </summary>
internal sealed class PowerForgeToolReleaseService
{
    private readonly ILogger _logger;
    private readonly Func<ProcessStartInfo, ProcessExecutionResult> _runProcess;

    /// <summary>
    /// Creates a new tool release service.
    /// </summary>
    public PowerForgeToolReleaseService(ILogger logger)
        : this(logger, RunProcess)
    {
    }

    internal PowerForgeToolReleaseService(
        ILogger logger,
        Func<ProcessStartInfo, ProcessExecutionResult> runProcess)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runProcess = runProcess ?? throw new ArgumentNullException(nameof(runProcess));
    }

    /// <summary>
    /// Plans tool outputs without executing publish commands.
    /// </summary>
    public PowerForgeToolReleasePlan Plan(PowerForgeToolReleaseSpec spec, string? configPath, PowerForgeReleaseRequest? request = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var projectRoot = string.IsNullOrWhiteSpace(spec.ProjectRoot)
            ? configDir
            : ResolvePath(configDir, spec.ProjectRoot!);

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"Tool release ProjectRoot not found: {projectRoot}");

        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var selectedTargets = NormalizeStrings(request?.Targets);
        var overrideRuntimes = NormalizeStrings(request?.Runtimes);
        var overrideFrameworks = NormalizeStrings(request?.Frameworks);
        var overrideFlavors = NormalizeFlavors(request?.Flavors);

        var plans = new List<PowerForgeToolReleaseTargetPlan>();
        foreach (var target in spec.Targets ?? Array.Empty<PowerForgeToolReleaseTarget>())
        {
            if (target is null)
                continue;

            var name = (target.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tools.Targets[].Name is required.", nameof(spec));

            if (selectedTargets.Length > 0 && !selectedTargets.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var projectPath = ResolvePath(projectRoot, target.ProjectPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(target.ProjectPath))
                throw new ArgumentException($"Tools target '{name}' requires ProjectPath.", nameof(spec));
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Tool release project not found for target '{name}': {projectPath}", projectPath);

            if (!CsprojVersionEditor.TryGetVersion(projectPath, out var version) || string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException($"Unable to resolve Version/VersionPrefix from '{projectPath}'.");

            var outputName = string.IsNullOrWhiteSpace(target.OutputName) ? name : target.OutputName.Trim();
            var commandAlias = string.IsNullOrWhiteSpace(target.CommandAlias) ? null : target.CommandAlias!.Trim();

            var frameworks = overrideFrameworks.Length > 0
                ? overrideFrameworks
                : NormalizeStrings(target.Frameworks);
            if (frameworks.Length == 0)
                throw new ArgumentException($"Tools target '{name}' requires at least one framework.", nameof(spec));

            var runtimes = overrideRuntimes.Length > 0
                ? overrideRuntimes
                : NormalizeStrings(target.Runtimes);
            if (runtimes.Length == 0)
                throw new ArgumentException($"Tools target '{name}' requires at least one runtime.", nameof(spec));

            var flavors = overrideFlavors.Length > 0
                ? overrideFlavors
                : NormalizeFlavors(target.Flavors);
            if (flavors.Length == 0)
                flavors = new[] { target.Flavor };

            var artifactRoot = string.IsNullOrWhiteSpace(target.ArtifactRootPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", outputName))
                : ResolvePath(projectRoot, target.ArtifactRootPath!);

            var msbuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in target.MsBuildProperties ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                msbuildProperties[kv.Key.Trim()] = kv.Value ?? string.Empty;
            }

            var combinations = new List<PowerForgeToolReleaseCombinationPlan>();
            foreach (var framework in frameworks)
            {
                foreach (var runtime in runtimes)
                {
                    foreach (var flavor in flavors)
                    {
                        var tokens = BuildTokens(name, outputName, version, runtime, framework, flavor, configuration);
                        var defaultOutput = Path.Combine(artifactRoot, "{rid}", "{framework}", "{flavor}");
                        var outputTemplate = string.IsNullOrWhiteSpace(target.OutputPath)
                            ? defaultOutput
                            : target.OutputPath!;

                        var outputPath = ResolvePath(projectRoot, ApplyTemplate(outputTemplate, tokens));
                        var zipPath = ResolveZipPath(projectRoot, target, outputPath, tokens);

                        combinations.Add(new PowerForgeToolReleaseCombinationPlan
                        {
                            Runtime = runtime,
                            Framework = framework,
                            Flavor = flavor,
                            OutputPath = outputPath,
                            ZipPath = target.Zip ? zipPath : null
                        });
                    }
                }
            }

            plans.Add(new PowerForgeToolReleaseTargetPlan
            {
                Name = name,
                ProjectPath = projectPath,
                OutputName = outputName,
                CommandAlias = commandAlias,
                Version = version,
                ArtifactRootPath = artifactRoot,
                UseStaging = target.UseStaging,
                ClearOutput = target.ClearOutput,
                Zip = target.Zip,
                KeepSymbols = target.KeepSymbols,
                KeepDocs = target.KeepDocs,
                CreateCommandAliasOnUnix = target.CreateCommandAliasOnUnix,
                MsBuildProperties = msbuildProperties,
                Combinations = combinations
                    .OrderBy(c => c.Framework, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Runtime, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Flavor.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        }

        if (selectedTargets.Length > 0)
        {
            var missing = selectedTargets
                .Where(selected => plans.All(plan => !string.Equals(plan.Name, selected, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (missing.Length > 0)
                throw new ArgumentException($"Unknown tool target(s): {string.Join(", ", missing)}", nameof(request));
        }

        if (plans.Count == 0)
            throw new InvalidOperationException("No tool release targets were selected.");

        return new PowerForgeToolReleasePlan
        {
            ProjectRoot = projectRoot,
            Configuration = configuration,
            Targets = plans.ToArray()
        };
    }

    /// <summary>
    /// Executes the planned tool releases.
    /// </summary>
    public PowerForgeToolReleaseResult Run(PowerForgeToolReleasePlan plan)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        var artefacts = new List<PowerForgeToolReleaseArtifactResult>();
        var manifests = new List<string>();

        try
        {
            foreach (var target in plan.Targets ?? Array.Empty<PowerForgeToolReleaseTargetPlan>())
            {
                var targetArtefacts = new List<PowerForgeToolReleaseArtifactResult>();
                foreach (var combination in target.Combinations ?? Array.Empty<PowerForgeToolReleaseCombinationPlan>())
                {
                    targetArtefacts.Add(PublishOne(plan, target, combination));
                }

                artefacts.AddRange(targetArtefacts);
                manifests.Add(WriteManifest(target, targetArtefacts));
            }

            return new PowerForgeToolReleaseResult
            {
                Success = true,
                Artefacts = artefacts.ToArray(),
                ManifestPaths = manifests.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            if (_logger.IsVerbose)
                _logger.Verbose(ex.ToString());

            return new PowerForgeToolReleaseResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Artefacts = artefacts.ToArray(),
                ManifestPaths = manifests.ToArray()
            };
        }
    }

    private PowerForgeToolReleaseArtifactResult PublishOne(
        PowerForgeToolReleasePlan plan,
        PowerForgeToolReleaseTargetPlan target,
        PowerForgeToolReleaseCombinationPlan combination)
    {
        Directory.CreateDirectory(target.ArtifactRootPath);

        var publishDir = combination.OutputPath;
        string? stagingDir = null;
        if (target.UseStaging)
        {
            stagingDir = Path.Combine(Path.GetTempPath(), $"PowerForge.ToolRelease.{Guid.NewGuid():N}");
            publishDir = stagingDir;
            Directory.CreateDirectory(publishDir);
        }

        try
        {
            if (target.ClearOutput && !target.UseStaging)
                ClearDirectory(combination.OutputPath);

            Directory.CreateDirectory(publishDir);
            ExecutePublish(plan, target, combination, publishDir);
            ApplyCleanup(publishDir, target);
            var executablePath = RenameMainExecutable(target, publishDir, combination.Runtime);

            if (target.ClearOutput && target.UseStaging)
                ClearDirectory(combination.OutputPath);

            if (target.UseStaging)
                CopyDirectoryContents(publishDir, combination.OutputPath);

            var finalExecutablePath = target.UseStaging
                ? Path.Combine(combination.OutputPath, Path.GetFileName(executablePath))
                : executablePath;

            string? aliasPath = null;
            if (!combination.Runtime.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
                && target.CreateCommandAliasOnUnix
                && !string.IsNullOrWhiteSpace(target.CommandAlias))
            {
                aliasPath = Path.Combine(combination.OutputPath, target.CommandAlias!);
                if (!string.Equals(aliasPath, finalExecutablePath, StringComparison.OrdinalIgnoreCase))
                    File.Copy(finalExecutablePath, aliasPath, overwrite: true);
            }

            string? zipPath = null;
            if (!string.IsNullOrWhiteSpace(combination.ZipPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(combination.ZipPath!)!);
                if (File.Exists(combination.ZipPath!))
                    File.Delete(combination.ZipPath!);
                ZipFile.CreateFromDirectory(combination.OutputPath, combination.ZipPath!);
                zipPath = combination.ZipPath;
            }

            var (files, totalBytes) = SummarizeDirectory(combination.OutputPath);
            return new PowerForgeToolReleaseArtifactResult
            {
                Target = target.Name,
                Version = target.Version,
                OutputName = target.OutputName,
                Runtime = combination.Runtime,
                Framework = combination.Framework,
                Flavor = combination.Flavor,
                OutputPath = combination.OutputPath,
                ExecutablePath = finalExecutablePath,
                CommandAliasPath = aliasPath,
                ZipPath = zipPath,
                Files = files,
                TotalBytes = totalBytes
            };
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(stagingDir) && Directory.Exists(stagingDir))
            {
                try
                {
                    Directory.Delete(stagingDir, recursive: true);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private void ExecutePublish(
        PowerForgeToolReleasePlan plan,
        PowerForgeToolReleaseTargetPlan target,
        PowerForgeToolReleaseCombinationPlan combination,
        string publishDir)
    {
        var projectName = Path.GetFileNameWithoutExtension(target.ProjectPath) ?? target.Name;
        _logger.Info($"Publishing {target.Name} {target.Version} ({combination.Framework}, {combination.Runtime}, {combination.Flavor})");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(target.ProjectPath) ?? plan.ProjectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        var args = new List<string>
        {
            "publish",
            Quote(target.ProjectPath),
            "-c",
            Quote(plan.Configuration),
            "-f",
            Quote(combination.Framework),
            "-r",
            Quote(combination.Runtime)
        };

        var (selfContained, singleFile, compress, selfExtract) = ResolveFlavor(combination.Flavor);
        args.Add($"--self-contained:{selfContained.ToString().ToLowerInvariant()}");
        args.Add($"/p:PublishSingleFile={singleFile.ToString().ToLowerInvariant()}");
        args.Add("/p:PublishReadyToRun=false");
        args.Add("/p:PublishTrimmed=false");
        args.Add($"/p:IncludeAllContentForSelfExtract={selfExtract.ToString().ToLowerInvariant()}");
        args.Add($"/p:IncludeNativeLibrariesForSelfExtract={selfExtract.ToString().ToLowerInvariant()}");
        args.Add($"/p:EnableCompressionInSingleFile={compress.ToString().ToLowerInvariant()}");
        args.Add("/p:EnableSingleFileAnalyzer=false");
        args.Add("/p:DebugType=None");
        args.Add("/p:DebugSymbols=false");
        args.Add("/p:GenerateDocumentationFile=false");
        args.Add("/p:CopyDocumentationFiles=false");
        args.Add("/p:ExcludeSymbolsFromSingleFile=true");
        args.Add("/p:ErrorOnDuplicatePublishOutputFiles=false");
        args.Add("/p:UseAppHost=true");
        args.Add($"/p:PublishDir={Quote(publishDir)}");

        foreach (var kv in target.MsBuildProperties)
            args.Add($"/p:{kv.Key}={kv.Value}");

        psi.Arguments = string.Join(" ", args);

        var processResult = _runProcess(psi);
        if (processResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish failed for '{target.Name}' ({projectName}, {combination.Runtime}, {combination.Framework}, {combination.Flavor}). " +
                $"{TrimForMessage(processResult.StdErr, processResult.StdOut)}");
        }
    }

    private void ApplyCleanup(string publishDir, PowerForgeToolReleaseTargetPlan target)
    {
        if (!target.KeepSymbols)
        {
            foreach (var file in Directory.EnumerateFiles(publishDir, "*.pdb", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { }
            }
        }

        if (!target.KeepDocs)
        {
            foreach (var file in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                try { File.Delete(file); } catch { }
            }
        }
    }

    private static string RenameMainExecutable(
        PowerForgeToolReleaseTargetPlan target,
        string publishDir,
        string runtime)
    {
        var isWindows = runtime.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        var candidateName = Path.GetFileNameWithoutExtension(target.ProjectPath) ?? target.Name;
        var sourceName = isWindows ? $"{candidateName}.exe" : candidateName;
        var sourcePath = Path.Combine(publishDir, sourceName);
        if (!File.Exists(sourcePath))
        {
            sourcePath = FindLargestCandidate(publishDir, isWindows)
                ?? throw new FileNotFoundException($"Main executable not found in publish output: {publishDir}");
        }

        var desiredName = isWindows ? $"{target.OutputName}.exe" : target.OutputName;
        var desiredPath = Path.Combine(publishDir, desiredName);
        if (!string.Equals(sourcePath, desiredPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(desiredPath))
                File.Delete(desiredPath);
            File.Move(sourcePath, desiredPath);
        }

        return desiredPath;
    }

    private static string? FindLargestCandidate(string publishDir, bool isWindows)
    {
        var files = Directory.EnumerateFiles(publishDir, "*", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => isWindows
                ? string.Equals(file.Extension, ".exe", StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrWhiteSpace(file.Extension))
            .OrderByDescending(file => file.Length)
            .ToArray();

        return files.FirstOrDefault()?.FullName;
    }

    private static (bool SelfContained, bool SingleFile, bool Compress, bool SelfExtract) ResolveFlavor(PowerForgeToolReleaseFlavor flavor)
        => flavor switch
        {
            PowerForgeToolReleaseFlavor.SingleContained => (true, true, true, true),
            PowerForgeToolReleaseFlavor.SingleFx => (false, true, true, false),
            PowerForgeToolReleaseFlavor.Portable => (true, false, false, false),
            PowerForgeToolReleaseFlavor.Fx => (false, false, false, false),
            _ => throw new ArgumentOutOfRangeException(nameof(flavor), flavor, "Unsupported tool release flavor.")
        };

    private static string WriteManifest(PowerForgeToolReleaseTargetPlan target, IReadOnlyList<PowerForgeToolReleaseArtifactResult> artefacts)
    {
        var manifestPath = Path.Combine(target.ArtifactRootPath, "release-manifest.json");
        Directory.CreateDirectory(target.ArtifactRootPath);

        var manifest = new PowerForgeToolReleaseManifest
        {
            Target = target.Name,
            Version = target.Version,
            OutputName = target.OutputName,
            Artefacts = artefacts.ToArray()
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        return manifestPath;
    }

    private static string ResolveZipPath(
        string projectRoot,
        PowerForgeToolReleaseTarget target,
        string outputPath,
        IReadOnlyDictionary<string, string> tokens)
    {
        if (!target.Zip)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(target.ZipPath))
            return ResolvePath(projectRoot, ApplyTemplate(target.ZipPath!, tokens));

        var zipNameTemplate = string.IsNullOrWhiteSpace(target.ZipNameTemplate)
            ? "{outputName}-{version}-{framework}-{rid}-{flavor}.zip"
            : target.ZipNameTemplate!;
        var zipName = ApplyTemplate(zipNameTemplate, tokens);
        if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            zipName += ".zip";

        return Path.Combine(Path.GetDirectoryName(outputPath)!, zipName);
    }

    private static Dictionary<string, string> BuildTokens(
        string target,
        string outputName,
        string version,
        string runtime,
        string framework,
        PowerForgeToolReleaseFlavor flavor,
        string configuration)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = target,
            ["outputName"] = outputName,
            ["version"] = version,
            ["rid"] = runtime,
            ["runtime"] = runtime,
            ["framework"] = framework,
            ["flavor"] = flavor.ToString(),
            ["configuration"] = configuration
        };
    }

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var value = template ?? string.Empty;
        foreach (var kv in tokens)
            value = value.Replace("{" + kv.Key + "}", kv.Value ?? string.Empty);
        return value;
    }

    private static string[] NormalizeStrings(IEnumerable<string>? values)
        => (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static PowerForgeToolReleaseFlavor[] NormalizeFlavors(IEnumerable<PowerForgeToolReleaseFlavor>? values)
        => (values ?? Array.Empty<PowerForgeToolReleaseFlavor>())
            .Distinct()
            .ToArray();

    private static string ResolvePath(string basePath, string value)
    {
        var trimmed = (value ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Path value is required.", nameof(value));

        return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(basePath, trimmed));
    }

    private static void ClearDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            try
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: true);
                else
                    File.Delete(entry);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var targetPath = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static (int Files, long TotalBytes) SummarizeDirectory(string path)
    {
        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file))
            .ToArray();

        long total = 0;
        foreach (var file in files)
            total += file.Length;

        return (files.Length, total);
    }

    private static ProcessExecutionResult RunProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
            return new ProcessExecutionResult(1, string.Empty, "Failed to start process.");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessExecutionResult(process.ExitCode, stdOut, stdErr);
    }

    private static string TrimForMessage(string? stdErr, string? stdOut)
    {
        var combined = string.Join(
            Environment.NewLine,
            new[] { stdErr?.Trim(), stdOut?.Trim() }.Where(text => !string.IsNullOrWhiteSpace(text)));
        if (combined.Length <= 3000)
            return combined;

        return combined.Substring(0, 3000) + "...";
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "\"\"";

        return value.Contains(" ", StringComparison.Ordinal) || value.Contains("\"", StringComparison.Ordinal)
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }

    internal struct ProcessExecutionResult
    {
        public ProcessExecutionResult(int exitCode, string stdOut, string stdErr)
        {
            ExitCode = exitCode;
            StdOut = stdOut ?? string.Empty;
            StdErr = stdErr ?? string.Empty;
        }

        public int ExitCode { get; }

        public string StdOut { get; }

        public string StdErr { get; }
    }
}

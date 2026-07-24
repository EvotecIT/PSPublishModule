using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Runs 'dotnet publish' for given frameworks and returns publish directories.
/// </summary>
public sealed class DotnetPublisher
{
    // Microsoft.Common targets append this list to RemoveProperties for every project-reference traversal.
    private const string ProjectReferenceVersionProperties = "%3BVersion%3BAssemblyVersion%3BFileVersion";
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new publisher that will log build output to the provided <paramref name="logger"/>.
    /// </summary>
    public DotnetPublisher(ILogger logger) => _logger = logger;

    /// <summary>
    /// Runs <c>dotnet publish</c> for each target framework and returns a map of TFM to publish directory.
    /// </summary>
    /// <param name="projectPath">Path to the SDK-style project file to publish.</param>
    /// <param name="configuration">Build configuration (e.g., Release).</param>
    /// <param name="frameworks">Target frameworks to publish (e.g., net472, net8.0, net10.0).</param>
    /// <param name="version">Version to stamp into the published assemblies.</param>
    /// <returns>Dictionary mapping TFM to the publish output directory.</returns>
    public IReadOnlyDictionary<string, string> Publish(string projectPath, string configuration, IEnumerable<string> frameworks, string version)
        => Publish(projectPath, configuration, frameworks, version, artifactsRoot: null);

    /// <summary>
    /// Runs <c>dotnet publish</c> for each target framework and returns a map of TFM to publish directory.
    /// When <paramref name="artifactsRoot"/> is provided, build outputs (bin/obj) are redirected there to avoid file locking issues.
    /// </summary>
    /// <param name="projectPath">Path to the SDK-style project file to publish.</param>
    /// <param name="configuration">Build configuration (e.g., Release).</param>
    /// <param name="frameworks">Target frameworks to publish (e.g., net472, net8.0, net10.0).</param>
    /// <param name="version">Version to stamp into the published assemblies.</param>
    /// <param name="artifactsRoot">Optional root folder for build artifacts (bin/obj/publish).</param>
    /// <returns>Dictionary mapping TFM to the publish output directory.</returns>
    public IReadOnlyDictionary<string, string> Publish(
        string projectPath,
        string configuration,
        IEnumerable<string> frameworks,
        string version,
        string? artifactsRoot)
        => Publish(projectPath, configuration, frameworks, version, artifactsRoot, restoreSources: null);

    /// <summary>
    /// Runs <c>dotnet publish</c> for each target framework and returns a map of TFM to publish directory.
    /// When <paramref name="artifactsRoot"/> is provided, build outputs (bin/obj/publish) are redirected there to avoid file locking issues.
    /// </summary>
    /// <param name="projectPath">Path to the SDK-style project file to publish.</param>
    /// <param name="configuration">Build configuration (e.g., Release).</param>
    /// <param name="frameworks">Target frameworks to publish (e.g., net472, net8.0, net10.0).</param>
    /// <param name="version">Version to stamp into the published assemblies.</param>
    /// <param name="artifactsRoot">Optional root folder for build artifacts (bin/obj/publish).</param>
    /// <param name="restoreSources">Additional NuGet restore sources appended to normal project restore sources.</param>
    /// <returns>Dictionary mapping TFM to the publish output directory.</returns>
    public IReadOnlyDictionary<string, string> Publish(
        string projectPath,
        string configuration,
        IEnumerable<string> frameworks,
        string version,
        string? artifactsRoot,
        IEnumerable<string>? restoreSources)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        projectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projDir = Path.GetDirectoryName(projectPath)!;

        var useIsolatedArtifacts = !string.IsNullOrWhiteSpace(artifactsRoot);
        string? artifacts = null;
        if (useIsolatedArtifacts)
        {
            artifacts = Path.GetFullPath(artifactsRoot!.Trim().Trim('"'));
            Directory.CreateDirectory(artifacts);
            Directory.CreateDirectory(Path.Combine(artifacts, "publish"));
        }

        var requestedFrameworks = (frameworks ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (!isWindows)
        {
            var skipped = requestedFrameworks.Where(IsNetFrameworkTfm).ToArray();
            if (skipped.Length > 0)
            {
                foreach (var tfm in skipped)
                    _logger.Warn($"Skipping '{tfm}' publish on non-Windows (classic .NET Framework targets require Windows).");

                requestedFrameworks.RemoveAll(IsNetFrameworkTfm);
            }
        }

        if (requestedFrameworks.Count == 0)
            throw new InvalidOperationException("No supported frameworks were provided for dotnet publish.");

        var maxCpuCountArgument = isWindows ? "/m:1" : "-m:1";
        foreach (var tfm in requestedFrameworks)
        {
            var publishDir = useIsolatedArtifacts
                ? Path.Combine(artifacts!, "publish", tfm)
                : Path.Combine(projDir, "bin", configuration, tfm, "publish");
            var existingPathMap = useIsolatedArtifacts
                ? ReadProjectGraphPathMap(projectPath, configuration, tfm, artifacts!)
                : null;
            var args = BuildPublishArguments(
                projectPath,
                version,
                configuration,
                tfm,
                useIsolatedArtifacts,
                artifacts,
                maxCpuCountArgument,
                publishDir,
                restoreSources,
                existingPathMap);
            RunDotnet(projDir, args, tfm, "publish");

            if (!Directory.Exists(publishDir))
                throw new DirectoryNotFoundException($"Publish directory not found: {publishDir}");

            result[tfm] = publishDir;
        }

        return result;
    }

    internal static IReadOnlyList<string> BuildPublishArguments(
        string projectPath,
        string version,
        string configuration,
        string tfm,
        bool useIsolatedArtifacts,
        string? artifacts,
        string maxCpuCountArgument,
        string publishDir,
        IEnumerable<string>? restoreSources,
        string? existingPathMap)
    {
        var args = new List<string>
        {
            "publish",
            projectPath,
            "--configuration", configuration,
            "-nologo",
            "--verbosity", "minimal",
            $"-p:Version={version}",
            $"-p:AssemblyVersion={version}",
            $"-p:FileVersion={version}",
            $"-p:_GlobalPropertiesToRemoveFromProjectReferences={ProjectReferenceVersionProperties}",
            "--framework", tfm
        };

        AppendSharedBuildArguments(
            args,
            useIsolatedArtifacts,
            artifacts,
            maxCpuCountArgument,
            restoreSources,
            existingPathMap);
        if (useIsolatedArtifacts)
        {
            args.Add("--output");
            args.Add(publishDir);
        }

        return args;
    }

    private static void AppendSharedBuildArguments(
        List<string> args,
        bool useIsolatedArtifacts,
        string? artifacts,
        string maxCpuCountArgument,
        IEnumerable<string>? restoreSources,
        string? existingPathMap)
    {
        var normalizedSources = NormalizeRestoreSources(restoreSources);
        if (normalizedSources.Length > 0)
            args.Add($"-p:RestoreAdditionalProjectSources={string.Join(";", normalizedSources)}");

        if (useIsolatedArtifacts)
        {
            args.Add("-p:UseArtifactsOutput=true");
            args.Add($"-p:ArtifactsPath={artifacts}");
            args.Add("-p:ContinuousIntegrationBuild=true");
            var artifactPathMap = $"{artifacts}=/_/PowerForge/artifacts";
            var pathMap = string.IsNullOrWhiteSpace(existingPathMap)
                ? artifactPathMap
                : $"{existingPathMap!.Trim().TrimEnd(',')},{artifactPathMap}";
            args.Add($"-p:PathMap={pathMap.Replace(",", "%2C")}");
            // Centralized artifacts output can make parallel project-reference builds race on generated files.
            // Serializing MSBuild trades speed for deterministic module binary publishes.
            args.Add(maxCpuCountArgument);
        }
    }

    private string ReadProjectGraphPathMap(
        string projectPath,
        string configuration,
        string tfm,
        string artifacts)
    {
        var projectQueue = new Queue<ProjectEvaluationRequest>();
        var visitedEvaluations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pathMaps = new List<string>();
        var seenPathMaps = new HashSet<string>(StringComparer.Ordinal);
        projectQueue.Enqueue(new ProjectEvaluationRequest(Path.GetFullPath(projectPath), tfm));

        while (projectQueue.Count > 0)
        {
            var request = projectQueue.Dequeue();
            var evaluationKey = $"{request.ProjectPath}|{request.TargetFramework ?? string.Empty}";
            if (!visitedEvaluations.Add(evaluationKey))
                continue;

            var evaluation = ReadProjectBuildContext(
                request.ProjectPath,
                configuration,
                request.TargetFramework,
                artifacts);
            var pathMap = evaluation.PathMap.Trim().TrimEnd(',');
            if (!string.IsNullOrWhiteSpace(pathMap) && seenPathMaps.Add(pathMap))
                pathMaps.Add(pathMap);

            if (request.TargetFramework == null)
            {
                foreach (var targetFramework in evaluation.TargetFrameworks)
                {
                    projectQueue.Enqueue(new ProjectEvaluationRequest(
                        request.ProjectPath,
                        targetFramework));
                }
            }

            foreach (var projectReference in evaluation.ProjectReferences)
            {
                // Referenced projects choose their own target framework. Evaluate their
                // project-declared framework(s) instead of forcing the entry project's TFM.
                projectQueue.Enqueue(new ProjectEvaluationRequest(projectReference, null));
            }
        }

        return string.Join(",", pathMaps);
    }

    private ProjectBuildContext ReadProjectBuildContext(
        string projectPath,
        string configuration,
        string? tfm,
        string artifacts)
    {
        var args = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:PathMap",
            "-getProperty:TargetFrameworks",
            "-getItem:ProjectReference",
            $"-p:Configuration={configuration}",
            "-p:UseArtifactsOutput=true",
            $"-p:ArtifactsPath={artifacts}",
            "-p:ContinuousIntegrationBuild=true"
        };
        if (!string.IsNullOrWhiteSpace(tfm))
            args.Add($"-p:TargetFramework={tfm}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            var jsonStart = stdout.IndexOf('{');
            var jsonEnd = stdout.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                throw new InvalidOperationException(
                    $"dotnet msbuild returned an invalid PathMap/project-reference evaluation for {projectPath}.");
            }

            using var document = JsonDocument.Parse(
                stdout.Substring(jsonStart, jsonEnd - jsonStart + 1));
            var root = document.RootElement;
            var pathMap = root
                .GetProperty("Properties")
                .GetProperty("PathMap")
                .GetString() ?? string.Empty;
            var targetFrameworks = root
                .GetProperty("Properties")
                .GetProperty("TargetFrameworks")
                .GetString() ?? string.Empty;
            var projectReferences = new List<string>();
            if (root.GetProperty("Items").TryGetProperty("ProjectReference", out var references))
            {
                foreach (var reference in references.EnumerateArray())
                {
                    if (reference.TryGetProperty("FullPath", out var fullPath) &&
                        !string.IsNullOrWhiteSpace(fullPath.GetString()))
                    {
                        projectReferences.Add(Path.GetFullPath(fullPath.GetString()!));
                    }
                }
            }

            var declaredTargetFrameworks = string.IsNullOrWhiteSpace(targetFrameworks)
                ? Array.Empty<string>()
                : targetFrameworks
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(static framework => framework.Trim())
                    .Where(static framework => framework.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            return new ProjectBuildContext(pathMap, projectReferences, declaredTargetFrameworks);
        }

        _logger.Error(
            $"dotnet msbuild could not evaluate PathMap/project references for {projectPath} " +
            $"({tfm ?? "project-declared frameworks"}): " +
            $"{stderr}\n{stdout}");
        throw new InvalidOperationException(
            $"dotnet msbuild could not evaluate PathMap/project references for {projectPath} " +
            $"({tfm ?? "project-declared frameworks"}, exit {process.ExitCode})");
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(string projectPath, string? targetFramework)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
        }

        internal string ProjectPath { get; }

        internal string? TargetFramework { get; }
    }

    private sealed class ProjectBuildContext
    {
        internal ProjectBuildContext(
            string pathMap,
            IReadOnlyList<string> projectReferences,
            IReadOnlyList<string> targetFrameworks)
        {
            PathMap = pathMap;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
        }

        internal string PathMap { get; }

        internal IReadOnlyList<string> ProjectReferences { get; }

        internal IReadOnlyList<string> TargetFrameworks { get; }
    }

    private void RunDotnet(string workingDirectory, IReadOnlyList<string> args, string tfm, string phase)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
            return;

        _logger.Error($"dotnet {phase} failed for {tfm}: {stderr}\n{stdout}");
        throw new InvalidOperationException($"dotnet {phase} failed for {tfm} (exit {process.ExitCode})");
    }

    private static string[] NormalizeRestoreSources(IEnumerable<string>? restoreSources)
        => (restoreSources ?? Array.Empty<string>())
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Select(static source => source.Trim().Trim('"'))
            .Where(static source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsNetFrameworkTfm(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm)) return false;
        var value = tfm.Trim();
        if (!value.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;

        // net472/net48/etc are classic .NET Framework TFMs and do not include a dot.
        var suffix = value.Substring(3);
        if (suffix.Length == 0 || suffix.Contains('.')) return false;
        if (!suffix.All(char.IsDigit)) return false;
        if (!int.TryParse(suffix, out var n)) return false;
        return n > 0 && n < 500;
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

        var sb = new System.Text.StringBuilder();
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

}

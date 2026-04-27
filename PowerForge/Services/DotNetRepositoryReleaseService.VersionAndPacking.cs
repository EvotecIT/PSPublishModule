using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private string ResolveVersion(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        DotNetRepositoryReleaseSpec spec,
        out string? warning)
    {
        warning = null;

        if (!spec.UpdateVersions)
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "UpdateVersions is disabled and no version tags were found in the project file.";
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "No expected version provided and no version tags were found in the project file.";
            return string.Empty;
        }

        if (Version.TryParse(expectedVersion, out var exact))
            return exact.ToString();

        var current = _resolver.ResolveLatest(
            packageId: string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId,
            sources: spec.VersionSources,
            credential: spec.VersionSourceCredential,
            includePrerelease: spec.IncludePrerelease);

        if (current is null)
            warning = $"No current package version found; using 0 baseline for '{expectedVersion}'.";

        return VersionPatternStepper.Step(expectedVersion!, current);
    }

    private static DotNetPackResult PackProject(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec, ILogger logger)
    {
        var result = new DotNetPackResult();

        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();

        string? outputPath = null;
        if (!string.IsNullOrWhiteSpace(spec.OutputPath))
        {
            outputPath = Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.GetFullPath(Path.Combine(spec.RootPath, spec.OutputPath));
            Directory.CreateDirectory(outputPath);
        }

        var packageRoot = outputPath ?? Path.Combine(csprojDir, "bin", configuration);
        var existingPackages = SnapshotPackages(packageRoot);

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, project.ProjectName, logger, out var stdErr, out var stdOut, out var duration);
        result.Duration = duration;
        if (exitCode != 0)
        {
            result.ErrorMessage = $"dotnet pack failed for {project.ProjectName} (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
            return result;
        }

        if (Directory.Exists(packageRoot))
        {
            var pkgs = Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                .ToArray();
            result.Packages.AddRange(pkgs);
        }

        result.Success = true;
        return result;
    }

    private static DotNetPackResult PackProjectsWithMsBuild(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger)
    {
        var result = new DotNetPackResult();
        if (projects.Count == 0)
        {
            result.Success = true;
            return result;
        }

        if (string.IsNullOrWhiteSpace(spec.OutputPath))
        {
            result.ErrorMessage = "MSBuild batch pack requires OutputPath.";
            return result;
        }

        var outputPath = Path.IsPathRooted(spec.OutputPath!)
            ? spec.OutputPath!
            : Path.GetFullPath(Path.Combine(spec.RootPath, spec.OutputPath!));
        Directory.CreateDirectory(outputPath);
        var existingPackages = SnapshotPackages(outputPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "project-build", $"pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var traversalPath = Path.Combine(tempRoot, "pack.proj");
        try
        {
            WritePackTraversalProject(traversalPath, projects, spec, outputPath);

            var exitCode = RunDotnetMsBuildPack(
                traversalPath,
                tempRoot,
                projects.Count,
                logger,
                out var stdErr,
                out var stdOut,
                out var duration);
            result.Duration = duration;

            if (exitCode != 0)
            {
                result.ErrorMessage = $"dotnet msbuild batch pack failed (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                return result;
            }

            var pkgs = Directory.EnumerateFiles(outputPath, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                .ToArray();
            result.Packages.AddRange(pkgs);
            result.Success = true;
            return result;
        }
        finally
        {
            TryDeleteDirectory(tempRoot, logger);
        }
    }

    internal static void WritePackTraversalProject(
        string traversalPath,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string outputPath)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var properties = $"Configuration={EscapeMsBuildPropertyValue(configuration)};PackageOutputPath={EscapeMsBuildPropertyValue(outputPath)}";

        var document = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    projects.Select(project => new XElement("PackProject",
                        new XAttribute("Include", Path.GetFullPath(project.CsprojPath))))),
                new XElement("Target",
                    new XAttribute("Name", "PackSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        // Restore is intentional so project references and package assets resolve inside the batch.
                        new XAttribute("Targets", "Restore;Pack"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("Properties", properties)))));

        document.Save(traversalPath);
    }

    private static string? ResolvePackagePath(DotNetRepositoryReleaseSpec spec, DotNetRepositoryProjectResult project, string version)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var outputPath = string.IsNullOrWhiteSpace(spec.OutputPath)
            ? Path.Combine(Path.GetDirectoryName(project.CsprojPath) ?? string.Empty, "bin", configuration)
            : (Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.Combine(spec.RootPath, spec.OutputPath));

        if (string.IsNullOrWhiteSpace(outputPath)) return null;
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        return Path.Combine(outputPath, $"{packageId}.{version}.nupkg");
    }

    private static int RunDotnetPack(
        string csproj,
        string workingDirectory,
        string configuration,
        string? outputPath,
        string projectName,
        ILogger logger,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string> { "pack", csproj, "--configuration", configuration };
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            args.Add("-o");
            args.Add(outputPath!);
        }
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath!);
        }
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet pack still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, "dotnet pack", stdOut, stdErr);
        return exitCode;
    }

    private static int RunDotnetMsBuildPack(
        string traversalProject,
        string workingDirectory,
        int projectCount,
        ILogger logger,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(new[]
        {
            "msbuild",
            traversalProject,
            "/t:PackSelected",
            "/m",
            "/nr:false",
            logger.IsVerbose ? "/v:n" : "/v:m"
        });
#else
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(traversalProject);
        psi.ArgumentList.Add("/t:PackSelected");
        psi.ArgumentList.Add("/m");
        psi.ArgumentList.Add("/nr:false");
        psi.ArgumentList.Add(logger.IsVerbose ? "/v:n" : "/v:m");
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"MSBuild batch pack still running ({projectCount} project(s), {FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, "MSBuild batch", "dotnet msbuild pack", stdOut, stdErr);
        return exitCode;
    }

    internal static PackagePushResult ClassifyNuGetPushOutcome(int exitCode, bool skipDuplicate, string stdErr, string stdOut)
    {
        var combined = string.Join(Environment.NewLine, stdErr, stdOut).Trim();

        if (exitCode != 0)
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = combined
            };
        }

        if (skipDuplicate && LooksLikeSkippedDuplicate(combined))
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.SkippedDuplicate,
                Message = combined
            };
        }

        return new PackagePushResult
        {
            Outcome = PackagePushOutcome.Published,
            Message = combined
        };
    }

    private static bool PushPackage(string packagePath, string apiKey, string source, bool skipDuplicate, out PackagePushResult result)
    {
        result = new PackagePushResult();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string>
        {
            "nuget", "push", packagePath,
            "--api-key", apiKey,
            "--source", source
        };
        if (skipDuplicate) args.Add("--skip-duplicate");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        if (skipDuplicate) psi.ArgumentList.Add("--skip-duplicate");
#endif

        using var p = Process.Start(psi);
        if (p is null)
        {
            result = new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = "Failed to start dotnet."
            };
            return false;
        }
        // Start both stream reads before waiting to avoid pipe-buffer deadlocks.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var stdOut = stdoutTask.GetAwaiter().GetResult();
        var stdErr = stderrTask.GetAwaiter().GetResult();
        result = ClassifyNuGetPushOutcome(p.ExitCode, skipDuplicate, stdErr, stdOut);
        return result.Outcome != PackagePushOutcome.Failed;
    }

    private static void LogProcessOutput(ILogger logger, string projectName, string operation, string stdOut, string stdErr)
    {
        if (!logger.IsVerbose)
            return;

        if (!string.IsNullOrWhiteSpace(stdOut))
            logger.Verbose($"{projectName}: {operation} stdout:{Environment.NewLine}{stdOut.TrimEnd()}");
        if (!string.IsNullOrWhiteSpace(stdErr))
            logger.Verbose($"{projectName}: {operation} stderr:{Environment.NewLine}{stdErr.TrimEnd()}");
    }

    private static string SummarizeProcessFailureOutput(string stdErr, string stdOut)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(stdErr))
            sections.Add("stderr:" + Environment.NewLine + SummarizeProcessOutputLines(stdErr));
        if (!string.IsNullOrWhiteSpace(stdOut))
            sections.Add("stdout:" + Environment.NewLine + SummarizeProcessOutputLines(stdOut));

        return sections.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, sections);
    }

    private static string SummarizeProcessOutputLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        const int maxLines = 40;
        return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
    }

    private static int RunProcessWithHeartbeat(
        ProcessStartInfo psi,
        ILogger logger,
        Func<TimeSpan, string> heartbeatMessage,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        using var p = Process.Start(psi);
        if (p is null) return 1;

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stopwatch = Stopwatch.StartNew();
        var nextProgress = TimeSpan.FromSeconds(15);

        while (!p.WaitForExit(1000))
        {
            if (stopwatch.Elapsed < nextProgress)
                continue;

            logger.Info(heartbeatMessage(stopwatch.Elapsed));
            nextProgress += TimeSpan.FromSeconds(15);
        }

        // On .NET Framework, WaitForExit(int) returning true does not guarantee async
        // output callbacks have completed; the no-arg overload does.
        p.WaitForExit();
        stdOut = stdoutTask.GetAwaiter().GetResult();
        stdErr = stderrTask.GetAwaiter().GetResult();
        stopwatch.Stop();
        duration = stopwatch.Elapsed;
        return p.ExitCode;
    }

    internal static Dictionary<string, (DateTime LastWriteUtc, long Length)> SnapshotPackages(string packageRoot)
    {
        var snapshot = new Dictionary<string, (DateTime LastWriteUtc, long Length)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(packageRoot))
            return snapshot;

        foreach (var path in Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(path);
            snapshot[path] = (info.LastWriteTimeUtc, info.Length);
        }

        return snapshot;
    }

    internal static bool WasPackageCreatedOrChanged(
        IReadOnlyDictionary<string, (DateTime LastWriteUtc, long Length)> existingPackages,
        string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return false;

        if (!existingPackages.TryGetValue(path, out var existing))
            return true;

        return existing.LastWriteUtc != info.LastWriteTimeUtc || existing.Length != info.Length;
    }

    private static string EscapeMsBuildPropertyValue(string value)
        => value.Replace("%", "%25")
            .Replace(";", "%3B")
            .Replace("=", "%3D")
            .Replace("$", "%24")
            .Replace("@", "%40");

    private static bool LooksLikeSkippedDuplicate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("409 (Conflict)", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("cannot be modified", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("skip duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal enum PackagePushOutcome
    {
        Published,
        SkippedDuplicate,
        Failed
    }

    internal sealed class PackagePushResult
    {
        public PackagePushOutcome Outcome { get; set; }
        public string? Message { get; set; }
    }

}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static int RunDotnetMsBuildTarget(
        string traversalProject,
        string workingDirectory,
        string targetName,
        string heartbeatOperation,
        string logOperation,
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
            $"/t:{targetName}",
            "/m",
            "/nr:false",
            logger.IsVerbose ? "/v:n" : "/v:m"
        });
#else
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(traversalProject);
        psi.ArgumentList.Add($"/t:{targetName}");
        psi.ArgumentList.Add("/m");
        psi.ArgumentList.Add("/nr:false");
        psi.ArgumentList.Add(logger.IsVerbose ? "/v:n" : "/v:m");
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"MSBuild batch {heartbeatOperation} still running ({projectCount} project(s), {FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, "MSBuild batch", logOperation, stdOut, stdErr);
        return exitCode;
    }

    private static int RunDotnetMsBuildGetProperty(
        string csproj,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        string propertyName,
        string projectName,
        ILogger logger,
        out string? value,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
        => RunDotnetMsBuildGetProperty(
            csproj,
            workingDirectory,
            configuration,
            targetFramework,
            runtimeIdentifier: null,
            propertyName,
            projectName,
            logger,
            out value,
            out stdErr,
            out stdOut,
            out duration);

    private static int RunDotnetMsBuildGetProperty(
        string csproj,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        string? runtimeIdentifier,
        string propertyName,
        string projectName,
        ILogger logger,
        out string? value,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        value = null;
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

        var args = new List<string>
        {
            "msbuild",
            csproj,
            "-nologo",
            $"-getProperty:{propertyName}",
            $"-p:Configuration={configuration}"
        };
        if (!string.IsNullOrWhiteSpace(targetFramework))
            args.Add($"-p:TargetFramework={targetFramework!.Trim()}");
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
            args.Add($"-p:RuntimeIdentifier={runtimeIdentifier!.Trim()}");

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet msbuild {propertyName} still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, $"dotnet msbuild {propertyName}", stdOut, stdErr);
        value = ExtractMsBuildPropertyValue(stdOut, propertyName);
        return exitCode;
    }

    private static string? ExtractMsBuildPropertyValue(string stdOut, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(stdOut))
            return null;

        var lines = stdOut.Replace("\r\n", "\n").Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
            return null;

        var xmlStart = Array.FindIndex(lines, static line => line.StartsWith("<", StringComparison.Ordinal));
        if (xmlStart >= 0)
        {
            try
            {
                var xml = string.Join(Environment.NewLine, lines.Skip(xmlStart));
                var document = XDocument.Parse(xml);
                return document.Descendants()
                    .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                    ?.Value;
            }
            catch
            {
                // Fall back to the last non-empty line for older MSBuild output shapes.
            }
        }

        return lines[lines.Length - 1];
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

    internal static IReadOnlyDictionary<string, PackagePushOutcome> ClassifyPublishedArtifacts(
        IReadOnlyList<string> artifacts,
        PackagePushResult pushResult)
    {
        var outcomes = artifacts.ToDictionary(
            artifact => artifact,
            _ => pushResult.Outcome,
            StringComparer.OrdinalIgnoreCase);
        if (pushResult.Outcome == PackagePushOutcome.Published || artifacts.Count <= 1)
            return outcomes;

        string? currentArtifact = null;
        var lines = (pushResult.Message ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            foreach (var artifact in artifacts)
            {
                var fileName = Path.GetFileName(artifact);
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    line.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    currentArtifact = artifact;
                    break;
                }
            }

            if (currentArtifact is not null && LooksLikeSkippedDuplicate(line))
                outcomes[currentArtifact] = PackagePushOutcome.SkippedDuplicate;
            else if (currentArtifact is not null && LooksLikePublished(line))
                outcomes[currentArtifact] = PackagePushOutcome.Published;
        }

        return outcomes;
    }

    private static bool PushPackage(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate,
        bool suppressCompanionSymbols,
        out PackagePushResult result)
    {
        if (IsLocalPublishSource(source) &&
            packagePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            return CopySymbolPackageToLocalFeed(packagePath, source, skipDuplicate, out result);
        }

        result = new PackagePushResult();
        var psi = CreateNuGetPushStartInfo(packagePath, apiKey, source, skipDuplicate, suppressCompanionSymbols);

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

    /// <summary>
    /// Copies a symbol package beside its primary package because dotnet nuget push can exit successfully
    /// without copying a directly supplied .snupkg to a local or UNC source.
    /// </summary>
    private static bool CopySymbolPackageToLocalFeed(
        string packagePath,
        string source,
        bool skipDuplicate,
        out PackagePushResult result)
    {
        try
        {
            var localFeed = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile
                ? uri.LocalPath
                : source;
            localFeed = Path.GetFullPath(localFeed);
            if (!Directory.Exists(localFeed))
                throw new DirectoryNotFoundException($"Local NuGet feed not found: {localFeed}");

            var symbolFileName = Path.GetFileName(packagePath);
            var primaryFileName = Path.GetFileNameWithoutExtension(packagePath) + ".nupkg";
            var primaryPath = Directory
                .EnumerateFiles(localFeed, primaryFileName, SearchOption.AllDirectories)
                .OrderBy(path => string.Equals(
                    Path.GetDirectoryName(path),
                    localFeed,
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
            var destinationDirectory = primaryPath is null
                ? localFeed
                : Path.GetDirectoryName(primaryPath) ?? localFeed;
            var destinationPath = Path.Combine(destinationDirectory, symbolFileName);

            if (File.Exists(destinationPath))
            {
                if (!skipDuplicate)
                    throw new IOException($"Symbol package already exists in the local feed: {destinationPath}");

                result = new PackagePushResult
                {
                    Outcome = PackagePushOutcome.SkippedDuplicate,
                    Message = $"Symbol package already exists in the local feed: {destinationPath}"
                };
                return true;
            }

            File.Copy(packagePath, destinationPath, overwrite: false);
            result = new PackagePushResult
            {
                Outcome = PackagePushOutcome.Published,
                Message = $"Copied symbol package to local feed: {destinationPath}"
            };
            return true;
        }
        catch (Exception ex)
        {
            result = new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = ex.Message
            };
            return false;
        }
    }

    internal static ProcessStartInfo CreateNuGetPushStartInfo(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate,
        bool suppressCompanionSymbols = false)
    {
        var packageDirectory = Path.GetDirectoryName(Path.GetFullPath(packagePath));
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = string.IsNullOrWhiteSpace(packageDirectory) ? Environment.CurrentDirectory : packageDirectory,
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
        if (suppressCompanionSymbols) args.Add("--no-symbols");
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
        if (suppressCompanionSymbols) psi.ArgumentList.Add("--no-symbols");
#endif
        return psi;
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

    internal static string SummarizeProcessOutputLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        const int firstLines = 10;
        const int lastLines = 40;
        const int maxDiagnosticLines = 20;
        if (lines.Length <= firstLines + lastLines)
            return string.Join(Environment.NewLine, lines);

        var middle = lines.Skip(firstLines).Take(lines.Length - firstLines - lastLines).ToArray();
        var diagnostics = middle
            .Where(IsDiagnosticOutputLine)
            .Distinct(StringComparer.Ordinal)
            .Take(maxDiagnosticLines)
            .ToArray();

        var summary = new List<string>();
        summary.AddRange(lines.Take(firstLines));
        summary.Add($"... omitted {middle.Length} line(s); diagnostic lines from that range are shown below when detected ...");
        if (diagnostics.Length > 0)
        {
            summary.Add("diagnostic lines:");
            summary.AddRange(diagnostics);
        }

        summary.Add($"last {lastLines} line(s):");
        summary.AddRange(lines.Skip(lines.Length - lastLines));
        return string.Join(Environment.NewLine, summary);
    }

    private static bool IsDiagnosticOutputLine(string line)
    {
        return line.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(": failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("unable to", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("not found", StringComparison.OrdinalIgnoreCase);
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
        var nextProgress = HeartbeatInterval;

        while (!p.WaitForExit(1000))
        {
            if (stopwatch.Elapsed < nextProgress)
                continue;

            logger.Info(heartbeatMessage(stopwatch.Elapsed));
            nextProgress += HeartbeatInterval;
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

        foreach (var path in Directory.EnumerateFiles(packageRoot, "*.snupkg", SearchOption.AllDirectories))
        {
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
               text.IndexOf("cannot be modified", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikePublished(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("package was pushed", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("successfully pushed", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("successfully published", StringComparison.OrdinalIgnoreCase) >= 0;
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

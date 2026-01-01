using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Runs 'dotnet publish' for given frameworks and returns publish directories.
/// </summary>
public sealed class DotnetPublisher
{
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
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
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

        foreach (var tfm in requestedFrameworks)
        {
            var publishDir = useIsolatedArtifacts
                ? Path.Combine(artifacts!, "publish", tfm)
                : Path.Combine(projDir, "bin", configuration, tfm, "publish");

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = projDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

#if NET472
            var args = new List<string>
            {
                "publish",
                "--configuration", configuration,
                "-nologo",
                "--verbosity", "minimal",
                $"-p:Version={version}",
                $"-p:AssemblyVersion={version}",
                $"-p:FileVersion={version}",
                "--framework", tfm
            };

            if (useIsolatedArtifacts)
            {
                args.Add("-p:UseArtifactsOutput=true");
                args.Add($"-p:ArtifactsPath={artifacts}");
                args.Add("--output");
                args.Add(publishDir);
            }

            psi.Arguments = BuildWindowsArgumentString(args);
#else
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add("--configuration");
            psi.ArgumentList.Add(configuration);
            psi.ArgumentList.Add("-nologo");
            psi.ArgumentList.Add("--verbosity");
            psi.ArgumentList.Add("minimal");
            psi.ArgumentList.Add($"-p:Version={version}");
            psi.ArgumentList.Add($"-p:AssemblyVersion={version}");
            psi.ArgumentList.Add($"-p:FileVersion={version}");
            psi.ArgumentList.Add("--framework");
            psi.ArgumentList.Add(tfm);

            if (useIsolatedArtifacts)
            {
                psi.ArgumentList.Add("-p:UseArtifactsOutput=true");
                psi.ArgumentList.Add($"-p:ArtifactsPath={artifacts}");
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(publishDir);
            }
#endif

            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                _logger.Error($"dotnet publish failed for {tfm}: {stderr}\n{stdout}");
                throw new InvalidOperationException($"dotnet publish failed for {tfm} (exit {p.ExitCode})");
            }

            if (!Directory.Exists(publishDir))
                throw new DirectoryNotFoundException($"Publish directory not found: {publishDir}");

            result[tfm] = publishDir;
        }

        return result;
    }

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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
    /// <param name="frameworks">Target frameworks to publish (e.g., net472, net8.0).</param>
    /// <param name="version">Version to stamp into the published assemblies.</param>
    /// <returns>Dictionary mapping TFM to the publish output directory.</returns>
    public IReadOnlyDictionary<string, string> Publish(string projectPath, string configuration, IEnumerable<string> frameworks, string version)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var projDir = Path.GetDirectoryName(projectPath)!;
        foreach (var tfm in frameworks ?? Array.Empty<string>())
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish --configuration {configuration} -nologo --verbosity minimal -p:Version={version} -p:AssemblyVersion={version} -p:FileVersion={version} --framework {tfm}",
                WorkingDirectory = projDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                _logger.Error($"dotnet publish failed for {tfm}: {stderr}\n{stdout}");
                throw new InvalidOperationException($"dotnet publish failed for {tfm} (exit {p.ExitCode})");
            }

            var publishDir = Path.Combine(projDir, "bin", configuration, tfm, "publish");
            if (!Directory.Exists(publishDir))
            {
                throw new DirectoryNotFoundException($"Publish directory not found: {publishDir}");
            }
            result[tfm] = publishDir;
        }
        return result;
    }
}


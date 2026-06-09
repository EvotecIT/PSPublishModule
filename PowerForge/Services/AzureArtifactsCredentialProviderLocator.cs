using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;

namespace PowerForge;

/// <summary>
/// Result of Azure Artifacts credential-provider discovery.
/// </summary>
public sealed class AzureArtifactsCredentialProviderDetectionResult
{
    /// <summary>Whether any supported Azure Artifacts credential-provider installation was detected.</summary>
    public bool IsDetected { get; set; }

    /// <summary>Detected credential-provider file paths.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();

    /// <summary>Detected credential-provider version when available.</summary>
    public string? Version { get; set; }
}

/// <summary>
/// Detects Azure Artifacts credential-provider installations from standard NuGet plugin locations.
/// </summary>
public static class AzureArtifactsCredentialProviderLocator
{
    /// <summary>
    /// Detects credential-provider installations using the current process environment and default search locations.
    /// </summary>
    public static AzureArtifactsCredentialProviderDetectionResult Detect()
    {
        return Detect(
            static name => Environment.GetEnvironmentVariable(name),
            static folder => Environment.GetFolderPath(folder));
    }

    internal static AzureArtifactsCredentialProviderDetectionResult Detect(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getFolderPath)
    {
        if (getEnvironmentVariable is null) throw new ArgumentNullException(nameof(getEnvironmentVariable));
        if (getFolderPath is null) throw new ArgumentNullException(nameof(getFolderPath));

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDelimitedPaths(candidates, getEnvironmentVariable("NUGET_PLUGIN_PATHS"));

        var userProfile = getFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            candidates.Add(Path.Combine(userProfile, ".nuget", "plugins"));

        var programFiles = getEnvironmentVariable("ProgramFiles");
        AddVisualStudioNuGetRoots(candidates, programFiles);

        var programFilesX86 = getEnvironmentVariable("ProgramFiles(x86)");
        AddVisualStudioNuGetRoots(candidates, programFilesX86);

        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            try
            {
                if (File.Exists(candidate))
                {
                    if (IsCredentialProviderFile(candidate))
                        matches.Add(Path.GetFullPath(candidate));
                    continue;
                }

                if (!Directory.Exists(candidate))
                    continue;

                foreach (var file in Directory.EnumerateFiles(candidate, "CredentialProvider.Microsoft.*", SearchOption.AllDirectories))
                {
                    if (IsCredentialProviderFile(file))
                        matches.Add(Path.GetFullPath(file));
                }
            }
            catch (IOException)
            {
                // Best effort discovery only.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort discovery only.
            }
            catch (SecurityException)
            {
                // Best effort discovery only.
            }
        }

        var paths = matches
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AzureArtifactsCredentialProviderDetectionResult
        {
            IsDetected = paths.Length > 0,
            Paths = paths,
            Version = TryGetVersion(paths)
        };
    }

    private static string? TryGetVersion(IEnumerable<string>? paths)
    {
        foreach (var path in paths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                    return info.ProductVersion;
                if (!string.IsNullOrWhiteSpace(info.FileVersion))
                    return info.FileVersion;
            }
            catch (IOException)
            {
                // best effort only
            }
            catch (UnauthorizedAccessException)
            {
                // best effort only
            }
            catch (SecurityException)
            {
                // best effort only
            }
        }

        return null;
    }

    private static void AddDelimitedPaths(ISet<string> output, string? value)
    {
        if (output is null || string.IsNullOrWhiteSpace(value))
            return;

        var delimitedValue = value!;
        foreach (var item in delimitedValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = item.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(trimmed))
                output.Add(trimmed);
        }
    }

    private static void AddVisualStudioNuGetRoots(ISet<string> output, string? programFilesRoot)
    {
        if (output is null || string.IsNullOrWhiteSpace(programFilesRoot))
            return;

        var visualStudioRoot = Path.Combine(programFilesRoot, "Microsoft Visual Studio");
        if (!Directory.Exists(visualStudioRoot))
            return;

        try
        {
            foreach (var yearDirectory in Directory.EnumerateDirectories(visualStudioRoot))
            {
                foreach (var editionDirectory in Directory.EnumerateDirectories(yearDirectory))
                {
                    var pluginsRoot = Path.Combine(
                        editionDirectory,
                        "Common7",
                        "IDE",
                        "CommonExtensions",
                        "Microsoft",
                        "NuGet",
                        "Plugins");
                    if (Directory.Exists(pluginsRoot))
                        output.Add(pluginsRoot);
                }
            }
        }
        catch (IOException)
        {
            // Best effort discovery only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort discovery only.
        }
        catch (SecurityException)
        {
            // Best effort discovery only.
        }
    }

    private static bool IsCredentialProviderFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fileName = Path.GetFileName(path);
        return string.Equals(fileName, "CredentialProvider.Microsoft.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "CredentialProvider.Microsoft.dll", StringComparison.OrdinalIgnoreCase);
    }
}

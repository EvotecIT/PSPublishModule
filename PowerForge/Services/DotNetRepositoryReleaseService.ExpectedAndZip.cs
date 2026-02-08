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
    private static Dictionary<string, string> BuildExpectedVersionMap(Dictionary<string, string>? map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map is null) return result;

        foreach (var kvp in map)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
            if (string.IsNullOrWhiteSpace(kvp.Value)) continue;
            result[kvp.Key.Trim()] = kvp.Value.Trim();
        }

        return result;
    }

    private static bool IsPackable(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var value = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("IsPackable", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(value)) return true;
            return !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static string BuildReleaseZipPath(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec)
    {
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var releasePath = string.IsNullOrWhiteSpace(spec.ReleaseZipOutputPath)
            ? Path.Combine(csprojDir, "bin", cfg)
            : spec.ReleaseZipOutputPath!;
        var version = string.IsNullOrWhiteSpace(project.NewVersion) ? "0.0.0" : project.NewVersion;
        return Path.Combine(releasePath, $"{project.ProjectName}.{version}.zip");
    }

    private static bool TryCreateReleaseZip(
        DotNetRepositoryProjectResult project,
        string configuration,
        string zipPath,
        out string error)
    {
        error = string.Empty;
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim();
        var releasePath = Path.Combine(csprojDir, "bin", cfg);

        if (!Directory.Exists(releasePath))
        {
            error = $"Release path not found: {releasePath}";
            return false;
        }

        try
        {
            var zipDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(zipDir))
                Directory.CreateDirectory(zipDir);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            var zipFull = Path.GetFullPath(zipPath);

            using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            foreach (var file in Directory.EnumerateFiles(releasePath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(file), zipFull, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch { }

                if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = ComputeRelativePath(releasePath, file);
                var entry = archive.CreateEntry(rel, System.IO.Compression.CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to create release zip: {ex.Message}";
            return false;
        }
    }

}

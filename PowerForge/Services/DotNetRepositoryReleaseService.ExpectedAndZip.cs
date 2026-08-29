using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService {
    private static Dictionary<string, string> BuildExpectedVersionMap(Dictionary<string, string>? map) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map is null) return result;

        foreach (var kvp in map) {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
            if (string.IsNullOrWhiteSpace(kvp.Value)) continue;
            result[kvp.Key.Trim()] = kvp.Value.Trim();
        }

        return result;
    }

    private static bool IsPackable(string csprojPath) {
        try {
            var doc = XDocument.Load(csprojPath);
            var value = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("IsPackable", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(value)) return true;
            return !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        } catch {
            return true;
        }
    }

    private static bool IsPackable(string csprojPath, DotNetRepositoryReleaseSpec spec) {
        if (!spec.WhatIf)
            return IsPackable(csprojPath);

        var evaluation = EvaluatePlannedProject(csprojPath, ResolvePlannedConfiguration(spec), targetFramework: null);
        if (!evaluation.Properties.TryGetValue("IsPackable", out var value) || string.IsNullOrWhiteSpace(value))
            return true;
        value = ExpandPlannedProperties(value, evaluation.Properties);
        if (!bool.TryParse(value, out var isPackable))
            throw new InvalidOperationException($"Cannot determine whether '{csprojPath}' is packable because IsPackable '{value}' is not Boolean.");
        return isPackable;
    }

    private static string ResolvePackageId(
        string csprojPath,
        string fallbackProjectName,
        DotNetRepositoryReleaseSpec spec) {
        if (spec.WhatIf) {
            var evaluation = EvaluatePlannedProject(csprojPath, ResolvePlannedConfiguration(spec), targetFramework: null);
            var packageId = ResolvePlannedPackageIdentity(evaluation.Properties, fallbackProjectName);
            if (packageId.IndexOf("$(", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException($"Cannot determine the planned package id for '{csprojPath}' because '{packageId}' contains an unresolved MSBuild property.");
            return packageId;
        }

        try {
            var doc = XDocument.Load(csprojPath);
            var packageId = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("PackageId", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            return string.IsNullOrWhiteSpace(packageId)
                ? fallbackProjectName
                : (packageId ?? string.Empty).Trim();
        } catch {
            return fallbackProjectName;
        }
    }

    private static string ResolvePlannedPackageIdentity(
        IReadOnlyDictionary<string, string> properties,
        string fallbackProjectName) {
        foreach (var propertyName in new[] { "PackageId", "AssemblyName", "MSBuildProjectName" }) {
            if (!properties.TryGetValue(propertyName, out var value) || string.IsNullOrWhiteSpace(value))
                continue;
            return ExpandPlannedProperties(value, properties).Trim();
        }
        return fallbackProjectName;
    }

    private static string ResolvePlannedConfiguration(DotNetRepositoryReleaseSpec spec)
        => string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();

    private static string BuildReleaseZipPath(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec) {
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var releasePath = string.IsNullOrWhiteSpace(spec.ReleaseZipOutputPath)
            ? Path.Combine(csprojDir, "bin", cfg)
            : spec.ReleaseZipOutputPath!;
        var version = string.IsNullOrWhiteSpace(project.NewVersion) ? "0.0.0" : project.NewVersion;
        var assetName = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        return Path.Combine(releasePath, $"{assetName}.{version}.zip");
    }

    private static bool TryCreateReleaseZip(
        DotNetRepositoryProjectResult project,
        string configuration,
        string zipPath,
        ILogger logger,
        out string error,
        out int fileCount,
        out long inputBytes) {
        error = string.Empty;
        fileCount = 0;
        inputBytes = 0;
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim();
        if (!TryResolveReleaseZipSources(project, csprojDir, cfg, logger, out var sources, out error))
            return false;

        try {
            var zipDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(zipDir))
                Directory.CreateDirectory(zipDir);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            var zipFull = Path.GetFullPath(zipPath);

            using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
            var entryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in sources) {
                foreach (var file in source.Files) {
                    if (string.Equals(Path.GetFullPath(file), zipFull, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                        file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var relativePath = ComputeRelativePath(source.Path, file).Replace('\\', '/');
                    var entryName = string.IsNullOrWhiteSpace(source.EntryPrefix)
                        ? relativePath
                        : source.EntryPrefix + "/" + relativePath;
                    if (!entryNames.Add(entryName))
                        throw new InvalidDataException($"Release output maps more than one file to '{entryName}'.");

                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTimeUtc(file);
                    using var entryStream = entry.Open();
                    using var fileStream = File.OpenRead(file);
                    fileCount++;
                    inputBytes += fileStream.Length;
                    fileStream.CopyTo(entryStream);
                }
            }

            if (fileCount == 0)
                throw new InvalidDataException($"No current build output files were found for {project.ProjectName}.");

            return true;
        } catch (Exception ex) {
            try {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            } catch {
                // Preserve the original archive creation failure.
            }
            error = $"Failed to create release zip: {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveReleaseZipSources(
        DotNetRepositoryProjectResult project,
        string projectDirectory,
        string configuration,
        ILogger logger,
        out ReleaseZipSource[] sources,
        out string error) {
        if (!TryResolveActiveTargetFrameworks(
                project,
                projectDirectory,
                configuration,
                logger,
                out var targetFrameworks,
                out _,
                out error)) {
            sources = Array.Empty<ReleaseZipSource>();
            return false;
        }

        var resolved = new List<ReleaseZipSource>();
        foreach (var targetFramework in targetFrameworks) {
            if (!TryEvaluateCleanupProperties(
                    project,
                    projectDirectory,
                    configuration,
                    targetFramework,
                    runtimeIdentifier: null,
                    "TargetDir,AssemblyName,IntermediateOutputPath,CleanFile",
                    logger,
                    out var properties,
                    out _,
                    out error)) {
                sources = Array.Empty<ReleaseZipSource>();
                return false;
            }

            var targetDirectories = ResolveEvaluatedRoots(properties, projectDirectory, "TargetDir");
            if (targetDirectories.Length != 1) {
                sources = Array.Empty<ReleaseZipSource>();
                error = $"Could not resolve one exact TargetDir for {project.ProjectName}{FormatBuildDimensionContext(targetFramework, runtimeIdentifier: null)}.";
                return false;
            }

            var targetDirectory = targetDirectories[0];
            if (!Directory.Exists(targetDirectory)) {
                sources = Array.Empty<ReleaseZipSource>();
                error = $"Current build output path not found for {project.ProjectName}{FormatBuildDimensionContext(targetFramework, runtimeIdentifier: null)}: {targetDirectory}";
                return false;
            }

            var entryPrefix = string.IsNullOrWhiteSpace(targetFramework) ? string.Empty : targetFramework!.Trim();
            var overlappingSource = resolved.FirstOrDefault(source => ReleaseZipPathsOverlap(source.Path, targetDirectory));
            if (overlappingSource is not null) {
                sources = Array.Empty<ReleaseZipSource>();
                error = $"Target framework outputs '{overlappingSource.EntryPrefix}' and '{entryPrefix}' overlap at '{overlappingSource.Path}' and '{targetDirectory}'. A deterministic release ZIP cannot be created from overlapping framework outputs.";
                return false;
            }

            var assemblyName = properties.TryGetValue("AssemblyName", out var evaluatedAssemblyName) &&
                               IsUsableAssemblyName(evaluatedAssemblyName)
                ? evaluatedAssemblyName.Trim()
                : project.ProjectName;
            if (!TryResolveReleaseZipFiles(
                    project,
                    targetDirectory,
                    assemblyName,
                    properties,
                    projectDirectory,
                    out var files,
                    out error)) {
                sources = Array.Empty<ReleaseZipSource>();
                return false;
            }

            resolved.Add(new ReleaseZipSource(targetDirectory, entryPrefix, files));
        }

        sources = resolved.ToArray();
        if (sources.Length == 0) {
            error = $"No exact current build output paths were resolved for {project.ProjectName}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryResolveReleaseZipFiles(
        DotNetRepositoryProjectResult project,
        string targetDirectory,
        string assemblyName,
        IReadOnlyDictionary<string, string> properties,
        string projectDirectory,
        out string[] files,
        out string error) {
        var intermediateDirectories = ResolveEvaluatedRoots(properties, projectDirectory, "IntermediateOutputPath");
        if (intermediateDirectories.Length != 1 ||
            !properties.TryGetValue("CleanFile", out var cleanFile) ||
            string.IsNullOrWhiteSpace(cleanFile)) {
            files = Array.Empty<string>();
            error = $"Could not resolve one exact MSBuild output manifest for {project.ProjectName}.";
            return false;
        }

        var manifestPath = Path.IsPathRooted(cleanFile)
            ? Path.GetFullPath(cleanFile)
            : Path.GetFullPath(Path.Combine(intermediateDirectories[0], cleanFile.Trim()));
        if (!File.Exists(manifestPath)) {
            files = Array.Empty<string>();
            error = $"Current MSBuild output manifest was not found for {project.ProjectName}: {manifestPath}";
            return false;
        }

        if (!TryValidateReleaseZipPath(manifestPath, out error) ||
            !TryValidateReleaseZipPath(targetDirectory, out error)) {
            files = Array.Empty<string>();
            return false;
        }

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(manifestPath)) {
            var candidate = line.Trim();
            if (candidate.Length == 0)
                continue;
            if (!Path.IsPathRooted(candidate)) {
                files = Array.Empty<string>();
                error = $"MSBuild output manifest contains a non-rooted path for {project.ProjectName}: {candidate}";
                return false;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (!IsPathAtOrWithin(fullPath, targetDirectory))
                continue;
            if (!File.Exists(fullPath)) {
                files = Array.Empty<string>();
                error = $"MSBuild output manifest references a missing current output for {project.ProjectName}: {fullPath}";
                return false;
            }
            if (!TryValidateReleaseZipPath(fullPath, out error)) {
                files = Array.Empty<string>();
                return false;
            }
            resolved.Add(fullPath);
        }

        var hasPrimaryAssembly = resolved.Any(path =>
            string.Equals(Path.GetFileName(path), assemblyName + ".dll", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(path), assemblyName + ".exe", StringComparison.OrdinalIgnoreCase));
        if (!hasPrimaryAssembly) {
            files = Array.Empty<string>();
            error = $"MSBuild output manifest does not contain the primary assembly for {project.ProjectName}: {assemblyName}";
            return false;
        }

        files = resolved.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        error = string.Empty;
        return true;
    }

    internal static bool ReleaseZipPathsOverlap(string left, string right)
        => IsPathAtOrWithin(left, right) || IsPathAtOrWithin(right, left);

    private static bool IsPathAtOrWithin(string path, string root) {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryValidateReleaseZipPath(string path, out string error) {
        try {
            var fullPath = Path.GetFullPath(path);
            var volumeRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
            var current = volumeRoot;
            if (!string.IsNullOrWhiteSpace(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidDataException($"Release output path traverses a linked filesystem root: {current}");
            }

            var relativePath = fullPath.Substring(volumeRoot.Length);
            foreach (var segment in relativePath.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                    throw new FileNotFoundException("Release output path component was not found.", current);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Release output path traverses a linked file or directory: {current}");
            }

            error = string.Empty;
            return true;
        } catch (Exception ex) {
            error = $"Release output path cannot be archived safely: {ex.Message}";
            return false;
        }
    }

    private sealed class ReleaseZipSource {
        internal ReleaseZipSource(string path, string entryPrefix, string[] files) {
            Path = path;
            EntryPrefix = entryPrefix;
            Files = files;
        }

        internal string Path { get; }

        internal string EntryPrefix { get; }

        internal string[] Files { get; }
    }
}

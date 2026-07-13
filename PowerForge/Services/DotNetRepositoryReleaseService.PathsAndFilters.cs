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
    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static List<string> FilterPackages(IEnumerable<string> packages, string projectName, string version)
    {
        var list = new List<string>();
        if (packages is null) return list;
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(version))
            return list;

        var prefix = $"{projectName}.{version}.";
        foreach (var pkg in packages)
        {
            var name = Path.GetFileName(pkg) ?? string.Empty;
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                list.Add(pkg);
        }

        return list;
    }

    internal static string[] GetPackagesForPublish(
        IEnumerable<DotNetRepositoryProjectResult> projects,
        bool includeSymbolPackages = false)
        => projects
            .Where(static project => project is not null)
            .SelectMany(project => includeSymbolPackages
                ? project.Packages.Concat(project.SymbolPackages)
                : project.Packages)
            .Where(static package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string[] GetPublishedArtifacts(
        DotNetRepositoryProjectResult? project,
        string package,
        bool includeCompanionSymbols = true)
    {
        if (project is null || string.IsNullOrWhiteSpace(package))
            return string.IsNullOrWhiteSpace(package) ? Array.Empty<string>() : new[] { package };

        if (!includeCompanionSymbols ||
            package.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { package };
        }

        var packageName = Path.GetFileNameWithoutExtension(package);
        return new[] { package }
            .Concat(project.SymbolPackages.Where(symbolPackage =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(symbolPackage),
                    packageName,
                    StringComparison.OrdinalIgnoreCase)))
            .Where(static artifact => !string.IsNullOrWhiteSpace(artifact))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Determines whether a resolved NuGet publish source is a filesystem feed rather than a named or HTTP source.
    /// </summary>
    internal static bool IsLocalPublishSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return uri.IsFile;

        return Path.IsPathRooted(PathValueResolver.NormalizeSeparators(trimmed));
    }

    internal static string ResolvePublishSource(string source, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        var trimmed = source.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            if (!absoluteUri.IsFile)
                return trimmed;

            return Path.GetFullPath(absoluteUri.LocalPath);
        }

        var normalized = PathValueResolver.NormalizeSeparators(trimmed);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        var localCandidate = Path.Combine(repositoryRoot, normalized);
        if (normalized.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            normalized.StartsWith(".", StringComparison.Ordinal) ||
            Directory.Exists(localCandidate))
        {
            return PathValueResolver.Resolve(repositoryRoot, normalized);
        }

        return trimmed;
    }

}

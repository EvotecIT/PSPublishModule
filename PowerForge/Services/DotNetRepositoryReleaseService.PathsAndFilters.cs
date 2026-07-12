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

    internal static string[] GetPackagesForPublish(IEnumerable<DotNetRepositoryProjectResult> projects)
        => projects
            .Where(static project => project is not null)
            .SelectMany(static project => project.Packages)
            .Where(static package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string[] GetPublishedArtifacts(
        DotNetRepositoryProjectResult? project,
        string package)
    {
        if (project is null || string.IsNullOrWhiteSpace(package))
            return string.IsNullOrWhiteSpace(package) ? Array.Empty<string>() : new[] { package };

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

    internal static string ResolvePublishSource(string source, string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(source))
            return source;

        var trimmed = source.Trim();
        var normalized = PathValueResolver.NormalizeSeparators(trimmed);
        if (Path.IsPathRooted(normalized))
            return Path.GetFullPath(normalized);

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) && !absoluteUri.IsFile)
            return trimmed;

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

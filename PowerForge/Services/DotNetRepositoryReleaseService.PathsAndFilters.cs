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

}

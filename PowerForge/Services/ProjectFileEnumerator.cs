using System.Collections.Generic;
using System.IO;

namespace PowerForge;

/// <summary>
/// Enumerates project files according to project kind or custom patterns while honoring directory excludes.
/// </summary>
public static class ProjectFileEnumerator
{
    /// <summary>
    /// Enumerates files for the provided options.
    /// </summary>
    public static IEnumerable<string> Enumerate(ProjectEnumeration e)
    {
        var excludes = new HashSet<string>(e.ExcludeDirectories, System.StringComparer.OrdinalIgnoreCase);
        var excludeFiles = e.ExcludeFiles ?? System.Array.Empty<string>();
        var stack = new Stack<string>();
        stack.Push(e.EnumerationRoot());
        var patterns = e.CustomExtensions ?? GetDefaultPatterns(e.Kind);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            string name = new DirectoryInfo(current).Name;
            if (!string.Equals(current, e.EnumerationRoot(), System.StringComparison.OrdinalIgnoreCase) && excludes.Contains(name)) continue;

            foreach (var pattern in patterns)
            {
                IEnumerable<string> files = System.Array.Empty<string>();       
                try { files = Directory.EnumerateFiles(current, pattern, SearchOption.TopDirectoryOnly); } catch { }
                foreach (var f in files)
                {
                    if (excludeFiles.Count > 0)
                    {
                        var rel = ComputeRelativePath(e.EnumerationRoot(), f);
                        if (ShouldExcludeFile(rel, excludeFiles))
                            continue;
                    }
                    yield return f;
                }
            }

            IEnumerable<string> dirs = System.Array.Empty<string>();
            try { dirs = Directory.EnumerateDirectories(current); } catch { }
            foreach (var d in dirs) stack.Push(d);
        }
    }

    private static string EnumerationRoot(this ProjectEnumeration e) => e.RootPath;

    private static IReadOnlyList<string> GetDefaultPatterns(ProjectKind kind)
    {
        return kind switch
        {
            ProjectKind.PowerShell => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml" },
            ProjectKind.CSharp     => new[] { "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.resx" },
            ProjectKind.All        => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml", "*.js", "*.ts", "*.py", "*.rb", "*.java", "*.cpp", "*.h", "*.hpp", "*.sql", "*.md", "*.txt", "*.yaml", "*.yml" },
            _ => new[] { "*.ps1", "*.psm1", "*.psd1", "*.ps1xml", "*.cs", "*.csx", "*.csproj", "*.sln", "*.config", "*.json", "*.xml" }
        };
    }

    private static bool ShouldExcludeFile(string relativePath, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || patterns is null || patterns.Count == 0)
            return false;

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (FileConsistencyOverrideResolver.Matches(pattern, relativePath))
                return true;
        }

        return false;
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(baseDir));
            var pathUri = new Uri(fullPath);
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? path
            : path + Path.DirectorySeparatorChar;
}


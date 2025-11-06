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
                foreach (var f in files) yield return f;
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
}


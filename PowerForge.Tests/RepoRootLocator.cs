namespace PowerForge.Tests;

internal static class RepoRootLocator
{
    private const int MaxSearchDepth = 12;

    public static string Find([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        // VSTest can place both the assembly and working directory in external
        // artifacts. The compiled test source is the final discovery origin.
        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory, Path.GetDirectoryName(sourceFilePath) ?? string.Empty })
        {
            if (string.IsNullOrEmpty(start)) continue;
            var current = new DirectoryInfo(start);
            for (var i = 0; i < MaxSearchDepth && current is not null; i++)
            {
                var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
                if (File.Exists(marker))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for PowerForge tests.");
    }
}

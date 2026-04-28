namespace PowerForge.Tests;

internal static class RepoRootLocator
{
    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            var marker = Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj");
            if (File.Exists(marker))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for PowerForge tests.");
    }
}

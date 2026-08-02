namespace PowerForge.Web;

public static partial class WebVisualStoryStager
{
    internal static StringComparison GetFileSystemPathComparison(string path)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory);

        while (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            var name = Path.GetFileName(directory);
            var parent = Path.GetDirectoryName(directory);
            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(parent))
            {
                var alternateName = ToggleCase(name);
                if (!string.Equals(name, alternateName, StringComparison.Ordinal))
                {
                    return Directory.Exists(Path.Combine(parent, alternateName))
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                }
            }

            directory = parent;
        }

        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string ToggleCase(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            var alternate = char.IsUpper(characters[index])
                ? char.ToLowerInvariant(characters[index])
                : char.ToUpperInvariant(characters[index]);
            if (alternate == characters[index])
                continue;

            characters[index] = alternate;
            return new string(characters);
        }

        return value;
    }

    private static bool SamePath(string left, string right, StringComparison comparison)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);

}

namespace PowerForge.Web;

/// <summary>Validates visual-story artifact paths for portable bundle storage.</summary>
internal static class VisualStoryPortablePathValidator
{
    private static readonly HashSet<string> ReservedWindowsDeviceNames = CreateReservedWindowsDeviceNames();

    internal static void Validate(string path)
    {
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.Any(IsWindowsInvalidFileNameCharacter) ||
                IsReservedWindowsDeviceName(segment))
            {
                throw new InvalidOperationException(
                    $"Visual-story artifact path is not portable to Windows: {path}");
            }
        }
    }

    private static bool IsWindowsInvalidFileNameCharacter(char value)
        => value < 32 || value is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*';

    private static bool IsReservedWindowsDeviceName(string segment)
    {
        var extensionIndex = segment.IndexOf('.');
        var stem = (extensionIndex < 0 ? segment : segment.Substring(0, extensionIndex)).TrimEnd(' ', '.');
        return ReservedWindowsDeviceNames.Contains(stem);
    }

    private static HashSet<string> CreateReservedWindowsDeviceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$"
        };
        for (var index = 1; index <= 9; index++)
        {
            names.Add("COM" + index);
            names.Add("LPT" + index);
        }
        names.Add("COM¹");
        names.Add("COM²");
        names.Add("COM³");
        names.Add("LPT¹");
        names.Add("LPT²");
        names.Add("LPT³");
        return names;
    }
}

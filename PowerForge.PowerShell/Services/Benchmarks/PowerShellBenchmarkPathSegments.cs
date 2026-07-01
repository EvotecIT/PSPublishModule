using System.Globalization;

namespace PowerForge;

internal static class PowerShellBenchmarkPathSegments
{
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    internal static string Value(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "_" : value!;
        var escaped = Uri.EscapeDataString(text).Replace("_", "%5F");
        escaped = EscapeTrailingWindowsIgnoredCharacters(escaped);
        escaped = EscapeWindowsDeviceName(text, escaped);
        return escaped switch
        {
            "" => "_",
            "." => "%2E",
            ".." => "%2E%2E",
            _ => escaped
        };
    }

    internal static string Matrix(IReadOnlyDictionary<string, object?> values, Func<string, bool> isBuiltInPathValue)
    {
        var text = string.Join(
            "_",
            values
                .Where(k => !isBuiltInPathValue(k.Key))
                .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                .Select(k => string.Concat(Value(k.Key), "=", Value(Convert.ToString(k.Value, CultureInfo.InvariantCulture)))));
        return string.IsNullOrWhiteSpace(text) ? "matrix" : text;
    }

    private static string EscapeTrailingWindowsIgnoredCharacters(string escaped)
    {
        var end = escaped.Length;
        while (end > 0 && (escaped[end - 1] == '.' || escaped[end - 1] == ' '))
            end--;
        if (end == escaped.Length)
            return escaped;

        var suffix = string.Concat(escaped.Skip(end).Select(ch => ch == '.' ? "%2E" : "%20"));
        return string.Concat(escaped.AsSpan(0, end).ToString(), suffix);
    }

    private static string EscapeWindowsDeviceName(string text, string escaped)
    {
        var candidate = text.TrimEnd(' ', '.');
        var dot = candidate.IndexOf('.');
        if (dot >= 0)
            candidate = candidate.Substring(0, dot);
        if (!WindowsDeviceNames.Contains(candidate))
            return escaped;

        return escaped.Length == 0
            ? "_"
            : string.Concat("%", ((int)escaped[0]).ToString("X2", CultureInfo.InvariantCulture), escaped.Substring(1));
    }
}

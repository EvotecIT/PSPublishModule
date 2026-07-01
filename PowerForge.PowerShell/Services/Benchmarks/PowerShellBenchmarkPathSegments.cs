using System.Globalization;

namespace PowerForge;

internal static class PowerShellBenchmarkPathSegments
{
    internal static string Value(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "_" : value!;
        var escaped = Uri.EscapeDataString(text).Replace("_", "%5F");
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
}

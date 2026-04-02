using System.IO;

namespace PowerForge;

internal static class ModuleManifestValueReader
{
    internal static bool TryGetTopLevelString(string manifestPath, string key, out string? value)
    {
        value = null;
        return TryReadManifestText(manifestPath, out var manifestText) &&
               ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, key, out value);
    }

    internal static string[] ReadTopLevelStringOrArray(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<string>();

        if (ModuleManifestTextParser.TryGetStringArrayValue(manifestText, key, out var values) && values is not null)
            return values;

        if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, key, out var value) && !string.IsNullOrWhiteSpace(value))
            return new[] { value! };

        return Array.Empty<string>();
    }

    private static bool TryReadManifestText(string manifestPath, out string manifestText)
    {
        manifestText = string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return false;

        try
        {
            manifestText = File.ReadAllText(manifestPath);
            return !string.IsNullOrWhiteSpace(manifestText);
        }
        catch
        {
            return false;
        }
    }
}

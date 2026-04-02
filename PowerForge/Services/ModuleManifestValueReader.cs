using System.IO;

namespace PowerForge;

internal static class ModuleManifestValueReader
{
    internal static string? ReadTopLevelString(string manifestPath, string key)
    {
        if (!TryGetTopLevelString(manifestPath, key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        return value!.Trim();
    }

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

    internal static string[] ReadPsDataStringOrArray(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<string>();

        if (ModuleManifestTextParser.TryReadPsDataAssignedExpression(manifestText, key, out var expression) &&
            !string.IsNullOrWhiteSpace(expression))
        {
            if (ModuleManifestTextParser.TryParseStringArrayExpression(expression!, out var values) && values is not null)
                return values;

            if (ModuleManifestTextParser.TryParseQuotedStringExpression(expression!, out var value) && !string.IsNullOrWhiteSpace(value))
                return new[] { value! };
        }

        if (ModuleManifestTextParser.TryGetPsDataStringArrayValue(manifestText, key, out var legacyValues) && legacyValues is not null)
            return legacyValues;

        if (ModuleManifestTextParser.TryGetPsDataStringValue(manifestText, key, out var legacyValue) && !string.IsNullOrWhiteSpace(legacyValue))
            return new[] { legacyValue! };

        if (ModuleManifestTextParser.TryGetStringArrayValue(manifestText, key, out var fallbackValues) && fallbackValues is not null)
            return fallbackValues;

        if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestText, key, out var fallbackValue) && !string.IsNullOrWhiteSpace(fallbackValue))
            return new[] { fallbackValue! };

        return Array.Empty<string>();
    }

    internal static RequiredModuleReference[] ReadRequiredModules(string manifestPath)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<RequiredModuleReference>();

        if (!ModuleManifestTextParser.TryGetRequiredModules(manifestText, out RequiredModuleReference[]? modules) || modules is null)
            return Array.Empty<RequiredModuleReference>();

        return modules;
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

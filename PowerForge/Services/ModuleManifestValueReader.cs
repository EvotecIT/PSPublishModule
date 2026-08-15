using System.IO;

namespace PowerForge;

internal static class ModuleManifestValueReader
{
    internal static string? ReadTopLevelString(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return null;

        return ReadTopLevelStringFromText(manifestText, key);
    }

    internal static string? ReadTopLevelStringFromText(string manifestText, string key)
    {
        if (!ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value!.Trim();
    }

    internal static bool TryGetTopLevelString(string manifestPath, string key, out string? value)
    {
        value = null;
        return TryReadManifestText(manifestPath, out var manifestText) &&
               ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(manifestText, key, out value);
    }

    internal static string[] ReadTopLevelStringOrArray(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<string>();

        return ReadTopLevelStringOrArrayFromText(manifestText, key);
    }

    internal static string[] ReadTopLevelStringOrArrayFromText(string manifestText, string key)
    {
        if (!ModuleManifestTextParser.TryReadTopLevelAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
            return Array.Empty<string>();

        if (ModuleManifestTextParser.TryParseStringArrayExpression(expression!, out var values) && values is not null)
            return values;

        if (ModuleManifestTextParser.TryParseQuotedStringExpression(expression!, out var value) && !string.IsNullOrWhiteSpace(value))
            return new[] { value! };

        return Array.Empty<string>();
    }

    internal static string[] ReadTopLevelModuleReferencePaths(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<string>();

        return ReadTopLevelModuleReferencePathsFromText(manifestText, key);
    }

    internal static string[] ReadTopLevelModuleReferencePathsFromText(string manifestText, string key)
    {
        if (!ModuleManifestTextParser.TryReadTopLevelAssignedExpressionByKey(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
        {
            return Array.Empty<string>();
        }

        if (ModuleManifestTextParser.TryParseModuleReferencePathExpression(expression!, out var paths) && paths is not null)
            return paths;

        throw new InvalidDataException(
            $"PowerShell manifest property '{key}' must contain string paths or module specifications with a literal ModuleName.");
    }

    internal static string[]? ReadTopLevelLiteralStringOrArray(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return null;

        if (ModuleManifestTextParser.TryGetStrictStringArrayValue(manifestText, key, out var values) && values is not null)
            return values;

        return null;
    }

    internal static string[] ReadPsDataStringOrArray(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return Array.Empty<string>();

        return ReadPsDataStringOrArrayFromText(manifestText, key);
    }

    internal static string[] ReadPsDataStringOrArrayFromText(string manifestText, string key)
    {
        if (ModuleManifestTextParser.TryReadPsDataAssignedExpression(manifestText, key, out var expression) &&
            !string.IsNullOrWhiteSpace(expression))
        {
            if (ModuleManifestTextParser.TryParseStringArrayExpression(expression!, out var values) && values is not null)
                return values;

            if (ModuleManifestTextParser.TryParseQuotedStringExpression(expression!, out var value) && !string.IsNullOrWhiteSpace(value))
                return new[] { value! };
        }

        return Array.Empty<string>();
    }

    internal static bool ReadPsDataBoolean(string manifestPath, string key)
    {
        if (!TryReadManifestText(manifestPath, out var manifestText))
            return false;

        return ReadPsDataBooleanFromText(manifestText, key);
    }

    internal static bool ReadPsDataBooleanFromText(string manifestText, string key)
    {
        if (!ModuleManifestTextParser.TryReadPsDataAssignedExpression(manifestText, key, out var expression) ||
            string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        return ModuleManifestTextParser.TryParseBooleanExpression(expression!, out var value) && value;
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

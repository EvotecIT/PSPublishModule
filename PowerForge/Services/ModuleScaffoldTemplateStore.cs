using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace PowerForge;

internal static class ModuleScaffoldTemplateStore
{
    private static readonly string[] TemplateFileNames =
    {
        "Example-Gitignore.txt",
        "Example-CHANGELOG.MD",
        "Example-README.MD",
        "Example-LicenseMIT.txt",
        "Example-ModuleBuilder.txt",
        "Example-ModulePSM1.txt",
        "Example-ModulePSD1.txt"
    };

    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryLoadDefaults(out IReadOnlyDictionary<string, string> templates)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var fileName in TemplateFileNames)
            {
                values[fileName] = Cache.GetOrAdd(fileName, LoadUncached);
            }

            templates = values;
            return true;
        }
        catch
        {
            templates = values;
            return false;
        }
    }

    private static string LoadUncached(string fileName)
    {
        var resourceName = "PowerForge.Templates.ModuleScaffold." + fileName;
        var assembly = typeof(ModuleScaffoldTemplateStore).GetTypeInfo().Assembly;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded module scaffold template not found: {resourceName}. Ensure '{fileName}' is included as an EmbeddedResource.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

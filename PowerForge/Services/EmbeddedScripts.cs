using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text;

namespace PowerForge;

internal static class EmbeddedScripts
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(StringComparer.Ordinal);

    internal static string Load(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path is required.", nameof(relativePath));

        var normalized = relativePath
            .Replace('\\', '/')
            .TrimStart('/')
            .Trim();

        return Cache.GetOrAdd(normalized, LoadUncached);
    }

    private static string LoadUncached(string relativePath)
    {
        var resourceName = "PowerForge." + relativePath.Replace('/', '.');

        var assembly = typeof(EmbeddedScripts).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded PowerShell script not found: {resourceName}. Ensure '{relativePath}' is included as an EmbeddedResource.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}


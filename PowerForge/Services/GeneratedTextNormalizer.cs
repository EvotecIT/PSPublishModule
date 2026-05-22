using System.IO;
using System.Text;

namespace PowerForge;

internal static class GeneratedTextNormalizer
{
    internal static string Normalize(string? text, string newline = "\r\n", bool ensureFinalNewline = true)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd(' ', '\t');

        normalized = string.Join("\n", lines).TrimEnd('\n');
        if (normalized.Length == 0)
            return string.Empty;

        if (!string.Equals(newline, "\n", StringComparison.Ordinal))
            normalized = normalized.Replace("\n", newline);

        return ensureFinalNewline ? normalized + newline : normalized;
    }

    internal static void WriteUtf8NoBom(string path, string? text)
    {
        File.WriteAllText(path, Normalize(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static void WriteUtf8Bom(string path, string? text)
    {
        File.WriteAllText(path, Normalize(text), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}

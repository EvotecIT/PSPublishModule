using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class CsvWriter
{
    internal static void Write(string path, IEnumerable<string> headers, IEnumerable<string[]> rows)
    {
        using var sw = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        sw.WriteLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
            sw.WriteLine(string.Join(",", row.Select(Escape)));
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!mustQuote) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}


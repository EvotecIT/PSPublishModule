using System.Text;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Normalizes line endings and encoding in a deterministic way.
/// Defaults: CRLF and UTF-8 with BOM for PowerShell file types.
/// </summary>
public sealed class LineEndingsNormalizer : ILineEndingsNormalizer
{
    private static readonly Encoding Utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly char[] NewlineCharacters = { '\r', '\n' };

    /// <inheritdoc />
    public NormalizationResult NormalizeFile(string path, NormalizationOptions? options = null)
    {
        options ??= new NormalizationOptions();
        var originalBytes = File.ReadAllBytes(path);
        string text;
        using (var stream = new MemoryStream(originalBytes, writable: false))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        // Detect dominant line ending if Auto
        var target = options.LineEnding switch
        {
            LineEnding.CRLF => "\r\n",
            LineEnding.LF => "\n",
            LineEnding.Auto => DetectDominant(text),
            _ => "\r\n"
        };

        var normalized = NormalizeEndings(text, target);
        var replacements = CountReplacements(text, normalized);

        // Save with desired encoding
        Encoding encoding = Utf8Bom;
        if (!options.EnsureUtf8Bom)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        var preamble = encoding.GetPreamble();
        var normalizedBytes = new byte[preamble.Length + encoding.GetByteCount(normalized)];
        Buffer.BlockCopy(preamble, 0, normalizedBytes, 0, preamble.Length);
        encoding.GetBytes(normalized, 0, normalized.Length, normalizedBytes, preamble.Length);
        var changed = !originalBytes.SequenceEqual(normalizedBytes);
        if (changed)
            File.WriteAllBytes(path, normalizedBytes);
        return new NormalizationResult(path, changed, replacements, encoding.WebName);
    }

    /// <summary>
    /// Determines the dominant line ending of the provided text.
    /// </summary>
    private static string DetectDominant(string text)
    {
        int crlf = 0, lf = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (i > 0 && text[i - 1] == '\r') crlf++; else lf++;
            }
        }
        // Prefer CRLF when equal to align with Windows PS ecosystem
        return crlf >= lf ? "\r\n" : "\n";
    }

    /// <summary>
    /// Converts line endings in <paramref name="text"/> to the <paramref name="target"/> style.
    /// </summary>
    private static string NormalizeEndings(string text, string target)
    {
        if (target == "\r\n")
        {
            // Keep the original text when every newline is already a CRLF pair.
            // Replacing CRLF with LF and back otherwise creates two full-size copies.
            if (HasOnlyCrLf(text)) return text;
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        if (text.IndexOf('\r') < 0) return text;
        return text.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static bool HasOnlyCrLf(string text)
    {
        var position = text.IndexOfAny(NewlineCharacters);
        while (position >= 0)
        {
            if (text[position] != '\r' || position + 1 >= text.Length || text[position + 1] != '\n')
                return false;
            position = text.IndexOfAny(NewlineCharacters, position + 2);
        }
        return true;
    }

    /// <summary>
    /// Approximates the number of newline changes between two snapshots.
    /// </summary>
    private static int CountReplacements(string before, string after)
    {
        if (ReferenceEquals(before, after) || before.Equals(after, StringComparison.Ordinal)) return 0;
        // Approximate: count line ending differences
        int bLf = before.Count(c => c == '\n');
        int aLf = after.Count(c => c == '\n');
        return Math.Abs(aLf - bLf);
    }
}

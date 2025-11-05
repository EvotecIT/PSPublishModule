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

    /// <inheritdoc />
    public NormalizationResult NormalizeFile(string path, NormalizationOptions? options = null)
    {
        options ??= new NormalizationOptions();
        var text = File.ReadAllText(path);

        // Detect dominant line ending if Auto
        var target = options.LineEnding switch
        {
            LineEnding.CRLF => "\r\n",
            LineEnding.LF => "\n",
            LineEnding.Auto => DetectDominant(text),
            _ => "\r\n"
        };

        var normalized = NormalizeEndings(text, target);
        var changed = !ReferenceEquals(text, normalized) && !text.Equals(normalized, StringComparison.Ordinal);
        var replacements = CountReplacements(text, normalized);

        // Save with desired encoding
        Encoding encoding = Utf8Bom;
        if (!options.EnsureUtf8Bom)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        File.WriteAllText(path, normalized, encoding);
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
        // Fast path: already consistent
        if (target == "\r\n")
        {
            // Replace bare LF not preceded by CR
            return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }
        else
        {
            // Target LF: remove CRs before LFs
            return text.Replace("\r\n", "\n");
        }
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

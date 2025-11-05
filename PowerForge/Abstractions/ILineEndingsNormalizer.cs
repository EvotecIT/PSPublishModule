using System.Text;

namespace PowerForge;

/// <summary>
/// Preferred line ending kinds PowerForge can normalize to.
/// </summary>
public enum LineEnding
{
    /// <summary>
    /// Detects the dominant line ending in the input and normalizes to that.
    /// </summary>
    Auto,
    /// <summary>
    /// Carriage Return + Line Feed (Windows style, \r\n).
    /// </summary>
    CRLF,
    /// <summary>
    /// Line Feed only (Unix style, \n).
    /// </summary>
    LF
}

/// <summary>
/// Options controlling the normalization process.
/// </summary>
public sealed class NormalizationOptions
{
    /// <summary>
    /// Target line ending style; default is <see cref="LineEnding.CRLF"/>.
    /// </summary>
    public LineEnding LineEnding { get; }
    /// <summary>
    /// If true, saves UTF-8 with BOM; otherwise UTF-8 without BOM.
    /// </summary>
    public bool EnsureUtf8Bom { get; }
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public NormalizationOptions(LineEnding lineEnding = LineEnding.CRLF, bool ensureUtf8Bom = true)
    { LineEnding = lineEnding; EnsureUtf8Bom = ensureUtf8Bom; }
}

/// <summary>
/// Result of a file normalization operation.
/// </summary>
public sealed class NormalizationResult
{
    /// <summary>Normalized file path.</summary>
    public string Path { get; }
    /// <summary>Whether content changed during normalization.</summary>
    public bool Changed { get; }
    /// <summary>Approximate count of line-ending changes.</summary>
    public int Replacements { get; }
    /// <summary>Encoding used to write the file.</summary>
    public string EncodingName { get; }
    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public NormalizationResult(string path, bool changed, int replacements, string encodingName)
    { Path = path; Changed = changed; Replacements = replacements; EncodingName = encodingName; }
}

/// <summary>
/// Normalizes line endings and encoding of files deterministically.
/// </summary>
public interface ILineEndingsNormalizer
{
    /// <summary>
    /// Normalizes the specified file’s line endings and encoding.
    /// </summary>
    /// <param name="path">Path to the file to normalize.</param>
    /// <param name="options">Normalization behavior; if <c>null</c>, defaults are used.</param>
    /// <returns>Normalization result with change information.</returns>
    NormalizationResult NormalizeFile(string path, NormalizationOptions? options = null);
}

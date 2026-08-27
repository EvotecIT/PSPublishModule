using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Owns PowerShell parsing and stable source-document identity.
/// </summary>
internal static class PowerShellSourceParser
{
    internal static ParsedSourceDocument ParseFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    internal static ParsedSourceDocument Parse(string source, string path)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var fullPath = Path.GetFullPath(path);
        // The Windows PowerShell 5.1 parser exposes only the shared three-argument overload.
        // Document identity is carried separately so semantic spans remain stable across hosts.
        var syntax = Parser.ParseInput(source, out var tokens, out var errors);
        return new ParsedSourceDocument(CreateDocumentId(fullPath), fullPath, source, syntax, tokens, errors);
    }

    internal static SourceSpan GetSpan(ParsedSourceDocument document, IScriptExtent extent)
        => new(
            document.DocumentId,
            extent.StartOffset,
            extent.EndOffset,
            extent.StartLineNumber,
            extent.StartColumnNumber,
            extent.EndLineNumber,
            extent.EndColumnNumber);

    private static string CreateDocumentId(string path)
    {
        var normalized = path.Replace('\\', '/').ToUpperInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}

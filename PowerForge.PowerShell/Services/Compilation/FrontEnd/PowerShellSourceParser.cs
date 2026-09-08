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
        => ParseFile(path, Path.GetDirectoryName(Path.GetFullPath(path)));

    internal static ParsedSourceDocument ParseFile(string path, string? identityRoot)
    {
        var fullPath = Path.GetFullPath(path);
        return Parse(File.ReadAllText(fullPath), fullPath, identityRoot);
    }

    internal static ParsedSourceDocument Parse(string source, string path)
        => Parse(source, path, Path.GetDirectoryName(Path.GetFullPath(path)));

    internal static ParsedSourceDocument Parse(string source, string path, string? identityRoot)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var fullPath = Path.GetFullPath(path);
        // The Windows PowerShell 5.1 parser exposes only the shared three-argument overload.
        // Document identity is carried separately so semantic spans remain stable across hosts.
        var syntax = Parser.ParseInput(source, out var tokens, out var errors);
        return new ParsedSourceDocument(CreateDocumentId(fullPath, identityRoot), fullPath, source, syntax, tokens, errors);
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

    internal static string GetParameterBlockSource(ParamBlockAst? parameterBlock)
    {
        if (parameterBlock is null) return string.Empty;
        return string.Join(
            Environment.NewLine,
            parameterBlock.Attributes.Select(static attribute => attribute.Extent.Text)
                .Append(parameterBlock.Extent.Text));
    }

    /// <summary>Returns complete authored lines, preserving their original line endings for native error metadata.</summary>
    internal static string GetSourceLines(ParsedSourceDocument document, SourceSpan span)
    {
        var start = span.StartOffset - span.StartColumn + 1;
        var endLineStart = span.EndOffset - span.EndColumn + 1;
        var end = endLineStart;
        while (end < document.Text.Length && document.Text[end] is not '\r' and not '\n') end++;
        if (end < document.Text.Length && document.Text[end++] == '\r' &&
            end < document.Text.Length && document.Text[end] == '\n') end++;
        return document.Text.Substring(start, end - start);
    }

    internal static string CreateDocumentId(string path, string? identityRoot, StringComparison? pathComparison = null)
    {
        var fullPath = Path.GetFullPath(path);
        var root = string.IsNullOrWhiteSpace(identityRoot)
            ? Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory()
            : Path.GetFullPath(identityRoot);
        var normalized = FrameworkCompatibility.GetRelativePath(root, fullPath).Replace('\\', '/');
        if ((pathComparison ?? PowerShellCompilationPathSafety.GetPathComparison(fullPath)) == StringComparison.OrdinalIgnoreCase)
            normalized = normalized.ToUpperInvariant();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}

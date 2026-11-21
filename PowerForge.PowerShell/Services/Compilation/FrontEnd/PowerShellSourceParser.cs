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

    internal static string HostSemanticProfileId
#if NETFRAMEWORK
        => PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId;
#else
        => PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
#endif

    internal static ParsedSourceDocument ParseFile(string path, string? identityRoot,
        string semanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
        var fullPath = Path.GetFullPath(path);
        return ParseFile(File.OpenRead(fullPath), fullPath, identityRoot, semanticProfileId);
    }

    internal static ParsedSourceDocument ParseFile(Stream contents, string fullPath, string? identityRoot, string semanticProfileId)
    {
        try
        {
            return Parse(ReadText(contents, semanticProfileId), fullPath, identityRoot);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidOperationException(
                $"Source '{fullPath}' uses non-ASCII bytes without a Unicode byte order mark. Add a UTF-8 BOM or compile the Windows PowerShell 5.1 profile on Windows.",
                error);
        }
    }

    internal static string ReadText(Stream contents, string semanticProfileId)
    {
        using (contents)
        {
            var profile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
            var encoding = profile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51
                ? GetDesktopDefaultEncoding() : Encoding.UTF8;
            using var reader = new StreamReader(contents, encoding, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }

    private static Encoding GetDesktopDefaultEncoding()
    {
#if NETFRAMEWORK
        return Encoding.Default;
#else
        // The provider's code page 0 represents the active Windows ANSI code page.
        return OperatingSystem.IsWindows()
            ? CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8
            : Encoding.GetEncoding(20127, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
#endif
    }

    internal static ParsedSourceDocument ParseHostFile(string path, string? identityRoot = null)
        => ParseFile(path, identityRoot, HostSemanticProfileId);

    internal static ParsedSourceDocument Parse(string source, string path)
        => Parse(source, path, Path.GetDirectoryName(Path.GetFullPath(path)));

    internal static ParsedSourceDocument Parse(string source, string path, string? identityRoot)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var fullPath = Path.GetFullPath(path);
#if NETFRAMEWORK
        var syntax = ParseDesktopFile(source, fullPath, out var tokens, out var errors);
#else
        var syntax = Parser.ParseInput(source, fullPath, out var tokens, out var errors);
#endif
        return new ParsedSourceDocument(CreateDocumentId(fullPath, identityRoot), fullPath, source, syntax, tokens, errors);
    }

#if NETFRAMEWORK
    private delegate ScriptBlockAst DesktopFileParser(string source, string path, out Token[] tokens, out ParseError[] errors);

    // Windows PowerShell exposes the file-aware overload at runtime, while the 5.1 reference
    // assembly exposes only the overload without a file name.
    private static readonly DesktopFileParser ParseDesktopFile = (DesktopFileParser)Delegate.CreateDelegate(
        typeof(DesktopFileParser), typeof(Parser), nameof(Parser.ParseInput));
#endif

    internal static SourceSpan GetSpan(ParsedSourceDocument document, IScriptExtent extent)
        => new(
            document.DocumentId,
            extent.StartOffset,
            extent.EndOffset,
            extent.StartLineNumber,
            extent.StartColumnNumber,
            extent.EndLineNumber,
            extent.EndColumnNumber);

    /// <summary>Finds source-ordered declaration bodies admitted by the selected compilation host.</summary>
    internal static IEnumerable<FunctionDefinitionAst> GetTopLevelFunctions(ScriptBlockAst syntax,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
        => GetRootFunctionDeclarations(syntax)
            .Where(declaration => !declaration.IsConditional || declaration.UsesConditionalNativeBinding(capabilities))
            .Select(static declaration => declaration.Syntax);

    internal static PowerShellRootFunctionDeclaration[] GetRootFunctionDeclarations(ScriptBlockAst syntax)
    {
        var functions = syntax.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<FunctionDefinitionAst>()
            .Where(function => IsOwnedByRoot(function, syntax))
            .OrderBy(static function => function.Extent.StartOffset)
            .ToArray();
        var replacements = functions.GroupBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1 && group.All(function => !function.IsWorkflow &&
                ReferenceEquals(function.Parent, syntax.EndBlock)))
            .Select(static group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return functions.Select(function => new PowerShellRootFunctionDeclaration(function, syntax,
            replacements.Contains(function.Name))).ToArray();
    }

    private static bool IsOwnedByRoot(FunctionDefinitionAst function, ScriptBlockAst root)
    {
        for (var parent = function.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or FunctionMemberAst or TypeDefinitionAst) return false;
            if (parent is ScriptBlockAst block) return ReferenceEquals(block, root);
        }
        return false;
    }

    internal static string GetParameterBlockSource(ParamBlockAst? parameterBlock)
    {
        if (parameterBlock is null) return string.Empty;
        return string.Join(
            Environment.NewLine,
            parameterBlock.Attributes.Select(static attribute => attribute.Extent.Text)
                .Append(parameterBlock.Extent.Text));
    }

    /// <summary>Reads directly owned using declarations through the shared AST visitor contract.</summary>
    internal static bool HasUsingStatements(ScriptBlockAst syntax)
        // Reference assets expose StatementAst while Windows PowerShell returns UsingStatementAst.
        // A direct getter call binds to that incompatible generic return signature on Desktop.
        => syntax.Find(node => node is UsingStatementAst && ReferenceEquals(node.Parent, syntax),
            searchNestedScriptBlocks: false) is not null;

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

    internal static string GetSourceLine(string source, SourceSpan span)
    {
        var start = span.StartOffset - span.StartColumn + 1;
        var end = span.StartOffset;
        while (end < source.Length && source[end] is not '\r' and not '\n') end++;
        return source.Substring(start, end - start);
    }

    internal static int GetLineBreakLength(string source, int offset)
        => source[offset] switch
        {
            '\r' => offset + 1 < source.Length && source[offset + 1] == '\n' ? 2 : 1,
            '\n' => 1,
            _ => 0
        };

    internal static (int Line, int Column) GetLineColumn(string source, int offset)
    {
        if (offset < 0 || offset > source.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            var width = GetLineBreakLength(source, index);
            if (width == 0) continue;
            index += width - 1;
            line++;
            lineStart = index + 1;
        }
        return (line, offset - lineStart + 1);
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

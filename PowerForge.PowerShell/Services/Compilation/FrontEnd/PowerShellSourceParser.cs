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

    internal static ParsedSourceDocument ParseFile(string path, string? identityRoot,
        string semanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
        var fullPath = Path.GetFullPath(path);
        var profile = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId);
        if (profile.Family != PowerShellCompilationSemanticHostFamily.WindowsPowerShell51)
            return Parse(File.ReadAllText(fullPath), fullPath, identityRoot);

#if NETFRAMEWORK
        var encoding = Encoding.Default;
#else
        // The provider's code page 0 means the active Windows ANSI code page.
        // Encoding.Default itself is UTF-8 on modern .NET.
        var encoding = OperatingSystem.IsWindows()
            ? CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.UTF8
            : Encoding.GetEncoding(20127, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
#endif
        try
        {
            using var reader = new StreamReader(fullPath, encoding, detectEncodingFromByteOrderMarks: true);
            return Parse(reader.ReadToEnd(), fullPath, identityRoot);
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException(
                $"Source '{fullPath}' uses non-ASCII bytes without a Unicode byte order mark. Add a UTF-8 BOM or compile the Windows PowerShell 5.1 profile on Windows.",
                error);
        }
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

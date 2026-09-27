using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Parser-owned PowerShell syntax and source text. Syntax objects do not cross the binding boundary.
/// </summary>
internal sealed class ParsedSourceDocument
{
    internal ParsedSourceDocument(
        string documentId,
        string path,
        string text,
        ScriptBlockAst syntaxRoot,
        Token[] tokens,
        ParseError[] errors,
        PowerShellAuthoredSourceProjection? authoredProjection = null,
        PowerShellNativeDependencyTypes? nativeDependencyTypes = null,
        string? nativeScriptRootName = null)
    {
        DocumentId = documentId;
        Path = path;
        Text = text;
        SyntaxRoot = syntaxRoot;
        Tokens = tokens;
        Errors = errors;
        AuthoredProjection = authoredProjection;
        NativeScriptRootName = nativeScriptRootName;
        NativeDependencyTypes = nativeDependencyTypes ?? PowerShellNativeDependencyTypes.Empty;
        TypeClosure = PowerShellSourceTypeClosure.Discover(this);
    }

    internal string DocumentId { get; }
    internal string Path { get; }
    internal string Text { get; }
    internal ScriptBlockAst SyntaxRoot { get; }
    internal Token[] Tokens { get; }
    internal ParseError[] Errors { get; }
    internal PowerShellAuthoredSourceProjection? AuthoredProjection { get; }
    /// <summary>Identifies a compiler-created script-root adapter whose storage belongs to a native script invocation.</summary>
    internal string? NativeScriptRootName { get; }
    internal PowerShellSourceTypeClosure TypeClosure { get; }
    internal PowerShellNativeDependencyTypes NativeDependencyTypes { get; }
    internal ParsedSourceDocument WithNativeDependencyTypes(PowerShellNativeDependencyTypes types)
        => ReferenceEquals(types, NativeDependencyTypes) ? this : new ParsedSourceDocument(
            DocumentId, Path, Text, SyntaxRoot, Tokens, Errors, AuthoredProjection, types, NativeScriptRootName);
}

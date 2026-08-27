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
        ParseError[] errors)
    {
        DocumentId = documentId;
        Path = path;
        Text = text;
        SyntaxRoot = syntaxRoot;
        Tokens = tokens;
        Errors = errors;
    }

    internal string DocumentId { get; }
    internal string Path { get; }
    internal string Text { get; }
    internal ScriptBlockAst SyntaxRoot { get; }
    internal Token[] Tokens { get; }
    internal ParseError[] Errors { get; }
}

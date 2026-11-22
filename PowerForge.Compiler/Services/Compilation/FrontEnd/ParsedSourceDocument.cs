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
        string? nativeScriptRootName = null,
        bool preserveFunctionDeclarationTiming = false,
        bool guardPackagedPathReads = false)
    {
        DocumentId = documentId;
        Path = path;
        Text = text;
        SyntaxRoot = syntaxRoot;
        Tokens = tokens;
        Errors = errors;
        AuthoredProjection = authoredProjection;
        NativeScriptRootName = nativeScriptRootName;
        PreserveFunctionDeclarationTiming = preserveFunctionDeclarationTiming;
        GuardPackagedPathReads = guardPackagedPathReads;
        NativeDependencyTypes = nativeDependencyTypes ?? PowerShellNativeDependencyTypes.Empty;
        RootFunctionDeclarations = PowerShellSourceParser.GetRootFunctionDeclarations(syntaxRoot);
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
    /// <summary>Requires each emitted function to enter native command lookup only when its authored declaration runs.</summary>
    internal bool PreserveFunctionDeclarationTiming { get; }
    /// <summary>Admits dynamic provider reads only behind the packaged executable's FileSystem operand guard.</summary>
    internal bool GuardPackagedPathReads { get; }
    internal PowerShellSourceTypeClosure TypeClosure { get; }
    internal PowerShellRootFunctionDeclaration[] RootFunctionDeclarations { get; }
    internal PowerShellNativeDependencyTypes NativeDependencyTypes { get; }
    internal ParsedSourceDocument WithNativeDependencyTypes(PowerShellNativeDependencyTypes types)
        => ReferenceEquals(types, NativeDependencyTypes) ? this : new ParsedSourceDocument(
            DocumentId, Path, Text, SyntaxRoot, Tokens, Errors, AuthoredProjection, types, NativeScriptRootName,
            PreserveFunctionDeclarationTiming, GuardPackagedPathReads);

    internal ParsedSourceDocument WithFunctionDeclarationTiming()
        => PreserveFunctionDeclarationTiming ? this : new ParsedSourceDocument(
            DocumentId, Path, Text, SyntaxRoot, Tokens, Errors, AuthoredProjection, NativeDependencyTypes, NativeScriptRootName,
            preserveFunctionDeclarationTiming: true, guardPackagedPathReads: GuardPackagedPathReads);

    internal ParsedSourceDocument WithPackagedPathGuards()
        => GuardPackagedPathReads ? this : new ParsedSourceDocument(
            DocumentId, Path, Text, SyntaxRoot, Tokens, Errors, AuthoredProjection, NativeDependencyTypes,
            NativeScriptRootName, PreserveFunctionDeclarationTiming, guardPackagedPathReads: true);
}

using System;
using System.Collections.Generic;
using System.Text;

namespace PowerForge;

/// <summary>
/// Defines the deterministic mapping from PowerShell names to public CLR identifiers.
/// </summary>
public static class PowerShellClrSymbolMapper
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed",
        "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
        "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>Maps one authored name to the identifier emitted by PowerForge's C# backends.</summary>
    public static string MapIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Generated";
        var builder = new StringBuilder(value.Length + 1);
        if (!char.IsLetter(value[0]) && value[0] != '_') builder.Append('_');
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        var identifier = builder.ToString();
        return CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;
    }
}

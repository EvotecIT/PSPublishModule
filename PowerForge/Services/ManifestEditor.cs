using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Edits PowerShell data files (PSD1) safely using the PowerShell AST, preserving file layout.
/// Only the targeted value text is replaced; comments and other content remain untouched.
/// </summary>
public static class ManifestEditor
{
    private static readonly string NewLine = Environment.NewLine;

    /// <summary>
    /// Sets the top-level ModuleVersion value in a PSD1 file to <paramref name="newVersion"/>.
    /// Returns true if a change was made.
    /// </summary>
    public static bool TrySetTopLevelModuleVersion(string filePath, string newVersion)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;

        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;

        // Find ModuleVersion at top level only
        foreach (var kv in topHash.KeyValuePairs)
        {
            var keyName = GetKeyName(kv.Item1);
            if (!string.Equals(keyName, "ModuleVersion", StringComparison.OrdinalIgnoreCase)) continue;
            var valAst = kv.Item2;
            if (valAst == null) continue;
            var start = valAst.Extent.StartOffset;
            var end = valAst.Extent.EndOffset;
            var replacement = $"'{newVersion}'";
            if (start < 0 || end < 0 || end <= start || end > content.Length) return false;
            var newContent = content.Substring(0, start) + replacement + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                // Save as UTF-8 with BOM for PS 5.1 compatibility
                File.WriteAllText(filePath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                return true;
            }
            return false;
        }
        // If missing, insert near the end before closing brace
        return InsertKeyValue(topHash, content, filePath, "ModuleVersion", $"'{newVersion}'");
    }

    /// <summary>Gets a top-level string value by key. Returns true if found.</summary>
    public static bool TryGetTopLevelString(string filePath, string key, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;
        foreach (var kv in topHash.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var expr = AsExpression(kv.Item2);
            switch (expr)
            {
                case StringConstantExpressionAst s: value = s.Value; return true;
                case ConstantExpressionAst c when c.Value is string str: value = str; return true;
            }
            return false;
        }
        return false;
    }

    /// <summary>Sets (or inserts) a top-level string value.</summary>
    public static bool TrySetTopLevelString(string filePath, string key, string newValue)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;
        foreach (var kv in topHash.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Item2; if (v == null) break;
            var start = v.Extent.StartOffset; var end = v.Extent.EndOffset;
            var replacement = $"'{newValue.Replace("'", "''")}'";
            var newContent = content.Substring(0, start) + replacement + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
                return true;
            }
            return false;
        }
        return InsertKeyValue(topHash, content, filePath, key, $"'{newValue.Replace("'", "''")}'");
    }

    /// <summary>Gets a top-level string array by key. Returns true if found.</summary>
    public static bool TryGetTopLevelStringArray(string filePath, string key, out string[]? values)
    {
        values = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;
        foreach (var kv in topHash.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var arr = ExtractStringArray(kv.Item2);
            if (arr != null) { values = arr; return true; }
            return false;
        }
        return false;
    }

    /// <summary>Sets (or inserts) a top-level string array value.</summary>
    public static bool TrySetTopLevelStringArray(string filePath, string key, string[] values)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;
        var arrayText = "@(" + string.Join(", ", values.Select(EscapeAndQuote)) + ")";
        foreach (var kv in topHash.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Item2; if (v == null) break;
            var start = v.Extent.StartOffset; var end = v.Extent.EndOffset;
            var newContent = content.Substring(0, start) + arrayText + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
                return true;
            }
            return false;
        }
        return InsertKeyValue(topHash, content, filePath, key, arrayText);
    }

    /// <summary>Adds an item to a top-level string array if missing. Returns true if modified.</summary>
    public static bool TryAddToTopLevelStringArray(string filePath, string key, string item)
    {
        if (!TryGetTopLevelStringArray(filePath, key, out var current) || current == null)
        {
            return TrySetTopLevelStringArray(filePath, key, new[] { item });
        }
        if (current.Any(s => string.Equals(s, item, StringComparison.OrdinalIgnoreCase))) return false;
        var updated = current.Concat(new[] { item }).ToArray();
        return TrySetTopLevelStringArray(filePath, key, updated);
    }

    /// <summary>Removes an item from a top-level string array if present. Returns true if modified.</summary>
    public static bool TryRemoveFromTopLevelStringArray(string filePath, string key, string item)
    {
        if (!TryGetTopLevelStringArray(filePath, key, out var current) || current == null) return false;
        var updated = current.Where(s => !string.Equals(s, item, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (updated.Length == current.Length) return false;
        return TrySetTopLevelStringArray(filePath, key, updated);
    }

    private static string? GetKeyName(ExpressionAst key)
    {
        switch (key)
        {
            case StringConstantExpressionAst s: return s.Value;
            case ConstantExpressionAst c when c.Value is string str: return str;
            default: return key.Extent.Text?.Trim('\'', '"');
        }
    }

    private static bool HasHashtableAncestor(Ast a)
    {
        var p = a.Parent;
        while (p != null)
        {
            if (p is HashtableAst) return true;
            p = p.Parent;
        }
        return false;
    }

    private static string EscapeAndQuote(string s) => $"'{(s ?? string.Empty).Replace("'", "''")}'";

    private static string[]? ExtractStringArray(Ast expr)
    {
        var e2 = AsExpression(expr);
        if (e2 is ArrayExpressionAst ae)
        {
            // @(...)
            if (ae.SubExpression is StatementBlockAst sb)
            {
                var vals = sb.Statements
                    .Select(s => AsExpression(s) as ExpressionAst)
                    .Select(x => x is StringConstantExpressionAst s ? s.Value : (x is ConstantExpressionAst c && c.Value is string str ? str : null))
                    .Where(x => x != null)
                    .ToArray();
                if (vals.Length > 0) return vals!;
            }
        }
        else if (e2 is ArrayLiteralAst al2)
        {
            var list = al2.Elements
                .Select(e => e is StringConstantExpressionAst s ? s.Value : (e is ConstantExpressionAst c && c.Value is string str ? str : null))
                .ToArray();
            if (list.All(x => x != null)) return list!;
        }
        return null;
    }

    private static ExpressionAst? AsExpression(Ast ast)
    {
        if (ast is ExpressionAst ee) return ee;
        if (ast is PipelineAst p)
        {
            if (p.PipelineElements.Count == 1 && p.PipelineElements[0] is CommandExpressionAst ce) return ce.Expression;
        }
        if (ast is StatementAst s)
        {
            // Statement with a pipeline expression as content
            if (s is PipelineAst ps) return AsExpression(ps);
        }
        return null;
    }

    private static bool InsertKeyValue(HashtableAst topHash, string content, string filePath, string key, string valueExpression)
    {
        // Determine indentation
        var indent = "    ";
        if (topHash.KeyValuePairs.Count > 0)
        {
            var first = topHash.KeyValuePairs[0];
            var line = content.Substring(first.Item1.Extent.StartOffset, first.Item1.Extent.EndOffset - first.Item1.Extent.StartOffset);
            var col = first.Item1.Extent.StartColumnNumber - 1;
            indent = new string(' ', Math.Max(0, col));
        }
        var insertText = NewLine + indent + key + " = " + valueExpression + NewLine;
        // Insert before closing '}' of the hashtable
        var end = topHash.Extent.EndOffset; // points just after '}'
        // try to insert just before '}'
        int closingPos = end - 1;
        if (closingPos < 0 || closingPos >= content.Length) return false;
        var newContent = content.Substring(0, closingPos) + insertText + content.Substring(closingPos);
        File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
        return true;
    }
}

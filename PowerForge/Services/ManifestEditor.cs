using System;
using System.Collections.Generic;
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

    /// <summary>Represents a single RequiredModules entry.</summary>
    public sealed class RequiredModule
    {
        /// <summary>Module name.</summary>
        public string ModuleName { get; }
        /// <summary>Optional explicit module version.</summary>
        public string? ModuleVersion { get; }
        /// <summary>Optional exact required version.</summary>
        public string? RequiredVersion { get; }
        /// <summary>Optional maximum allowed version.</summary>
        public string? MaximumVersion { get; }
        /// <summary>Optional module GUID.</summary>
        public string? Guid { get; }
        /// <summary>Creates a new required module entry.</summary>
        public RequiredModule(
            string moduleName,
            string? moduleVersion = null,
            string? requiredVersion = null,
            string? maximumVersion = null,
            string? guid = null)
        {
            ModuleName = moduleName;
            ModuleVersion = moduleVersion;
            RequiredVersion = requiredVersion;
            MaximumVersion = maximumVersion;
            Guid = guid;
        }
    }

    /// <summary>Gets the RequiredModules list from the top-level manifest.</summary>
    public static bool TryGetRequiredModules(string filePath, out RequiredModule[]? modules)
    {
        modules = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;
        foreach (var kv in topHash.KeyValuePairs)
        {
            var key = GetKeyName(kv.Item1);
            if (!string.Equals(key, "RequiredModules", StringComparison.OrdinalIgnoreCase)) continue;
            var list = ExtractRequiredModules(kv.Item2 as Ast);
            if (list != null) { modules = list; return true; }
            return false;
        }
        return false;
    }

    /// <summary>Sets (replaces) RequiredModules at top-level with the specified entries.</summary>
    public static bool TrySetRequiredModules(string filePath, RequiredModule[] modules)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;

        // Build array text respecting simple formatting
        var arrayText = BuildRequiredModulesArrayText(modules);
        foreach (var kv in topHash.KeyValuePairs)
        {
            var key = GetKeyName(kv.Item1);
            if (!string.Equals(key, "RequiredModules", StringComparison.OrdinalIgnoreCase)) continue;
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
        // Insert if missing
        return InsertKeyValue(topHash, content, filePath, "RequiredModules", arrayText);
    }

    /// <summary>Add or update a single RequiredModule by ModuleName. Returns true if modified.</summary>
    public static bool TryUpsertRequiredModule(string filePath, RequiredModule entry)
    {
        if (!TryGetRequiredModules(filePath, out var current) || current == null)
        {
            return TrySetRequiredModules(filePath, new[] { entry });
        }
        var updated = current.ToList();
        var idx = updated.FindIndex(m => string.Equals(m.ModuleName, entry.ModuleName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            updated[idx] = entry;
        else
            updated.Add(entry);
        return TrySetRequiredModules(filePath, updated.ToArray());
    }

    /// <summary>
    /// Sets a top-level hashtable whose values are string arrays (e.g., CommandModuleDependencies).
    /// </summary>
    public static bool TrySetTopLevelHashtableStringArray(
        string filePath,
        string key,
        IReadOnlyDictionary<string, string[]> values)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var items = values ?? new Dictionary<string, string[]>();
        if (items.Count == 0) return false;

        var content = File.ReadAllText(filePath);
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var topHash = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (topHash == null) return false;

        var ordered = items
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ordered.Length == 0) return false;

        var sb = new StringBuilder();
        sb.Append("@{").Append(NewLine);
        var hasEntries = false;
        foreach (var kvp in ordered)
        {
            var cmds = (kvp.Value ?? Array.Empty<string>())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => EscapeAndQuote(c.Trim()))
                .ToArray();
            if (cmds.Length == 0) continue;

            var list = "@(" + string.Join(", ", cmds) + ")";
            sb.Append("    ").Append(EscapeAndQuote(kvp.Key.Trim())).Append(" = ").Append(list).Append(NewLine);
            hasEntries = true;
        }
        sb.Append("}");

        if (!hasEntries) return false;

        var valueExpression = sb.ToString();
        foreach (var kv in topHash.KeyValuePairs)
        {
            var keyName = GetKeyName(kv.Item1);
            if (!string.Equals(keyName, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Item2; if (v == null) break;
            var start = v.Extent.StartOffset; var end = v.Extent.EndOffset;
            var newContent = content.Substring(0, start) + valueExpression + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
                return true;
            }
            return false;
        }

        return InsertKeyValue(topHash, content, filePath, key, valueExpression);
    }

    /// <summary>Removes a RequiredModule entry by ModuleName. Returns true if modified.</summary>
    public static bool TryRemoveRequiredModule(string filePath, string moduleName)
    {
        if (!TryGetRequiredModules(filePath, out var current) || current == null) return false;
        var updated = current.Where(m => !string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (updated.Length == current.Length) return false;
        return TrySetRequiredModules(filePath, updated);
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
                var list = new System.Collections.Generic.List<string>();
                var hasNonString = false;

                foreach (var st in sb.Statements)
                {
                    var inner = AsExpression(st);
                    ExtractStringArrayItem(inner, list, ref hasNonString);
                }

                if (hasNonString) return null;
                return list.ToArray();
            }
        }
        else if (e2 is ArrayLiteralAst al2)
        {
            var list = new System.Collections.Generic.List<string>();
            var hasNonString = false;
            foreach (var e in al2.Elements)
                ExtractStringArrayItem(e, list, ref hasNonString);
            if (hasNonString) return null;
            return list.ToArray();
        }
        return null;
    }

    private static void ExtractStringArrayItem(ExpressionAst? expr, System.Collections.Generic.List<string> list, ref bool hasNonString)
    {
        if (expr is null)
        {
            hasNonString = true;
            return;
        }

        switch (expr)
        {
            case StringConstantExpressionAst s:
                list.Add(s.Value);
                return;
            case ConstantExpressionAst c when c.Value is string str:
                list.Add(str);
                return;
            case ArrayLiteralAst al:
                foreach (var e in al.Elements)
                    ExtractStringArrayItem(e, list, ref hasNonString);
                return;
            case ArrayExpressionAst ae when ae.SubExpression is StatementBlockAst sb:
                foreach (var st in sb.Statements)
                    ExtractStringArrayItem(AsExpression(st), list, ref hasNonString);
                return;
            default:
                hasNonString = true;
                return;
        }
    }

    private static RequiredModule[]? ExtractRequiredModules(Ast? expr)    
    {
        if (expr == null) return null;
        var e2 = AsExpression(expr);
        var list = new System.Collections.Generic.List<RequiredModule>(); 
        if (e2 is ArrayExpressionAst ae)
        {
            if (ae.SubExpression is StatementBlockAst sb)
            {
                foreach (var st in sb.Statements)
                {
                    var itemExpr = AsExpression(st);
                    if (itemExpr is ArrayLiteralAst al)
                    {
                        foreach (var el in al.Elements)
                        {
                            var mod2 = ParseRequiredModuleItem(el);
                            if (mod2 != null) list.Add(mod2);
                        }
                        continue;
                    }
                    var mod = ParseRequiredModuleItem(itemExpr);
                    if (mod != null) list.Add(mod);
                }
            }
        }
        else if (e2 is ArrayLiteralAst al2)
        {
            foreach (var el in al2.Elements)
            {
                var mod = ParseRequiredModuleItem(el);
                if (mod != null) list.Add(mod);
            }
        }
        else
        {
            var mod = ParseRequiredModuleItem(e2);
            if (mod != null) list.Add(mod);
        }
        return list.Count > 0 ? list.ToArray() : Array.Empty<RequiredModule>();
    }

    private static RequiredModule? ParseRequiredModuleItem(ExpressionAst? expr)
    {
        if (expr == null) return null;
        if (expr is StringConstantExpressionAst s) return new RequiredModule(s.Value);
        if (expr is ConstantExpressionAst c && c.Value is string raw) return new RequiredModule(raw);
        if (expr is HashtableAst h)
        {
            string? name = null, version = null, requiredVersion = null, maximumVersion = null, guid = null;
            foreach (var kv in h.KeyValuePairs)
            {
                var key = GetKeyName(kv.Item1);
                var val = AsExpression(kv.Item2);
                if (string.Equals(key, "ModuleName", StringComparison.OrdinalIgnoreCase))
                {
                    if (val is StringConstantExpressionAst sv) name = sv.Value; else if (val is ConstantExpressionAst cv && cv.Value is string vs) name = vs;
                }
                else if (string.Equals(key, "ModuleVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (val is StringConstantExpressionAst vv) version = vv.Value; else if (val is ConstantExpressionAst vc && vc.Value is string vss) version = vss;
                }
                else if (string.Equals(key, "RequiredVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (val is StringConstantExpressionAst rv) requiredVersion = rv.Value; else if (val is ConstantExpressionAst rc && rc.Value is string rss) requiredVersion = rss;
                }
                else if (string.Equals(key, "MaximumVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (val is StringConstantExpressionAst mv) maximumVersion = mv.Value; else if (val is ConstantExpressionAst mc && mc.Value is string mss) maximumVersion = mss;
                }
                else if (string.Equals(key, "Guid", StringComparison.OrdinalIgnoreCase))
                {
                    if (val is StringConstantExpressionAst gv) guid = gv.Value; else if (val is ConstantExpressionAst gc && gc.Value is string gss) guid = gss;
                }
            }
            if (!string.IsNullOrWhiteSpace(name)) return new RequiredModule(name!, version, requiredVersion, maximumVersion, guid);
        }
        return null;
    }

    private static string BuildRequiredModulesArrayText(RequiredModule[] modules)
    {
        // Keep compact, on one line per entry for readability
        var items = modules.Select(m =>
        {
            if (m.ModuleVersion == null && m.RequiredVersion == null && m.MaximumVersion == null && m.Guid == null)
                return EscapeAndQuote(m.ModuleName);
            var parts = new System.Collections.Generic.List<string> { $"ModuleName = {EscapeAndQuote(m.ModuleName)}" };
            if (!string.IsNullOrWhiteSpace(m.ModuleVersion)) parts.Add($"ModuleVersion = {EscapeAndQuote(m.ModuleVersion!)}");
            if (!string.IsNullOrWhiteSpace(m.RequiredVersion)) parts.Add($"RequiredVersion = {EscapeAndQuote(m.RequiredVersion!)}");
            if (!string.IsNullOrWhiteSpace(m.MaximumVersion)) parts.Add($"MaximumVersion = {EscapeAndQuote(m.MaximumVersion!)}");
            if (!string.IsNullOrWhiteSpace(m.Guid)) parts.Add($"Guid = {EscapeAndQuote(m.Guid!)}");
            return "@{ " + string.Join("; ", parts) + " }";
        });
        return "@(" + string.Join(", ", items) + ")";
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

    /// <summary>Sets a PSData string value under PrivateData.PSData, creating missing hashtables as needed.</summary>
    public static bool TrySetPsDataString(string filePath, string key, string value)
        => TrySetPsDataValue(filePath, key, EscapeAndQuote(value ?? string.Empty));

    /// <summary>Sets a PSData string array value under PrivateData.PSData.</summary>
    public static bool TrySetPsDataStringArray(string filePath, string key, string[] values)
        => TrySetPsDataValue(filePath, key, "@(" + string.Join(", ", (values ?? Array.Empty<string>()).Select(EscapeAndQuote)) + ")");

    /// <summary>Sets a PSData boolean value under PrivateData.PSData.</summary>
    public static bool TrySetPsDataBool(string filePath, string key, bool value)
        => TrySetPsDataValue(filePath, key, value ? "$true" : "$false");

    /// <summary>Sets a PSData sub-hashtable string (e.g., Repository.Branch).</summary>
    public static bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value)
        => TrySetPsDataSubValue(filePath, parentKey, key, EscapeAndQuote(value ?? string.Empty));

    /// <summary>Sets a PSData sub-hashtable string array (e.g., Repository.Paths).</summary>
    public static bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values)
        => TrySetPsDataSubValue(filePath, parentKey, key, "@(" + string.Join(", ", (values ?? Array.Empty<string>()).Select(EscapeAndQuote)) + ")");

    /// <summary>Sets a PSData sub-hashtable boolean value (e.g., Delivery.Enable).</summary>
    public static bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value)
        => TrySetPsDataSubValue(filePath, parentKey, key, value ? "$true" : "$false");

    private static bool TrySetPsDataValue(string filePath, string key, string valueExpression)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        if (!TryEnsurePsDataHashtable(filePath, ref content, out var psData)) return false;

        // Try replace existing
        foreach (var kv in psData.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Item2; if (v == null) break;
            var start = v.Extent.StartOffset; var end = v.Extent.EndOffset;
            var newContent = content.Substring(0, start) + valueExpression + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
                return true;
            }
            return false;
        }
        // Insert new key
        return InsertKeyValue(psData, content, filePath, key, valueExpression);
    }

    private static bool TryEnsurePsDataHashtable(string filePath, ref string content, out HashtableAst psData)
    {
        psData = null!;
        Token[] tokens; ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;
        var top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (top == null) return false;

        // Ensure PrivateData exists
        var privateData = FindChildHashtable(top, "PrivateData");
        if (privateData == null)
        {
            if (!InsertKeyValue(top, content, filePath, "PrivateData", "@{ PSData = @{} }")) return false;
            content = File.ReadAllText(filePath);
            ast = Parser.ParseFile(filePath, out tokens, out errors);
            if (errors != null && errors.Length > 0) return false;
            top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
            if (top == null) return false;
            privateData = FindChildHashtable(top, "PrivateData");
        }

        // Ensure PSData exists
        var psDataHash = FindChildHashtable(privateData!, "PSData");
        if (psDataHash == null)
        {
            if (!InsertKeyValue(privateData!, content, filePath, "PSData", "@{}")) return false;
            content = File.ReadAllText(filePath);
            ast = Parser.ParseFile(filePath, out tokens, out errors);
            if (errors != null && errors.Length > 0) return false;
            top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
            privateData = FindChildHashtable(top!, "PrivateData");
            psDataHash = FindChildHashtable(privateData!, "PSData");
        }
        psData = psDataHash!;
        return true;
    }

    private static bool TrySetPsDataSubValue(string filePath, string parentKey, string key, string valueExpression)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        var content = File.ReadAllText(filePath);
        if (!TryEnsurePsDataHashtable(filePath, ref content, out var psDataRoot)) return false;
        // Ensure parent hashtable exists: PrivateData.PSData.<parentKey>
        var ast = Parser.ParseFile(filePath, out _, out _);
        var top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        var privateData = FindChildHashtable(top!, "PrivateData");
        var psdata = FindChildHashtable(privateData!, "PSData");
        var parent = FindChildHashtable(psdata!, parentKey);
        if (parent == null)
        {
            if (!InsertKeyValue(psdata!, content, filePath, parentKey, "@{}")) return false;
            content = File.ReadAllText(filePath);
            ast = Parser.ParseFile(filePath, out _, out _);
            top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
            privateData = FindChildHashtable(top!, "PrivateData");
            psdata = FindChildHashtable(privateData!, "PSData");
            parent = FindChildHashtable(psdata!, parentKey);
            if (parent == null) return false;
        }
        // Replace existing key or insert
        foreach (var kv in parent!.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = kv.Item2; if (v == null) break;
            var start = v.Extent.StartOffset; var end = v.Extent.EndOffset;
            var newContent = content.Substring(0, start) + valueExpression + content.Substring(end);
            if (!string.Equals(content, newContent, StringComparison.Ordinal))
            {
                File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
                return true;
            }
            return false;
        }
        return InsertKeyValue(parent!, content, filePath, key, valueExpression);
    }

    /// <summary>Sets a PSData sub-hashtable array (e.g., Delivery.ImportantLinks = @(@{ Name='..'; Link='..' }, ...)).</summary>
    public static bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, System.Collections.Generic.IEnumerable<System.Collections.Generic.IDictionary<string, string>> items)
    {
        var arr = items?.ToArray() ?? Array.Empty<System.Collections.Generic.IDictionary<string, string>>();
        var parts = arr.Select(dict =>
        {
            var kvs = dict.Select(kv => kv.Key + " = " + EscapeAndQuote(kv.Value ?? string.Empty));
            return "@{ " + string.Join("; ", kvs) + " }";
        });
        var valueExpression = "@(" + string.Join(", ", parts) + ")";
        return TrySetPsDataSubValue(filePath, parentKey, key, valueExpression);
    }

    private static HashtableAst? FindChildHashtable(HashtableAst parent, string key)
    {
        foreach (var kv in parent.KeyValuePairs)
        {
            var k = GetKeyName(kv.Item1);
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                var expr = AsExpression(kv.Item2);
                if (expr is HashtableAst h) return h;
            }
        }
        return null;
    }
}

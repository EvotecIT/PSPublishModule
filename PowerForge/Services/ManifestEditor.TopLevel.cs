using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

public static partial class ManifestEditor
{
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

    /// <summary>Gets required module names that are declared as hashtables without any version fields.</summary>
    public static bool TryGetInvalidRequiredModuleSpecs(string filePath, out string[]? modules)
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
            var list = ExtractInvalidRequiredModules(kv.Item2 as Ast);
            modules = list;
            return true;
        }
        return false;
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

}

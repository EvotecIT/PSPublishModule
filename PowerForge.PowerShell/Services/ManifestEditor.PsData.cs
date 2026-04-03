using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

public static partial class ManifestEditor
{
    /// <summary>Gets a PSData string array value from PrivateData.PSData.</summary>
    public static bool TryGetPsDataStringArray(string filePath, string key, out string[]? values)
    {
        values = null;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;

        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;

        var top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (top == null) return false;

        var privateData = FindChildHashtable(top, "PrivateData");
        if (privateData == null) return false;

        var psData = FindChildHashtable(privateData, "PSData");
        if (psData == null) return false;

        foreach (var kv in psData.KeyValuePairs)
        {
            var name = GetKeyName(kv.Item1);
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase)) continue;

            var parsed = ExtractStringArray(kv.Item2);
            if (parsed != null)
            {
                values = parsed;
                return true;
            }

            var expr = AsExpression(kv.Item2);
            if (expr is StringConstantExpressionAst s)
            {
                values = new[] { s.Value };
                return true;
            }
            if (expr is ConstantExpressionAst c && c.Value is string str)
            {
                values = new[] { str };
                return true;
            }
            return false;
        }

        return false;
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

    /// <summary>Removes a PSData key from PrivateData.PSData.</summary>
    public static bool TryRemovePsDataKey(string filePath, string key)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var content = File.ReadAllText(filePath);
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;

        var top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (top == null) return false;
        var privateData = FindChildHashtable(top, "PrivateData");
        if (privateData == null) return false;
        var psData = FindChildHashtable(privateData, "PSData");
        if (psData == null) return false;

        return RemoveKeyValue(psData, content, filePath, key);
    }

    /// <summary>Sets a PSData sub-hashtable string (e.g., Repository.Branch).</summary>
    public static bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value)
        => TrySetPsDataSubValue(filePath, parentKey, key, EscapeAndQuote(value ?? string.Empty));

    /// <summary>Sets a PSData sub-hashtable string array (e.g., Repository.Paths).</summary>
    public static bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values)
        => TrySetPsDataSubValue(filePath, parentKey, key, "@(" + string.Join(", ", (values ?? Array.Empty<string>()).Select(EscapeAndQuote)) + ")");

    /// <summary>Sets a PSData sub-hashtable boolean value (e.g., Delivery.Enable).</summary>
    public static bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value)
        => TrySetPsDataSubValue(filePath, parentKey, key, value ? "$true" : "$false");

    /// <summary>Removes a key from a PSData sub-hashtable (for example, Repository.Branch).</summary>
    public static bool TryRemovePsDataSubKey(string filePath, string parentKey, string key)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
        if (string.IsNullOrWhiteSpace(parentKey) || string.IsNullOrWhiteSpace(key)) return false;

        var content = File.ReadAllText(filePath);
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(filePath, out tokens, out errors);
        if (errors != null && errors.Length > 0) return false;

        var top = (HashtableAst?)ast.Find(a => a is HashtableAst h && !HasHashtableAncestor(h), true);
        if (top == null) return false;
        var privateData = FindChildHashtable(top, "PrivateData");
        if (privateData == null) return false;
        var psData = FindChildHashtable(privateData, "PSData");
        if (psData == null) return false;
        var parent = FindChildHashtable(psData, parentKey);
        if (parent == null) return false;

        return RemoveKeyValue(parent, content, filePath, key);
    }

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

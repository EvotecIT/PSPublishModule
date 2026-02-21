using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Management.Automation.Language;

namespace PowerForge;

public static partial class ManifestEditor
{
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

    private static string[] ExtractInvalidRequiredModules(Ast? expr)
    {
        if (expr == null) return Array.Empty<string>();
        var e2 = AsExpression(expr);
        var invalid = new List<string>();

        foreach (var item in EnumerateRequiredModuleItems(e2))
        {
            if (item is not HashtableAst h) continue;

            string? name = null;
            var hasVersion = false;

            foreach (var kv in h.KeyValuePairs)
            {
                var key = GetKeyName(kv.Item1);
                if (string.Equals(key, "ModuleName", StringComparison.OrdinalIgnoreCase))
                {
                    var val = AsExpression(kv.Item2);
                    if (val is StringConstantExpressionAst sv) name = sv.Value;
                    else if (val is ConstantExpressionAst cv && cv.Value is string vs) name = vs;
                }
                else if (string.Equals(key, "ModuleVersion", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "RequiredVersion", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(key, "MaximumVersion", StringComparison.OrdinalIgnoreCase))
                {
                    hasVersion = true;
                }
            }

            if (!hasVersion)
            {
                string entry = string.IsNullOrWhiteSpace(name) ? "<unknown>" : name!;
                if (!invalid.Contains(entry, StringComparer.OrdinalIgnoreCase))
                    invalid.Add(entry);
            }
        }

        return invalid.ToArray();
    }

    private static IEnumerable<ExpressionAst> EnumerateRequiredModuleItems(ExpressionAst? expr)
    {
        if (expr == null) yield break;

        if (expr is ArrayExpressionAst ae && ae.SubExpression is StatementBlockAst sb)
        {
            foreach (var st in sb.Statements)
            {
                var itemExpr = AsExpression(st);
                foreach (var item in EnumerateRequiredModuleItems(itemExpr))
                    yield return item;
            }
            yield break;
        }

        if (expr is ArrayLiteralAst al)
        {
            foreach (var el in al.Elements)
            {
                foreach (var item in EnumerateRequiredModuleItems(el))
                    yield return item;
            }
            yield break;
        }

        yield return expr;
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
        if (modules == null || modules.Length == 0)
            return "@()";

        var order = new[] { "Guid", "ModuleName", "ModuleVersion", "RequiredVersion", "MaximumVersion" };
        var width = order.Max(k => k.Length);
        var sb = new StringBuilder();
        sb.Append("@(");

        var wroteAny = false;

        for (int i = 0; i < modules.Length; i++)
        {
            var m = modules[i];
            if (m is null || string.IsNullOrWhiteSpace(m.ModuleName)) continue;

            var hasVersion = !string.IsNullOrWhiteSpace(m.ModuleVersion) ||
                             !string.IsNullOrWhiteSpace(m.RequiredVersion) ||
                             !string.IsNullOrWhiteSpace(m.MaximumVersion);

            if (!hasVersion)
            {
                if (wroteAny) sb.Append(", ");
                sb.Append(EscapeAndQuote(m.ModuleName));
                wroteAny = true;
                continue;
            }

            if (wroteAny) sb.Append(", ");
            sb.Append("@{");
            sb.Append(NewLine);

            void AppendLine(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                sb.Append("            ")
                  .Append(key)
                  .Append(new string(' ', width - key.Length))
                  .Append(" = ")
                  .Append(EscapeAndQuote(value!))
                  .Append(NewLine);
            }

            AppendLine("Guid", m.Guid);
            AppendLine("ModuleName", m.ModuleName);
            AppendLine("ModuleVersion", m.ModuleVersion);
            AppendLine("RequiredVersion", m.RequiredVersion);
            AppendLine("MaximumVersion", m.MaximumVersion);

            sb.Append("        }");
            wroteAny = true;
        }

        if (!wroteAny)
            return "@()";

        sb.Append(")");
        return sb.ToString();
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

    private static bool RemoveKeyValue(HashtableAst hash, string content, string filePath, string key)
    {
        foreach (var kv in hash.KeyValuePairs)
        {
            var keyName = GetKeyName(kv.Item1);
            if (!string.Equals(keyName, key, StringComparison.OrdinalIgnoreCase)) continue;

            var start = kv.Item1.Extent.StartOffset;
            var end = kv.Item2.Extent.EndOffset;
            if (start < 0 || end <= start || end > content.Length) return false;

            // Remove the whole line containing the key and include trailing newline when present.
            var removeStart = start;
            while (removeStart > 0)
            {
                var ch = content[removeStart - 1];
                if (ch == '\r' || ch == '\n') break;
                removeStart--;
            }

            var removeEnd = end;
            while (removeEnd < content.Length)
            {
                var ch = content[removeEnd];
                if (ch == '\r' || ch == '\n') break;
                removeEnd++;
            }
            if (removeEnd < content.Length && content[removeEnd] == '\r') removeEnd++;
            if (removeEnd < content.Length && content[removeEnd] == '\n') removeEnd++;

            var newContent = content.Remove(removeStart, removeEnd - removeStart);
            if (string.Equals(content, newContent, StringComparison.Ordinal)) return false;

            File.WriteAllText(filePath, newContent, new UTF8Encoding(true));
            return true;
        }

        return false;
    }

}

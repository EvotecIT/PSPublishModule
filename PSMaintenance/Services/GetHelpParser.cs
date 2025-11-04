using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PSMaintenance;

/// <summary>
/// Parses PowerShell Get-Help output into a structured model (syntax, parameters, examples)
/// and optionally extracts raw EXAMPLES text for verbatim rendering. Optimized for PS5.1+.
/// </summary>
internal sealed partial class GetHelpParser
{
    public CommandHelpModel? Parse(string commandName, int timeoutSeconds = 5, ExamplesMode examplesMode = ExamplesMode.Auto)
    {
        using var ps = PowerShell.Create();
        ps.AddScript($"Get-Help -Name '{commandName}' -Full -ErrorAction SilentlyContinue");
        var async = ps.BeginInvoke();
        if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
        {
            try { ps.Stop(); } catch { }
            return null;
        }
        var help = ps.EndInvoke(async).FirstOrDefault() as PSObject;
        if (help == null) return null;

        var model = new CommandHelpModel
        {
            Name = GetString(help, "Name") ?? commandName,
            Synopsis = GetSynopsis(help) ?? string.Empty,
            Description = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(help, "Description"))
        };

        // Syntax sets
        foreach (var s in GetArray(help, "Syntax", "SyntaxItem"))
        {
            var syntax = new SyntaxSet
            {
                Name = GetString(s, "Name") ?? commandName
            };
            foreach (var p in GetArray(s, "Parameter"))
            {
                var ph = new ParameterHelp
                {
                    Name = GetString(p, "Name") ?? string.Empty,
                    Type = Get(p, "Type", t => GetString(t, "Name") ?? string.Empty) ?? string.Empty,
                    Position = GetString(p, "Position"),
                    Required = GetBool(p, "Required"),
                    PipelineInput = GetString(p, "PipelineInput"),
                    Globbing = GetBool(p, "Globbing"),
                    DefaultValue = GetString(p, "DefaultValue")
                };
                var aliases = GetArray(p, "Aliases").Select(a => SafeToString(a)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
                if (aliases.Count > 0) ph.Aliases = aliases;
                syntax.Parameters.Add(ph);
            }
            model.Syntax.Add(syntax);
        }

        // Parameters (detailed docs)
        foreach (var p in GetArray(help, "Parameters", "Parameter"))
        {
            var ph = new ParameterHelp
            {
                Name = GetString(p, "Name") ?? string.Empty,
                Type = Get(p, "Type", t => GetString(t, "Name") ?? string.Empty) ?? string.Empty,
                Description = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(p, "Description")),
                Required = GetBool(p, "Required"),
                Position = GetString(p, "Position"),
                PipelineInput = GetString(p, "PipelineInput"),
                Globbing = GetBool(p, "Globbing"),
                DefaultValue = GetString(p, "DefaultValue")
            };
            var aliases = GetArray(p, "Aliases").Select(a => SafeToString(a)).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
            if (aliases.Count > 0) ph.Aliases = aliases;
            model.Parameters.Add(ph);
        }

        // Choose examples according to requested mode
        var rawExamples = (examplesMode == ExamplesMode.Maml) ? null : TryExtractRawExamples(commandName, timeoutSeconds);
        if (examplesMode == ExamplesMode.Raw && (rawExamples == null || rawExamples.Count == 0))
        {
            // Forced Raw but none found: leave empty; caller will still have rest of help
        }
        else if (examplesMode == ExamplesMode.Raw && rawExamples != null)
        {
            model.Examples.AddRange(rawExamples);
        }
        else if (examplesMode == ExamplesMode.Auto && rawExamples != null && rawExamples.Count > 0)
        {
            model.Examples.AddRange(rawExamples);
        }
        else
        {
            // Fall back to MAML examples
            foreach (var ex in GetArray(help, "Examples", "Example"))
            {
                var title      = (GetString(ex, "Title") ?? string.Empty).Trim();
                var codeRaw    = (GetString(ex, "Code") ?? string.Empty) ?? string.Empty;
                var remarksRaw = string.Join("\n\n", GetParagraphs(ex, "Remarks"));

                var exItem = new ExampleHelp
                {
                    Title = title,
                    Code = (codeRaw ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n', '\r'),
                    Remarks = string.IsNullOrWhiteSpace(remarksRaw) ? string.Empty : remarksRaw.Replace("\r\n", "\n").TrimEnd('\n', '\r'),
                    Mode = "structured:maml"
                };
                exItem.CodeLines = string.IsNullOrEmpty(exItem.Code) ? 0 : exItem.Code.Split(new[]{'\n'}, StringSplitOptions.None).Length;
                exItem.RemarksLines = string.IsNullOrEmpty(exItem.Remarks) ? 0 : exItem.Remarks.Split(new[]{'\n'}, StringSplitOptions.None).Length;
                model.Examples.Add(exItem);
            }
        }

        // Inputs
        foreach (var it in GetArray(help, "InputTypes", "InputType"))
        {
            var type = ResolveTypeName(it) ?? Get(it, "Type", t => GetString(t, "Name"));
            var desc = string.Join(" ", GetParagraphs(it, "Description"));
            if (!string.IsNullOrWhiteSpace(type)) model.Inputs.Add(new TypeHelp { TypeName = type!.Trim(), Description = string.IsNullOrWhiteSpace(desc) ? null : desc });
        }

        // Outputs
        foreach (var ot in GetArray(help, "ReturnValues", "ReturnValue"))
        {
            var type = ResolveTypeName(ot) ?? Get(ot, "Type", t => GetString(t, "Name"));
            var desc = string.Join(" ", GetParagraphs(ot, "Description"));
            if (!string.IsNullOrWhiteSpace(type)) model.Outputs.Add(new TypeHelp { TypeName = type!.Trim(), Description = string.IsNullOrWhiteSpace(desc) ? null : desc });
        }

        // Notes
        var notes = string.Join(Environment.NewLine + Environment.NewLine, GetParagraphs(help, "AlertSet").Concat(GetParagraphs(help, "Notes")));
        model.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

        // Related links
        foreach (var link in GetArray(help, "RelatedLinks", "NavigationLink"))
        {
            var text = GetString(link, "LinkText") ?? string.Empty;
            var uri = GetString(link, "Uri") ?? string.Empty;
            if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(uri)) model.RelatedLinks.Add(new RelatedLink { Title = text, Uri = uri });
        }

        return model;
    }

    // Try to detect trailing narrative that accidentally landed inside Code and split it out to Remarks
    private bool TrySplitTrailingNarrativeFromCodeWithAst(string codeRaw, out string codeOnly, out string trailingAsRemarks)
    {
        codeOnly = codeRaw ?? string.Empty; trailingAsRemarks = string.Empty;
        if (string.IsNullOrWhiteSpace(codeOnly)) return true;
        try
        {
            var text = codeOnly.Replace("\r\n", "\n");
            var lines = text.Split(new[] {'\n'}, StringSplitOptions.None);
            var isCode = new bool[lines.Length];

            bool LooksLikeCode(string l)
            {
                var t = (l ?? string.Empty).TrimStart();
                if (t.Length == 0) return false; // blank handled by run logic below
                if (t.StartsWith("PS>") || t.StartsWith("PS ") || t.StartsWith("C:\\")) return true;
                if (t.StartsWith("#")) return true;
                if (t.StartsWith("@{") || t.StartsWith("@(") || t.StartsWith("param(")) return true;
                if (t.StartsWith("function ", StringComparison.OrdinalIgnoreCase) || t.StartsWith("if (", StringComparison.OrdinalIgnoreCase)) return true;
                if (t.StartsWith("$") || t.StartsWith("[")) return true;
                // Verb-Noun at line start
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z]+-[A-Za-z0-9]+(\s|$)")) return true;
                // Parameter style anywhere: space dash param
                if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\s-[A-Za-z]")) return true;
                return false;
            }
            for (int i=0;i<lines.Length;i++) isCode[i] = LooksLikeCode(lines[i]);

            // find last code line index (ignore trailing blanks)
            int lastCode = -1;
            for (int i=0;i<lines.Length;i++) if (isCode[i]) lastCode = i;
            if (lastCode < 0) { trailingAsRemarks = codeOnly.Trim(); codeOnly = string.Empty; return true; }

            // But allow trailing blank lines after last code
            int splitAt = lastCode + 1;
            // If everything after splitAt are blanks → no trailing narrative
            bool hasTrailingText = false;
            for (int i = splitAt; i < lines.Length; i++) { if (!string.IsNullOrWhiteSpace(lines[i])) { hasTrailingText = true; break; } }
            if (!hasTrailingText) { codeOnly = text.TrimEnd(); trailingAsRemarks = string.Empty; return true; }

            codeOnly = string.Join("\n", lines.Take(splitAt)).TrimEnd();
            trailingAsRemarks = string.Join("\n", lines.Skip(splitAt)).Trim();
            return true;
        }
        catch { return false; }
    }

    // Try to move the leading remarks lines that are actual code (by AST/token inspection) back into the Code block
    private bool TryReclassifyLeadingCodeWithAst(string codeRaw, string remarksRaw, out string codeOut, out string remarksOut)
    {
        codeOut = codeRaw ?? string.Empty; remarksOut = remarksRaw ?? string.Empty;
        if (string.IsNullOrEmpty(remarksOut)) { codeOut = codeOut.TrimEnd(); remarksOut = string.Empty; return true; }
        try
        {
            var text = remarksOut.Replace("\r\n", "\n");
            Token[] tokens; ParseError[] errors; var ast = Parser.ParseInput(text, out tokens, out errors);
            var lines = text.Split(new[] {'\n'}, StringSplitOptions.None);
            var isCode = new bool[lines.Length];

            Func<Ast, bool> selector = a =>
                a is PipelineAst || a is CommandAst || a is CommandExpressionAst ||
                a is AssignmentStatementAst || a is HashtableAst || a is ArrayLiteralAst || a is ArrayExpressionAst ||
                a is ScriptBlockAst || a is FunctionDefinitionAst || a is IfStatementAst || a is ForEachStatementAst || a is ForStatementAst ||
                a is WhileStatementAst || a is DoWhileStatementAst || a is DoUntilStatementAst || a is TryStatementAst || a is SwitchStatementAst ||
                a is TrapStatementAst || a is ReturnStatementAst || a is ThrowStatementAst || a is BreakStatementAst || a is ContinueStatementAst;

            foreach (var node in ast.FindAll(selector, true))
            {
                var start = Math.Max(1, node.Extent.StartLineNumber) - 1;
                var end   = Math.Max(start, node.Extent.EndLineNumber   - 1);
                for (int i = start; i <= end && i < isCode.Length; i++) isCode[i] = true;
            }
            // Consume contiguous leading lines that are code (blank lines included while in a code run)
            var prefix = new System.Text.StringBuilder();
            int idx = 0; bool lastWasCode = false;
            while (idx < lines.Length)
            {
                var l = lines[idx];
                if (isCode[idx] || (string.IsNullOrWhiteSpace(l) && lastWasCode))
                {
                    prefix.AppendLine(l); idx++; lastWasCode = true; continue;
                }
                break;
            }
            if (prefix.Length > 0)
            {
                var codePartRaw = prefix.ToString().TrimEnd();
                // If reclassified lines lost indentation (common in MAML Remarks), re-indent them
                var codeLines = (codeOut ?? string.Empty).Replace("\r\n","\n").Split(new[]{'\n'}, StringSplitOptions.None);
                string indent = string.Empty;
                for (int i = codeLines.Length - 1; i >= 0; i--)
                {
                    var ln = codeLines[i];
                    if (string.IsNullOrWhiteSpace(ln)) continue;
                    indent = new string(ln.TakeWhile(ch => ch == ' ' || ch == '\t').ToArray());
                    break;
                }
                if (!string.IsNullOrEmpty(indent))
                {
                    var plines = codePartRaw.Replace("\r\n","\n").Split(new[]{'\n'}, StringSplitOptions.None);
                    for (int i=0;i<plines.Length;i++)
                    {
                        if (plines[i].Length == 0) continue;
                        // Only indent lines that don't already start with whitespace
                        if (!(plines[i].Length > 0 && (plines[i][0] == ' ' || plines[i][0] == '\t')))
                            plines[i] = indent + plines[i];
                    }
                    codePartRaw = string.Join("\n", plines);
                }
                codeOut = string.IsNullOrEmpty(codeOut) ? codePartRaw : ((codeOut ?? string.Empty).TrimEnd() + "\n\n" + codePartRaw);
                remarksOut = string.Join("\n", lines.Skip(idx)).TrimStart('\n');
            }
            else
            {
                codeOut = codeOut.TrimEnd();
                remarksOut = remarksOut.TrimStart('\n');
            }
            return true;
        }
        catch { return false; }
    }

    // Heuristic reclassification: treat leading lines in remarks as code continuation when they look like code
    private static void ReclassifyExample(string codeRaw, string remarksRaw, out string codeOut, out string remarksOut)
    {
        var code = (codeRaw ?? string.Empty).Replace("\r\n", "\n");
        var remarks = (remarksRaw ?? string.Empty).Replace("\r\n", "\n");

        if (string.IsNullOrWhiteSpace(remarks))
        {
            codeOut = code.TrimEnd();
            remarksOut = string.Empty;
            return;
        }

        var lines = remarks.Split(new[] {'\n'}, StringSplitOptions.None).ToList();
        var prefix = new System.Text.StringBuilder();
        int idx = 0;
        int curly = 0, paren = 0;
        bool inDQuote = false, inSQuote = false; // simple string tracking
        bool inHereD = false, inHereS = false;   // here-strings @"..."@ or @'...'@

        // Helper local funcs
        bool IsLikelyCode(string line)
        {
            var t = (line ?? string.Empty);
            var trimmed = t.TrimStart();
            if (trimmed.Length == 0) return true; // blank lines allowed within code blocks
            if (trimmed.StartsWith("PS ") || trimmed.StartsWith("PS>") || trimmed.StartsWith("C:\\")) return true;
            if (trimmed.StartsWith("#")) return true; // comments in code
            if (trimmed.StartsWith("@{") || trimmed.StartsWith("@(") || trimmed.StartsWith("param(")) return true;
            if (trimmed.StartsWith("function ") || trimmed.StartsWith("If(" , StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("if (")) return true;
            if (trimmed.StartsWith("$")) return true;
            if (trimmed.Contains(" = ") || trimmed.Contains(" -") || trimmed.Contains("`")) return true;
            if (trimmed.StartsWith("Email", StringComparison.OrdinalIgnoreCase)) return true; // common DSL in examples
            return false;
        }

        string NormalizeForDepth(string l)
        {
            // naive depth tracking; ignore braces inside quotes/here-strings
            for (int i = 0; i < l.Length; i++)
            {
                var ch = l[i];
                if (!inHereD && !inHereS)
                {
                    if (ch == '"' && !inSQuote) inDQuote = !inDQuote;
                    else if (ch == '\'' && !inDQuote) inSQuote = !inSQuote;
                    if (!inDQuote && !inSQuote)
                    {
                        if (ch == '{') curly++;
                        else if (ch == '}') curly = Math.Max(0, curly - 1);
                        else if (ch == '(') paren++;
                        else if (ch == ')') paren = Math.Max(0, paren - 1);
                    }
                    // here-strings start
                    if (i + 1 < l.Length && l[i] == '@' && (l[i+1] == '"' || l[i+1] == '\''))
                    {
                        if (l[i+1] == '"' && !inSQuote) { inHereD = true; i++; }
                        else if (l[i+1] == '\'' && !inDQuote) { inHereS = true; i++; }
                    }
                }
                else
                {
                    // end here-string when "@ or '@ appears at line start (PowerShell rule)
                    var s = l.TrimStart();
                    if (inHereD && s.StartsWith("\"@")) inHereD = false;
                    if (inHereS && s.StartsWith("'@")) inHereS = false;
                }
            }
            return l;
        }

        while (idx < lines.Count)
        {
            var line = lines[idx];
            NormalizeForDepth(line);
            if (IsLikelyCode(line) || inHereD || inHereS || curly > 0 || paren > 0)
            {
                prefix.AppendLine(line);
                idx++;
                continue;
            }
            // First narrative-looking line at zero depth ends the code continuation
            break;
        }

        if (prefix.Length > 0)
        {
            // Merge original code with code-like remarks prefix; preserve a blank line between when appropriate
            if (!string.IsNullOrEmpty(code))
                codeOut = (code.TrimEnd() + "\n\n" + prefix.ToString().TrimEnd());
            else
                codeOut = prefix.ToString().TrimEnd();

            remarksOut = string.Join("\n", lines.Skip(idx)).TrimStart('\n');
        }
        else
        {
            codeOut = code.TrimEnd();
            remarksOut = remarks.TrimStart('\n');
        }
    }

    // Helpers
    private static string? GetString(PSObject obj, string name)
        => obj.Properties[name]?.Value?.ToString();

    private static string? GetSynopsis(PSObject help)
    {
        var v = help.Properties["Synopsis"]?.Value;
        if (v is string s) return s;
        if (v is PSObject p)
        {
            var t = p.Properties["Text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(t)) return t;
            var tt = p.Properties["#text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(tt)) return tt;
            // Do not enumerate arbitrary properties here to avoid picking up syntax
        }
        return v?.ToString();
    }

    private static bool? GetBool(PSObject obj, string name)
    {
        var v = obj.Properties[name]?.Value;
        if (v is bool b) return b;
        if (v == null) return null;
        if (bool.TryParse(v.ToString(), out var bb)) return bb;
        return null;
    }

    private static T? Get<T>(PSObject obj, string name, Func<PSObject, T?> map)
    {
        var v = obj.Properties[name]?.Value as PSObject;
        if (v == null) return default;
        return map(v);
    }

    private static IEnumerable<string> GetParagraphs(PSObject obj, string name)
    {
        var v = obj.Properties[name]?.Value;
        foreach (var s in ExtractTextList(v))
        {
            var trimmed = s?.Trim();
            if (!string.IsNullOrEmpty(trimmed)) yield return trimmed!;
        }
    }

    private static IEnumerable<PSObject> GetArray(PSObject obj, string containerName, string? itemName = null)
    {
        var v = obj.Properties[containerName]?.Value;
        if (v is PSObject p)
        {
            if (!string.IsNullOrEmpty(itemName))
            {
                var inner = p.Properties[itemName]?.Value;
                foreach (var o in Flatten(inner)) yield return o;
            }
            else
            {
                foreach (var o in Flatten(p.BaseObject)) yield return o;
            }
        }
        else
        {
            foreach (var o in Flatten(v)) yield return o;
        }
    }

    private static IEnumerable<PSObject> Flatten(object? value)
    {
        if (value == null) yield break;
        if (value is PSObject po) { yield return po; yield break; }
        if (value is IEnumerable<object> e && value is not string)
        {
            foreach (var item in e)
            {
                if (item is PSObject px) yield return px; else if (item != null) yield return new PSObject(item);
            }
            yield break;
        }
        yield return new PSObject(value);
    }

    private static string? ResolveTypeName(PSObject obj)
    {
        // Try common shapes under a container (e.g., ReturnValue, InputType)
        var typeProp = obj.Properties["Type"]?.Value;
        if (typeProp is PSObject tp)
        {
            var name = tp.Properties["Name"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            var text = tp.Properties["Text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(text)) return text;
            var t2 = tp.Properties["#text"]?.Value as string;
            if (!string.IsNullOrWhiteSpace(t2)) return t2;
            if (tp.BaseObject is string s0) return s0;
        }
        if (typeProp is string s1 && !string.IsNullOrWhiteSpace(s1)) return s1;

        // Or the object itself is a type descriptor
        var directName = obj.Properties["Name"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directName)) return directName;
        var directText = obj.Properties["Text"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directText)) return directText;
        var directT = obj.Properties["#text"]?.Value as string;
        if (!string.IsNullOrWhiteSpace(directT)) return directT;
        if (obj.BaseObject is string s) return s;
        return null;
    }

    private static IEnumerable<string> ExtractTextList(object? v)
    {
        if (v == null) yield break;
        if (v is string s) { yield return s; yield break; }
        if (v is PSObject p)
        {
            if (p.BaseObject is string s2) { yield return s2; yield break; }
            var textProp = p.Properties["Text"]?.Value;
            if (textProp != null)
            {
                foreach (var t in ExtractTextList(textProp)) yield return t;
            }
            var paraProp = p.Properties["para"]?.Value;
            if (paraProp != null)
            {
                foreach (var t in ExtractTextList(paraProp)) yield return t;
            }
            var innerText = p.Properties["#text"]?.Value;
            if (innerText != null)
            {
                foreach (var t in ExtractTextList(innerText)) yield return t;
            }
            if (textProp == null && paraProp == null && innerText == null)
            {
                foreach (var prop in p.Properties)
                {
                    if (prop?.Value == null) continue;
                    foreach (var t in ExtractTextList(prop.Value)) yield return t;
                }
            }
            yield break;
        }
        if (v is IEnumerable<object> en && v is not string)
        {
            foreach (var item in en)
            {
                foreach (var t in ExtractTextList(item)) yield return t;
            }
            yield break;
        }
        yield return v.ToString() ?? string.Empty;
    }

    private static string? SafeToString(PSObject? po)
    {
        if (po == null) return null;
        var v = po.BaseObject;
        if (v is string s) return s;
        var name = (po.Properties["Name"]?.Value ?? po.Properties["Text"]?.Value ?? po.Properties["#text"]?.Value)?.ToString();
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return po.ToString();
    }
}

// Helper methods for recovering examples from source files to preserve exact formatting
internal sealed partial class GetHelpParser
{
    private List<ExampleHelp>? TryExtractRawExamples(string commandName, int timeoutSeconds)
    {
        try
        {
            using var ps = PowerShell.Create();
            var script = "$h = Get-Help -Full -Name '" + commandName.Replace("'", "''") + "' -ErrorAction SilentlyContinue; if ($h) { $h | Out-String -Width 1mb } else { '' }";
            ps.AddScript(script);
            var async = ps.BeginInvoke();
            if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
            {
                try { ps.Stop(); } catch { }
                return null;
            }
            var text = string.Join("", ps.EndInvoke(async).Select(o => o?.ToString()));
            if (string.IsNullOrWhiteSpace(text)) return null;

            var nl = text.Replace("\r\n", "\n");
            var lines = nl.Split(new[]{'\n'}, StringSplitOptions.None);
            // Locate EXAMPLES section
            int exStart = -1; int exEnd = lines.Length;
            for (int i=0;i<lines.Length;i++)
            {
                var t = (lines[i] ?? string.Empty).Trim();
                if (string.Equals(t, "EXAMPLES", StringComparison.OrdinalIgnoreCase)) { exStart = i + 1; break; }
            }
            if (exStart < 0) return null;
            // End at next major header
            var headers = new[] { "INPUTS","OUTPUTS","NOTES","RELATED LINKS","ALIASES","REMARKS","SYNTAX","DESCRIPTION","PARAMETERS" };
            for (int i=exStart;i<lines.Length;i++)
            {
                var t = (lines[i] ?? string.Empty).Trim();
                if (headers.Any(h => string.Equals(t, h, StringComparison.OrdinalIgnoreCase))) { exEnd = i; break; }
            }
            var body = lines.Skip(exStart).Take(Math.Max(0, exEnd - exStart)).ToList();
            if (body.Count == 0) return null;

            // Split by EXAMPLE headers
            var idxs = new System.Collections.Generic.List<int>();
            for (int i=0;i<body.Count;i++)
            {
                var t = (body[i] ?? string.Empty).Trim();
                if (t.StartsWith("EXAMPLE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Example", StringComparison.OrdinalIgnoreCase)) idxs.Add(i);
            }
            if (idxs.Count == 0) { idxs.Add(0); }
            idxs.Add(body.Count);

            var results = new List<ExampleHelp>(); int n = 1;
            for (int k=0;k<idxs.Count-1;k++)
            {
                int s = idxs[k]; int e = idxs[k+1];
                var chunk = body.Skip(s).Take(e - s).ToList();
                if (chunk.Count == 0) continue;
                var title = (chunk.FirstOrDefault() ?? string.Empty).Trim();
                // remove header line if it looks like EXAMPLE heading
                if (title.StartsWith("EXAMPLE", StringComparison.OrdinalIgnoreCase) || title.StartsWith("Example", StringComparison.OrdinalIgnoreCase))
                {
                    chunk = chunk.Skip(1).ToList();
                }
                var content = string.Join("\n", chunk);
                var ex = new ExampleHelp
                {
                    Title = string.IsNullOrWhiteSpace(title) ? $"Example {n}" : title,
                    Code = content.TrimEnd('\n','\r'),
                    Remarks = string.Empty,
                    Mode = "raw:text"
                };
                ex.CodeLines = string.IsNullOrEmpty(ex.Code) ? 0 : ex.Code.Split(new[]{'\n'}, StringSplitOptions.None).Length;
                ex.RemarksLines = 0;
                results.Add(ex);
                n++;
            }
            return results;
        }
        catch { return null; }
    }
}

internal sealed class CommandHelpModel
{
    public string Name { get; set; } = string.Empty;
    public string Synopsis { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<SyntaxSet> Syntax { get; } = new();
    public List<ParameterHelp> Parameters { get; } = new();
    public List<ExampleHelp> Examples { get; } = new();
    public List<TypeHelp> Inputs { get; } = new();
    public List<TypeHelp> Outputs { get; } = new();
    public string? Notes { get; set; }
    public List<RelatedLink> RelatedLinks { get; } = new();
}

internal sealed class SyntaxSet
{
    public string Name { get; set; } = string.Empty;
    public List<ParameterHelp> Parameters { get; } = new();
}

internal sealed class ParameterHelp
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Position { get; set; }
    public bool? Required { get; set; }
    public string? PipelineInput { get; set; }
    public bool? Globbing { get; set; }
    public string? DefaultValue { get; set; }
    public List<string>? Aliases { get; set; }
}

internal sealed class ExampleHelp
{
    public string Title { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    // Diagnostics for verbose logging
    public string? Mode { get; set; }
    public int CodeLines { get; set; }
    public int RemarksLines { get; set; }
}

internal sealed class TypeHelp
{
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

internal sealed class RelatedLink
{
    public string Title { get; set; } = string.Empty;
    public string? Uri { get; set; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HtmlForgeX;
using HtmlForgeX.Extensions;
using HtmlForgeX.Markdown;
using PowerForge;
using System.Management.Automation.Language;

namespace PSPublishModule;

internal sealed partial class HtmlExporter
{
    // Helpers for Commands tab
    private List<string> ResolveExportedCommands(string moduleName)
    {
        var ps = System.Management.Automation.PowerShell.Create();
        // Filter only functions/cmdlets (exclude aliases)
        var script = @"
            $m = Get-Module -ListAvailable -Name '" + moduleName + @"' | Sort-Object Version -Descending | Select-Object -First 1
            if ($m) { $m.ExportedCommands.Values | Where-Object { $_.CommandType -in 'Function','Cmdlet' } | Select-Object -ExpandProperty Name }
        ";
        ps.AddScript(script);
        var results = ps.Invoke();
        return results.Select(r => r?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;
    }

    private string? GetHelpMarkdown(string moduleName, string command, int timeoutSeconds)
    {
        var ps = System.Management.Automation.PowerShell.Create();
        // Use -Full for richer content; convert basic sections to markdown headings
        var script = $"$h = Get-Help -Full -Name '{command}' -ErrorAction SilentlyContinue; if ($h) {{ $h | Out-String }} else {{ '' }}";
        ps.AddScript(script);
        var async = ps.BeginInvoke();
        if (!async.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))))
        {
            try { ps.Stop(); } catch { }
            return "_Get-Help timed out._";
        }
        var text = string.Join("", ps.EndInvoke(async).Select(p => p?.ToString()));
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return FormatRawHelpMarkdown(text); } catch { }
        // Naive fallback
        var md = text.Replace("SYNTAX", "# Syntax").Replace("DESCRIPTION", "# Description").Replace("PARAMETERS", "# Parameters").Replace("EXAMPLES", "# Examples");
        return md;
    }

    // --- Raw Get-Help text -> Markdown with robust Examples handling ---
    private static readonly string[] RawHelpHeaders = new[] { "NAME", "SYNOPSIS", "SYNTAX", "DESCRIPTION", "PARAMETERS", "EXAMPLES", "INPUTS", "OUTPUTS", "NOTES", "RELATED LINKS" };

    private string FormatRawHelpMarkdown(string raw)
    {
        var nl = (raw ?? string.Empty).Replace("\r\n", "\n");
        var lines = nl.Split(new[] {'\n'}, StringSplitOptions.None);
        var idx = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for (int i=0;i<lines.Length;i++)
        {
            var t = (lines[i] ?? string.Empty).Trim();
            if (Array.IndexOf(RawHelpHeaders, t.ToUpperInvariant()) >= 0 && !idx.ContainsKey(t)) idx[t] = i;
        }
        var sb = new System.Text.StringBuilder(nl.Length + 1024);
        void AppendSection(string h)
        {
            if (!idx.TryGetValue(h, out var start)) return;
            int end = lines.Length;
            foreach (var oh in RawHelpHeaders)
            {
                if (oh.Equals(h, StringComparison.OrdinalIgnoreCase)) continue;
                if (idx.TryGetValue(oh, out var pos) && pos > start && pos < end) end = pos;
            }
            var body = lines.Skip(start + 1).Take(end - start - 1).ToList();
            if (h.Equals("EXAMPLES", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("# Examples");
                AppendExamplesBlock(body, sb);
            }
            else
            {
                sb.AppendLine("# " + ToTitle(h));
                foreach (var l in body) sb.AppendLine(l);
                sb.AppendLine();
            }
        }
        foreach (var h in RawHelpHeaders) AppendSection(h);
        return sb.ToString();
    }

    private static string ToTitle(string s)
        => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase((s ?? string.Empty).ToLowerInvariant());

    private void AppendExamplesBlock(List<string> body, System.Text.StringBuilder sb)
    {
        var boundaries = new List<int>();
        for (int i=0;i<body.Count;i++)
        {
            var t = (body[i] ?? string.Empty).Trim();
            if (t.StartsWith("EXAMPLE", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Example", StringComparison.OrdinalIgnoreCase)) boundaries.Add(i);
        }
        if (boundaries.Count == 0)
        {
            sb.AppendLine("````"); foreach (var l in body) sb.AppendLine(l); sb.AppendLine("````"); return;
        }
        boundaries.Add(body.Count);
        for (int b=0;b<boundaries.Count-1;b++)
        {
            int start = boundaries[b]; int end = boundaries[b+1];
            var chunk = body.Skip(start).Take(end-start).ToList();
            var title = (chunk.FirstOrDefault() ?? string.Empty).Trim();
            var content = string.Join("\n", chunk.Skip(1));
            // Prefer AST-based split using ExampleClassifier; fall back to internal heuristic
            string codeOut, remarksOut, mode;
            var classifier = new ExampleClassifier();
            if (!classifier.Classify(content, out codeOut, out remarksOut, out mode))
            {
                if (!TrySplitExampleWithAst(content, out codeOut, out remarksOut))
                {
                    ReclassifyExample(string.Empty, content, out codeOut, out remarksOut);
                }
            }
            if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine("## " + title);
            var block = string.IsNullOrEmpty(remarksOut) ? codeOut : ((codeOut ?? string.Empty) + "\n\n" + remarksOut);
            sb.AppendLine("```powershell");
            sb.AppendLine((block ?? string.Empty).TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
        }
    }

    // Attempt AST/token-based split of example content into code and remarks.
    // Returns true on success with code/remarks assigned; false to allow caller fallback.
    private bool TrySplitExampleWithAst(string exampleBody, out string codeOut, out string remarksOut)
    {
        codeOut = string.Empty; remarksOut = string.Empty;
        if (exampleBody == null) return false;
        var text = exampleBody.Replace("\r\n", "\n");
        // Quick skip: if it looks empty, bail
        if (string.IsNullOrWhiteSpace(text)) { codeOut = string.Empty; remarksOut = string.Empty; return true; }

        try
        {
            // Parse the entire example body. We tolerate errors and still use tokens/AST extents.
            Token[] tokens; ParseError[] errors;
            var ast = Parser.ParseInput(text, out tokens, out errors);

            var lines = text.Split(new[] { '\n' }, StringSplitOptions.None);
            var isCode = new bool[lines.Length];

            // Mark lines covered by significant AST nodes as code
            // We include statements, pipelines, commands, hashtables, arrays, scriptblocks, assignments, etc.
            Func<Ast, bool> selector = a =>
                a is PipelineAst || a is CommandAst || a is CommandExpressionAst ||
                a is AssignmentStatementAst || a is HashtableAst || a is ArrayLiteralAst || a is ArrayExpressionAst ||
                a is ScriptBlockAst || a is FunctionDefinitionAst || a is IfStatementAst || a is ForEachStatementAst || a is ForStatementAst ||
                a is WhileStatementAst || a is DoWhileStatementAst || a is DoUntilStatementAst || a is TryStatementAst || a is SwitchStatementAst ||
                a is TrapStatementAst || a is ReturnStatementAst || a is ThrowStatementAst || a is BreakStatementAst || a is ContinueStatementAst;

            foreach (var node in ast.FindAll(selector, true))
            {
                var start = Math.Max(1, node.Extent.StartLineNumber) - 1;
                var end = Math.Max(start, node.Extent.EndLineNumber - 1);
                for (int i = start; i <= end && i < isCode.Length; i++) isCode[i] = true;
            }

            // Avoid treating arbitrary tokens as code; rely on AST nodes only to minimize false positives.

            // Build code and remarks while preserving blank lines. Attach blank lines to the active block kind.
            var codeSb = new System.Text.StringBuilder();
            var remSb = new System.Text.StringBuilder();
            bool lastWasCode = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (isCode[i] || (string.IsNullOrWhiteSpace(l) && lastWasCode))
                {
                    codeSb.AppendLine(l);
                    lastWasCode = true;
                }
                else
                {
                    remSb.AppendLine(l);
                    lastWasCode = false;
                }
            }

            codeOut = codeSb.ToString().TrimEnd();
            remarksOut = remSb.ToString().Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Heuristic similar to the structured path: reclassify leading remarks back into code when appropriate
    private void ReclassifyExample(string codeRaw, string remarksRaw, out string codeOut, out string remarksOut)
    {
        var code = (codeRaw ?? string.Empty).Replace("\r\n", "\n");
        var remarks = (remarksRaw ?? string.Empty).Replace("\r\n", "\n");
        if (string.IsNullOrWhiteSpace(remarks)) { codeOut = code.TrimEnd(); remarksOut = string.Empty; return; }
        var lines = remarks.Split(new[] {'\n'}, StringSplitOptions.None).ToList();
        var prefix = new System.Text.StringBuilder();
        int idx = 0; int curly = 0, paren = 0; bool inDQ=false,inSQ=false,inHereD=false,inHereS=false;
        bool IsLikelyCode(string line)
        {
            var t=(line??string.Empty); var trimmed=t.TrimStart();
            if (trimmed.Length==0) return true; if (trimmed.StartsWith("PS ")||trimmed.StartsWith("PS>")||trimmed.StartsWith("C:\\")) return true;
            if (trimmed.StartsWith("#")) return true; if (trimmed.StartsWith("@{")||trimmed.StartsWith("@(")||trimmed.StartsWith("param(")) return true;
            if (trimmed.StartsWith("function ", StringComparison.OrdinalIgnoreCase)||trimmed.StartsWith("if (", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("$")) return true; if (trimmed.Contains(" = ")||trimmed.Contains(" -" )||trimmed.Contains("`")) return true;
            if (trimmed.StartsWith("Email", StringComparison.OrdinalIgnoreCase)) return true; return false;
        }
        void ScanDepth(string l)
        {
            for (int i=0;i<l.Length;i++)
            {
                var ch=l[i];
                if (!inHereD && !inHereS)
                {
                    if (ch=='"' && !inSQ) inDQ=!inDQ; else if (ch=='\'' && !inDQ) inSQ=!inSQ;
                    if (!inDQ && !inSQ) { if (ch=='{') curly++; else if (ch=='}') curly=Math.Max(0,curly-1); else if (ch=='(') paren++; else if (ch==')') paren=Math.Max(0,paren-1);}
                    if (i+1<l.Length && l[i]=='@' && (l[i+1]=='"' || l[i+1]=='\'')) { if (l[i+1]=='"' && !inSQ) { inHereD=true; i++; } else if (l[i+1]=='\'' && !inDQ) { inHereS=true; i++; } }
                }
                else { var s=l.TrimStart(); if (inHereD && s.StartsWith("\"@")) inHereD=false; if (inHereS && s.StartsWith("'@")) inHereS=false; }
            }
        }
        while (idx<lines.Count)
        {
            var line=lines[idx]; ScanDepth(line);
            if (IsLikelyCode(line) || inHereD || inHereS || curly>0 || paren>0) { prefix.AppendLine(line); idx++; continue; }
            break;
        }
        if (prefix.Length>0)
        {
            codeOut = string.IsNullOrEmpty(code) ? prefix.ToString().TrimEnd() : (code.TrimEnd()+"\n\n"+prefix.ToString().TrimEnd());
            remarksOut = string.Join("\n", lines.Skip(idx)).TrimStart('\n');
        }
        else { codeOut = code.TrimEnd(); remarksOut = remarks.TrimStart('\n'); }
    }

    private List<(string Command, CommandHelpModel? Model, string? Help, int Lines)> BuildHelpMap(string moduleName, List<string> commands, int timeoutSeconds, ExamplesMode examplesMode, Action<string>? log)
    {
        var list = new List<(string, CommandHelpModel?, string?, int)>();
            var parser = new GetHelpParser();
        foreach (var cmd in commands)
        {
            string? content = null;
            // Prefer structured parse
            var model = parser.Parse(cmd, timeoutSeconds, examplesMode);
            if (model != null)
            {
                content = null; // render with components
            }
            else
            {
                // Fallback to raw Get-Help text -> markdown
                content = GetHelpMarkdown(moduleName, cmd, timeoutSeconds);
            }
            var lines = string.IsNullOrEmpty(content) ? 0 : content!.Split(new[] {'\n'}, StringSplitOptions.None).Length;
            if (model != null)
            {
                var sets = model.Syntax?.Count ?? 0;
                var pcount = model.Parameters?.Count ?? 0;
                var ex = model.Examples?.Count ?? 0;
                log?.Invoke($"Rendering help for {cmd}... (structured: sets={sets}, params={pcount}, examples={ex})");
                // Per-example diagnostics
                if (model.Examples != null && model.Examples.Count > 0)
                {
                    for (int i = 0; i < model.Examples.Count; i++)
                    {
                        var e = model.Examples[i];
                        var mode = string.IsNullOrEmpty(e.Mode) ? "structured" : e.Mode!;
                        log?.Invoke($"  • Example {i + 1}: mode={mode}, codeLines={e.CodeLines}, remarksLines={e.RemarksLines}");
                    }
                }
            }
            else
            {
                log?.Invoke($"Rendering help for {cmd}... ({lines} lines)");
            }
            list.Add((cmd, model, content, lines));
        }
        return list;
    }

    private static void RenderHelpPanel(TablerTabsPanel panel, CommandHelpModel m, ExamplesLayout examplesLayout)
    {
        if (!string.IsNullOrWhiteSpace(m.Synopsis))
        {
            panel.Markdown("## Synopsis", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Synopsis.Trim(), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.LineBreak();
        }
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            panel.Markdown("## Description", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Description.Trim(), new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.LineBreak();
        }
        if (m.Syntax.Count > 0)
        {
            panel.Markdown("## Syntax", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            var total = m.Syntax.Count;
            var byName = m.Parameters.ToDictionary(x => x.Name, System.StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < total; i++)
            {
                var s = m.Syntax[i];
                panel.Markdown($"### Syntax - Set {i + 1} of {total}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                var parts = new System.Collections.Generic.List<string>();
                foreach (var p in s.Parameters)
                {
                    var name = p.Name;
                    var type = string.IsNullOrEmpty(p.Type) ? string.Empty : $" <{p.Type}>";
                    var token = $"-{name}{type}";
                    if (!(p.Required ?? false)) token = $"[{token}]";
                    parts.Add(token);
                }
                var line = (s.Name ?? m.Name) + (parts.Count > 0 ? (" " + string.Join(" ", parts)) : string.Empty);
                var fenced = $"```powershell\n{line}\n```";
                panel.Markdown(fenced, new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });

                // Immediately render the parameters for this set
                var names = s.Parameters.Select(p => p.Name).Distinct(System.StringComparer.OrdinalIgnoreCase);
                var rows = new System.Collections.Generic.List<object>();
                foreach (var n in names)
                {
                    var p = byName.TryGetValue(n, out var ph) ? ph : new ParameterHelp { Name = n };
                    rows.Add(new {
                        Name = p.Name,
                        Type = p.Type ?? string.Empty,
                        Required = p.Required.HasValue ? (p.Required.Value ? "Yes" : "No") : string.Empty,
                        Position = p.Position ?? string.Empty,
                        Pipeline = p.PipelineInput ?? string.Empty,
                        Wildcards = p.Globbing.HasValue ? (p.Globbing.Value ? "Yes" : "No") : string.Empty,
                        Default = p.DefaultValue ?? string.Empty,
                        Aliases = (p.Aliases == null || p.Aliases.Count == 0) ? string.Empty : string.Join(", ", p.Aliases),
                        Description = p.Description ?? string.Empty
                    });
                }
                panel.Card(c => {
                    c.Header(h => h.Title($"Parameters - Set {i + 1} of {total}"));
                    c.DataTable(rows, t => t
                        .Settings(s => s.Preset(DataTablesPreset.Minimal))
                        .Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy))
                        .Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))
                    );
                });
            }
            panel.LineBreak();
        }
        if (m.Inputs.Count > 0)
        {
            var rows = m.Inputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Inputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.Minimal)).Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy)).Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))); });
        }
        if (m.Outputs.Count > 0)
        {
            var rows = m.Outputs.Select(t => new { Type = t.TypeName, Description = t.Description ?? string.Empty }).ToList();
            panel.Card(c => { c.Header(h => h.Title("Outputs")); c.DataTable(rows, t => t.Settings(s => s.Preset(DataTablesPreset.Minimal)).Settings(s => s.Export(DataTablesExportFormat.Excel, DataTablesExportFormat.CSV, DataTablesExportFormat.Copy)).Settings(s => s.ToggleViewButton("Switch to ScrollX", ToggleViewMode.ScrollX, persist: true))); });
        }
        if (m.Examples.Count > 0)
        {
            panel.Markdown("## Examples", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            int idx = 1;
            foreach (var ex in m.Examples)
            {
                var title = NormalizeExampleTitle(ex.Title, idx);
                panel.Markdown($"### {title}", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                var code = ex.Code ?? string.Empty;
                var remarks = ex.Remarks ?? string.Empty;
                switch (examplesLayout)
                {
                    case ExamplesLayout.ProseFirst:
                        if (!string.IsNullOrWhiteSpace(remarks)) panel.Markdown(remarks.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        if (!string.IsNullOrWhiteSpace(code)) {
                            var fenced1 = $"```powershell\n{code.TrimEnd()}\n```";
                            panel.Markdown(fenced1, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        }
                        break;
                    case ExamplesLayout.AllAsCode:
                        var block = string.IsNullOrEmpty(remarks) ? code : (code + "\n\n" + remarks);
                        var fenced2 = $"```powershell\n{(block ?? string.Empty).TrimEnd()}\n```";
                        panel.Markdown(fenced2, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        break;
                    default: // MamlDefault
                        if (!string.IsNullOrWhiteSpace(code)) {
                            var fenced3 = $"```powershell\n{code.TrimEnd()}\n```";
                            panel.Markdown(fenced3, new MarkdownOptions { HeadingsBaseLevel = 4, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        }
                        if (!string.IsNullOrWhiteSpace(remarks)) panel.Markdown(remarks.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
                        break;
                }
                panel.LineBreak();
                idx++;
            }
        }
        if (!string.IsNullOrWhiteSpace(m.Notes))
        {
            panel.Markdown("## Notes", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            panel.Markdown(m.Notes!.Trim(), new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
        }
        if (m.RelatedLinks.Count > 0)
        {
            panel.Markdown("## Related Links", new MarkdownOptions { HeadingsBaseLevel = 2, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
            var md = string.Join("\n", m.RelatedLinks.Select(l => !string.IsNullOrEmpty(l.Uri) ? $"- [{l.Title}]({l.Uri})" : $"- {l.Title}"));
            panel.Markdown(md, new MarkdownOptions { HeadingsBaseLevel = 3, AutolinkBareUrls = true, Sanitize = true, AllowRawHtmlInline = true, AllowRawHtmlBlocks = true });
        }
    }

}

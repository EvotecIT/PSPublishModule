using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Spectre.Console;

namespace PSMaintenance.Rendering.Highlighters;

/// <summary>
/// PowerShell highlighter using PSParser via reflection to preserve exact code spans.
/// </summary>
internal sealed class PowerShellHighlighter : IHighlighter
{
    private static readonly string[] _langs = new[]{"powershell","ps","ps1","pwsh","pscore"};
    public bool CanHandle(string language) => _langs.Contains((language ?? string.Empty).ToLowerInvariant());

    public string? Highlight(string code, string language)
    {
        try
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var sma = typeof(System.Management.Automation.PSObject).Assembly;
            var psParser = sma.GetType("System.Management.Automation.PSParser");
            if (psParser == null) return null; // let pipeline fallback

            // Resolve Tokenize(string, out errors) overload robustly
            MethodInfo? tokenize = null;
            foreach (var m in psParser.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "Tokenize") continue;
                var ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.IsByRef)
                {
                    tokenize = m; break;
                }
            }
            if (tokenize == null) return null;

            object? errors = null;
            object?[] args = new object?[] { code, errors };
            var tokens = tokenize.Invoke(null, args) as System.Collections.IEnumerable;
            if (tokens == null) return null;

            // Build markup preserving original spacing via Start/Length
            var tokenList = new List<(int Start,int Length,string Type)>();
            foreach (var t in tokens)
            {
                var tType = t.GetType();
                var pType = tType.GetProperty("Type") ?? tType.GetProperty("TokenType");
                var typeObj = pType?.GetValue(t);
                var typeName = typeObj?.ToString() ?? string.Empty;
                int start = 0; int length = 0;
                // Prefer Extent offsets for multi-line accuracy
                var extent = tType.GetProperty("Extent")?.GetValue(t);
                if (extent != null)
                {
                    var extType = extent.GetType();
                    var startOffset = extType.GetProperty("StartOffset")?.GetValue(extent);
                    var endOffset = extType.GetProperty("EndOffset")?.GetValue(extent);
                    if (startOffset != null && endOffset != null)
                    {
                        start = Convert.ToInt32(startOffset); length = Convert.ToInt32(endOffset) - start;
                    }
                }
                if (length == 0)
                {
                    var pStart = tType.GetProperty("Start");
                    var pLength = tType.GetProperty("Length");
                    if (pStart != null && pLength != null)
                    {
                        var sv = pStart.GetValue(t);
                        var lv = pLength.GetValue(t);
                        if (sv != null && lv != null)
                        {
                            start = Convert.ToInt32(sv);
                            length = Convert.ToInt32(lv);
                        }
                    }
                }
                tokenList.Add((start,length,typeName));
            }
            tokenList = tokenList.OrderBy(x => x.Start).ToList();

            var sb = new System.Text.StringBuilder();
            int pos = 0;
            foreach (var tk in tokenList)
            {
                if (tk.Start > pos)
                    sb.Append(Markup.Escape(code.Substring(pos, tk.Start - pos)));
                var segment = (tk.Length > 0 && tk.Start >= 0 && tk.Start + tk.Length <= code.Length) ? code.Substring(tk.Start, tk.Length) : string.Empty;
                var style = StyleForPsToken(tk.Type);
                if (style == null) sb.Append(Markup.Escape(segment));
                else sb.Append("[").Append(style).Append("]").Append(Markup.Escape(segment)).Append("[/]");
                pos = tk.Start + tk.Length;
            }
            if (pos < code.Length) sb.Append(Markup.Escape(code.Substring(pos)));
            return sb.ToString();
        }
        catch
        {
            return null; // fallback to generic
        }
    }

    private static string? StyleForPsToken(string type)
    {
        switch ((type ?? string.Empty))
        {
            case "String": return "orange1";
            case "Number": return "orchid";
            case "Command": return "springgreen1";
            case "Parameter": return "yellow";
            case "Variable": return "turquoise2";
            case "Comment": return "grey50 italic";
            case "Keyword": return "deepskyblue2";
            default: return null;
        }
    }
}

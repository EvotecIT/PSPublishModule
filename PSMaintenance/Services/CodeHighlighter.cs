// ReSharper disable All
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace PSMaintenance;

internal static class CodeHighlighter
{
    public static string Highlight(string code, string language)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var lang = (language ?? string.Empty).Trim().ToLowerInvariant();
        if (lang is "powershell" or "ps" or "ps1" or "pwsh" or "pscore")
            return HighlightPowerShell(code);
        if (lang is "csharp" or "cs" or "c#")
            return HighlightCSharp(code);
        if (lang is "json" or "jsonc")
            return HighlightJson(code);
        if (lang is "yaml" or "yml")
            return HighlightYaml(code);
        // default: just escape
        return Markup.Escape(code);
    }

    private static string HighlightPowerShell(string code)
    {
        var sb = new StringBuilder();
        var lines = code.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(HighlightPowerShellLine(lines[i]));
        }
        return sb.ToString();
    }

    private static string HighlightPowerShellLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return string.Empty;
        var spans = new List<SpanToken>();

        // comments: from # to end of line (not escaped with backtick)
        var mComment = Regex.Match(line, @"(?<!`)#.*");
        if (mComment.Success)
        {
            spans.Add(new SpanToken(mComment.Index, line.Length - mComment.Index, "grey50 italic"));
        }

        AddRegexSpans(spans, line, @"\$[A-Za-z_][A-Za-z0-9_:]*", "turquoise2"); // variables
        AddRegexSpans(spans, line, @"(?<=^|\s)-[A-Za-z][A-Za-z0-9-]*", "yellow"); // parameters
        AddRegexSpans(spans, line, @"\b[A-Za-z]+-[A-Za-z][A-Za-z0-9]*\b", "springgreen1"); // cmdlets
        AddRegexSpans(spans, line, @"\b(0x[0-9A-Fa-f]+|\d+(?:\.\d+)?)\b", "orchid"); // numbers

        // strings: double and single quotes (simple, line-local)
        AddRegexSpans(spans, line, "\"([^\\\"\n]|\\.)*\"", "orange1");
        AddRegexSpans(spans, line, "'[^'\n]*'", "orange1");

        // keywords (limited set)
        var keywords = new[]{"function","param","begin","process","end","if","else","elseif","foreach","for","while","do","switch","return","try","catch","finally","throw","trap","break","continue","class"};
        AddWordSpans(spans, line, keywords, "deepskyblue2");

        return BuildMarkup(line, spans);
    }

    private static string HighlightCSharp(string code)
    {
        var sb = new StringBuilder();
        var lines = code.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        bool inBlockComment = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var spans = new List<SpanToken>();
            var line = lines[i];
            if (string.IsNullOrEmpty(line)) { continue; }

            // handle block comments start/end (very simple)
            if (inBlockComment)
            {
                int end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end >= 0)
                {
                    spans.Add(new SpanToken(0, end+2, "grey50 italic"));
                    inBlockComment = false;
                    // continue scanning remainder
                    var rest = line.Substring(end+2);
                    var colored = BuildMarkup(rest, spans: new List<SpanToken>());
                    sb.Append(Markup.Escape(line.Substring(0,0))); // no-op
                    sb.Append(BuildMarkup(line, spans));
                    continue;
                }
                else
                {
                    spans.Add(new SpanToken(0, line.Length, "grey50 italic"));
                    sb.Append(BuildMarkup(line, spans));
                    continue;
                }
            }

            // single-line comment
            var sl = line.IndexOf("//", StringComparison.Ordinal);
            if (sl >= 0)
            {
                spans.Add(new SpanToken(sl, line.Length - sl, "grey50 italic"));
            }

            // block comment start
            var bc = line.IndexOf("/*", StringComparison.Ordinal);
            if (bc >= 0)
            {
                int end = line.IndexOf("*/", bc+2, StringComparison.Ordinal);
                if (end >= 0)
                {
                    spans.Add(new SpanToken(bc, end - bc + 2, "grey50 italic"));
                }
                else
                {
                    spans.Add(new SpanToken(bc, line.Length - bc, "grey50 italic"));
                    inBlockComment = true;
                }
            }

            // strings and chars
            AddRegexSpans(spans, line, "@\\\"([^\\\"]|\\\"\\\")*\\\"", "orange1"); // verbatim string @\"...\"
            AddRegexSpans(spans, line, "\\\"([^\\\\\\\"\\n]|\\\\.)*\\\"", "orange1"); // normal string \"...\"
            AddRegexSpans(spans, line, "'(?:\\\\.|[^'\\\\])'", "orange1"); // char literal

            // numbers
            AddRegexSpans(spans, line, @"\b(0x[0-9A-Fa-f]+|\d+(?:\.\d+)?)(?:[uUlLfFmMdD])?\b", "orchid");

            // keywords
            var keywords = new[]{"using","namespace","class","interface","struct","enum","public","private","protected","internal","static","readonly","record","void","int","string","var","new","return","if","else","for","foreach","while","do","switch","case","break","continue","try","catch","finally","throw","this","base","true","false","null"};
            AddWordSpans(spans, line, keywords, "deepskyblue2");

            // PascalCase types
            AddRegexSpans(spans, line, @"\b[A-Z][A-Za-z0-9_]*\b", "turquoise2");

            sb.Append(BuildMarkup(line, spans));
        }
        return sb.ToString();
    }

    private static string HighlightJson(string code)
    {
        var sb = new StringBuilder();
        var lines = code.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            var spans = new List<SpanToken>();
            // strings
            AddRegexSpans(spans, line, "\\\"([^\\\"\\n]|\\\\.)*\\\"", "orange1");
            // numbers
            AddRegexSpans(spans, line, "-?\\b(0x[0-9A-Fa-f]+|\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?)\\b", "orchid");
            // booleans/null
            AddWordSpans(spans, line, new[]{"true","false","null"}, "deepskyblue2");
            // keys (string before colon)
            AddRegexSpans(spans, line, "\\\"([^\\\"\\n]|\\\\.)*\\\"(?=\\s*:)" , "turquoise2");
            sb.Append(BuildMarkup(line, spans));
        }
        return sb.ToString();
    }

    private static string HighlightYaml(string code)
    {
        var sb = new StringBuilder();
        var lines = code.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            var spans = new List<SpanToken>();
            // comments
            var m = Regex.Match(line, "(?<!`)#.*");
            if (m.Success) spans.Add(new SpanToken(m.Index, line.Length - m.Index, "grey50 italic"));
            // keys (start of line up to colon)
            AddRegexSpans(spans, line, "^\\s*[A-Za-z0-9_.-]+(?=\\s*:)", "turquoise2");
            // strings
            AddRegexSpans(spans, line, "\\\"([^\\\"\\n]|\\\\.)*\\\"", "orange1");
            AddRegexSpans(spans, line, "'(?:\\\\.|[^'\\\\])'", "orange1");
            // numbers & booleans
            AddRegexSpans(spans, line, "-?\\b\\d+(?:\\.\\d+)?\\b", "orchid");
            AddWordSpans(spans, line, new[]{"true","false","null","on","off","yes","no"}, "deepskyblue2");
            // list indicators '- '
            AddRegexSpans(spans, line, "^\\s*-\\s+", "grey70");
            sb.Append(BuildMarkup(line, spans));
        }
        return sb.ToString();
    }

    private static void AddRegexSpans(List<SpanToken> spans, string line, string pattern, string style)
    {
        foreach (Match m in Regex.Matches(line, pattern))
        {
            if (!Overlaps(spans, m.Index, m.Length)) spans.Add(new SpanToken(m.Index, m.Length, style));
        }
    }

    private static void AddWordSpans(List<SpanToken> spans, string line, string[] words, string style)
    {
        var pattern = "\\b(" + string.Join("|", words.Select(Regex.Escape)) + ")\\b";
        foreach (Match m in Regex.Matches(line, pattern))
        {
            if (!Overlaps(spans, m.Index, m.Length)) spans.Add(new SpanToken(m.Index, m.Length, style));
        }
    }

    private static bool Overlaps(List<SpanToken> spans, int index, int length)
    {
        int end = index + length;
        foreach (var s in spans)
        {
            int sEnd = s.Index + s.Length;
            if (index < sEnd && s.Index < end) return true;
        }
        return false;
    }

    private static string BuildMarkup(string line, List<SpanToken> spans)
    {
        if (spans.Count == 0) return Markup.Escape(line);
        spans = spans.OrderBy(s => s.Index).ToList();
        var sb = new StringBuilder();
        int pos = 0;
        foreach (var s in spans)
        {
            if (s.Index > pos)
            {
                sb.Append(Markup.Escape(line.Substring(pos, s.Index - pos)));
            }
            var tokenText = line.Substring(s.Index, s.Length);
            sb.Append('[').Append(s.Style).Append(']').Append(Markup.Escape(tokenText)).Append("[/]");
            pos = s.Index + s.Length;
        }
        if (pos < line.Length) sb.Append(Markup.Escape(line.Substring(pos)));
        return sb.ToString();
    }

    private sealed class SpanToken
    {
        public int Index { get; }
        public int Length { get; }
        public string Style { get; }
        public SpanToken(int index, int length, string style) { Index = index; Length = length; Style = style; }
    }
}

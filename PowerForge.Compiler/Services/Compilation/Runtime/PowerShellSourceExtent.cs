namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation.Language;

    /// <summary>Builds native source positions from complete authored lines without normalizing their text.</summary>
    internal static class PowerShellSourceExtent
    {
        internal static IScriptExtent Create(string file, int line, int column, int endLine, int endColumn, string text)
        {
            text ??= string.Empty;
            var endLineStart = 0;
            for (var current = line; current < endLine; current++) endLineStart = EndOfLine(text, endLineStart);
            return new ScriptExtent(
                new ScriptPosition(file, line, column, text.Substring(0, EndOfLine(text, 0))),
                new ScriptPosition(file, endLine, endColumn,
                    text.Substring(endLineStart, EndOfLine(text, endLineStart) - endLineStart)));
        }

        private static int EndOfLine(string text, int start)
        {
            var end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            if (end < text.Length && text[end++] == '\r' && end < text.Length && text[end] == '\n') end++;
            return end;
        }
    }
}

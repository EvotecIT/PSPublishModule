using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Restores authored positions after the executable entry body is wrapped for semantic compilation.</summary>
internal static class PowerShellTypedExecutableEntrySourceMapper
{
    internal static void Remap(
        PowerShellCSharpMethodEmission emission,
        ParsedSourceDocument synthetic,
        ParsedSourceDocument authored,
        IReadOnlyList<PowerShellRegionSourceRemap> mappings)
    {
        var pattern = "(?m)^(?<prefix>[ \\t]*#line[ \\t]+)(?<line>[0-9]+)[ \\t]+\"" +
                      Regex.Escape(synthetic.DocumentId) + "\"";
        var remapped = Regex.Replace(emission.Source, pattern, match =>
        {
            var line = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            var authoredLine = MapLine(synthetic.Text, authored.Text, mappings, line);
            return match.Groups["prefix"].Value + authoredLine.ToString(CultureInfo.InvariantCulture) +
                   " \"" + authored.DocumentId + "\"";
        });
        if (remapped.Contains('"' + synthetic.DocumentId + '"', StringComparison.Ordinal))
            throw new InvalidDataException("Executable entry source retained an unmapped synthetic document.");
        emission.Source = remapped;
        emission.SourceMap = emission.SourceMap.Select(entry =>
        {
            var start = MapPosition(synthetic.Text, authored.Text, mappings,
                entry.SourceStartLine, entry.SourceStartColumn);
            var end = MapPosition(synthetic.Text, authored.Text, mappings,
                entry.SourceEndLine, entry.SourceEndColumn);
            return new PowerShellCompilationSourceMapEntry(
                start.Line, start.Column, end.Line, end.Column,
                entry.GeneratedStartLine, entry.GeneratedStartColumn,
                entry.GeneratedEndLine, entry.GeneratedEndColumn);
        }).ToArray();
        var end = GetLineColumn(authored.Text, authored.Text.Length);
        emission.SourceSpan = new SourceSpan(authored.DocumentId, 0, authored.Text.Length,
            1, 1, end.Line, end.Column);
    }

    private static int MapLine(string synthetic, string authored,
        IReadOnlyList<PowerShellRegionSourceRemap> mappings, int line)
    {
        var lineStart = GetOffset(synthetic, line, 1);
        var lineEnd = lineStart;
        while (lineEnd < synthetic.Length && synthetic[lineEnd] != '\n') lineEnd++;
        foreach (var mapping in mappings)
        {
            if (mapping.SyntheticStartOffset > lineEnd || mapping.SyntheticEndOffset < lineStart)
                continue;
            var position = Math.Max(lineStart, mapping.SyntheticStartOffset);
            return GetLineColumn(authored, MapOffset(position, mapping)).Line;
        }
        throw new InvalidDataException($"Executable entry line {line} has no authored source mapping.");
    }

    private static (int Line, int Column) MapPosition(string synthetic, string authored,
        IReadOnlyList<PowerShellRegionSourceRemap> mappings, int line, int column)
    {
        var offset = GetOffset(synthetic, line, column);
        foreach (var mapping in mappings)
            if (offset >= mapping.SyntheticStartOffset && offset <= mapping.SyntheticEndOffset)
                return GetLineColumn(authored, MapOffset(offset, mapping));
        throw new InvalidDataException($"Executable entry position {line}:{column} has no authored source mapping.");
    }

    private static int MapOffset(int offset, PowerShellRegionSourceRemap mapping)
        => mapping.AuthoredStartOffset + Math.Min(
            offset - mapping.SyntheticStartOffset,
            mapping.AuthoredEndOffset - mapping.AuthoredStartOffset);

    private static int GetOffset(string text, int line, int column)
    {
        if (line < 1 || column < 1) throw new InvalidDataException("Executable entry has an invalid source position.");
        var currentLine = 1;
        var lineStart = 0;
        for (var index = 0; index < text.Length && currentLine < line; index++)
            if (text[index] == '\n') { currentLine++; lineStart = index + 1; }
        if (currentLine != line || lineStart + column - 1 > text.Length)
            throw new InvalidDataException($"Executable entry position {line}:{column} exceeds the synthetic source.");
        return lineStart + column - 1;
    }

    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
            if (text[index] == '\n') { line++; column = 1; }
            else column++;
        return (line, column);
    }
}

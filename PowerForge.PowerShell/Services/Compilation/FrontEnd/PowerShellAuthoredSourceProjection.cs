using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Maps a synthetic executable entry statement back to its exact authored document.</summary>
internal sealed class PowerShellAuthoredSourceProjection
{
    private readonly ParsedSourceDocument _authored;
    private readonly PowerShellRegionSourceRemap[] _mappings;

    internal PowerShellAuthoredSourceProjection(ParsedSourceDocument authored, PowerShellRegionSourceRemap[] mappings)
    {
        _authored = authored ?? throw new ArgumentNullException(nameof(authored));
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
    }

    internal PowerShellCommandRegionSourceSelection Select(
        ParsedSourceDocument synthetic,
        IEnumerable<IScriptExtent> statements)
        => new(_authored.Path, _authored.Text, statements.Select(extent => Map(synthetic, extent)));

    private SourceSpan Map(ParsedSourceDocument synthetic, IScriptExtent extent)
    {
        var matches = _mappings.Where(mapping =>
            extent.StartOffset >= mapping.SyntheticStartOffset &&
            extent.EndOffset <= mapping.SyntheticEndOffset).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException("Executable entry hosted statement has no unique authored source mapping.");
        var mapping = matches[0];
        var start = mapping.AuthoredStartOffset + extent.StartOffset - mapping.SyntheticStartOffset;
        var end = mapping.AuthoredStartOffset + extent.EndOffset - mapping.SyntheticStartOffset;
        if (end > mapping.AuthoredEndOffset ||
            !synthetic.Text.Substring(extent.StartOffset, extent.EndOffset - extent.StartOffset)
                .Equals(_authored.Text.Substring(start, end - start), StringComparison.Ordinal))
            throw new InvalidDataException("Executable entry hosted statement differs from its authored source.");
        var startPosition = GetPosition(_authored.Text, start);
        var endPosition = GetPosition(_authored.Text, end);
        return new SourceSpan(_authored.DocumentId, start, end,
            startPosition.Line, startPosition.Column, endPosition.Line, endPosition.Column);
    }

    private static (int Line, int Column) GetPosition(string source, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
            if (source[index] == '\n') { line++; column = 1; }
            else column++;
        return (line, column);
    }
}

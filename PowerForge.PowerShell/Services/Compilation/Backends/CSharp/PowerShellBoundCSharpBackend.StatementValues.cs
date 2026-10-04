using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string? _statementValueDiscardHelper;
    private readonly List<(string Text, PowerShellCompilationSourceMapEntry[] Entries)> _statementValueMaps = new();

    private string EmitNativeStatementValue(PowerShellLoweredNativeStatementValueExpression value)
    {
        var builder = new StringBuilder("new global::System.Func<object?>(() =>\n{\n");
        var sourceMap = new List<PowerShellCompilationSourceMapEntry>();
        EmitCapturedOutputBody(builder, value.Statements, value.RecordsTemporary, value.SinkTemporary,
            null, true, PowerShellOutputCaptureKind.CollapsedPowerShellValue, false, 1,
            _getTemporaryIdentifier!, _statementValueDiscardHelper, sourceMap);
        builder.Append("})()");
        var text = builder.ToString();
        _statementValueMaps.Add((text, sourceMap.ToArray()));
        return text;
    }

    // Expressions are rendered as fragments. Relocate embedded statement maps
    // after their exact generated text is inserted into the containing builder.
    private void MapStatementValueFragments(StringBuilder builder, int fragmentStart, int mapStart,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        if (_statementValueMaps.Count == mapStart) return;
        var text = builder.ToString();
        for (var i = mapStart; i < _statementValueMaps.Count; i++)
        {
            var fragment = _statementValueMaps[i];
            var offset = text.IndexOf(fragment.Text, fragmentStart, StringComparison.Ordinal);
            if (offset < 0) throw new InvalidOperationException("A statement-valued expression lost its generated source-map fragment.");
            var position = PowerShellGeneratedSourcePosition.Get(new StringBuilder(text.Substring(0, offset)));
            foreach (var entry in fragment.Entries)
                sourceMap.Add(new PowerShellCompilationSourceMapEntry(entry.SourceStartLine, entry.SourceStartColumn,
                    entry.SourceEndLine, entry.SourceEndColumn,
                    position.Line + entry.GeneratedStartLine - 1,
                    entry.GeneratedStartColumn + (entry.GeneratedStartLine == 1 ? position.Column - 1 : 0),
                    position.Line + entry.GeneratedEndLine - 1,
                    entry.GeneratedEndColumn + (entry.GeneratedEndLine == 1 ? position.Column - 1 : 0)));
        }
        _statementValueMaps.RemoveRange(mapStart, _statementValueMaps.Count - mapStart);
    }
}

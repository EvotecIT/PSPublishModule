namespace PowerForge;

/// <summary>Authored statement selections used only to preserve hosted-region source metadata.</summary>
internal sealed class PowerShellCommandRegionSourceSelection
{
    internal PowerShellCommandRegionSourceSelection(string path, string document, IEnumerable<SourceSpan> statements)
    {
        Path = path;
        Document = document;
        Statements = statements.ToArray();
    }

    internal string Path { get; }
    internal string Document { get; }
    internal PowerShellImmutableArray<SourceSpan> Statements { get; }
}

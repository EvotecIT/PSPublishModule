namespace PowerForge;

internal sealed class FormattingBatch
{
    internal FormattingBatch(IEnumerable<string> files, FormatOptions options)
    {
        Files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Options = options;
    }

    public string[] Files { get; }
    public FormatOptions Options { get; }
}

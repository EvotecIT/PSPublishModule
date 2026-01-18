namespace PowerForge;

/// <summary>
/// Extensions for mapping file consistency line endings to converter values.
/// </summary>
public static class FileConsistencyLineEndingExtensions
{
    /// <summary>
    /// Maps a <see cref="FileConsistencyLineEnding"/> to a <see cref="LineEnding"/>.
    /// </summary>
    public static LineEnding ToLineEnding(this FileConsistencyLineEnding lineEnding)
        => lineEnding == FileConsistencyLineEnding.CRLF ? LineEnding.CRLF : LineEnding.LF;
}

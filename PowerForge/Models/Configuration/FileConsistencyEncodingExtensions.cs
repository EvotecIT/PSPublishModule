namespace PowerForge;

/// <summary>
/// Extensions for mapping file consistency encodings to conversion encodings.
/// </summary>
public static class FileConsistencyEncodingExtensions
{
    /// <summary>
    /// Maps a <see cref="FileConsistencyEncoding"/> to a <see cref="TextEncodingKind"/>.
    /// </summary>
    public static TextEncodingKind ToTextEncodingKind(this FileConsistencyEncoding encoding)
        => encoding switch
        {
            FileConsistencyEncoding.ASCII => TextEncodingKind.Ascii,
            FileConsistencyEncoding.UTF8 => TextEncodingKind.UTF8,
            FileConsistencyEncoding.UTF8BOM => TextEncodingKind.UTF8BOM,
            FileConsistencyEncoding.Unicode => TextEncodingKind.Unicode,
            FileConsistencyEncoding.BigEndianUnicode => TextEncodingKind.BigEndianUnicode,
            FileConsistencyEncoding.UTF7 => TextEncodingKind.UTF7,
            FileConsistencyEncoding.UTF32 => TextEncodingKind.UTF32,
            _ => TextEncodingKind.UTF8BOM
        };
}

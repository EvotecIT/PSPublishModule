namespace PowerForge;

/// <summary>
/// AST-backed PowerShell manifest mutator built on the existing <see cref="ManifestEditor"/> helpers.
/// </summary>
public sealed class AstModuleManifestMutator : IModuleManifestMutator
{
    /// <inheritdoc />
    public bool TrySetTopLevelModuleVersion(string filePath, string newVersion)
        => ManifestEditor.TrySetTopLevelModuleVersion(filePath, newVersion);

    /// <inheritdoc />
    public bool TrySetTopLevelString(string filePath, string key, string newValue)
        => ManifestEditor.TrySetTopLevelString(filePath, key, newValue);

    /// <inheritdoc />
    public bool TrySetTopLevelStringArray(string filePath, string key, string[] values)
        => ManifestEditor.TrySetTopLevelStringArray(filePath, key, values);

    /// <inheritdoc />
    public bool TrySetPsDataString(string filePath, string key, string value)
        => ManifestEditor.TrySetPsDataString(filePath, key, value);

    /// <inheritdoc />
    public bool TrySetPsDataStringArray(string filePath, string key, string[] values)
        => ManifestEditor.TrySetPsDataStringArray(filePath, key, values);
}

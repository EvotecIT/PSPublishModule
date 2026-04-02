namespace PowerForge;

/// <summary>
/// Mutates selected PowerShell module manifest fields without exposing the underlying editing mechanism.
/// </summary>
public interface IModuleManifestMutator
{
    /// <summary>
    /// Sets the top-level <c>ModuleVersion</c> value.
    /// </summary>
    bool TrySetTopLevelModuleVersion(string filePath, string newVersion);

    /// <summary>
    /// Sets a top-level string value.
    /// </summary>
    bool TrySetTopLevelString(string filePath, string key, string newValue);

    /// <summary>
    /// Sets a top-level string array value.
    /// </summary>
    bool TrySetTopLevelStringArray(string filePath, string key, string[] values);

    /// <summary>
    /// Sets a <c>PrivateData.PSData</c> string value.
    /// </summary>
    bool TrySetPsDataString(string filePath, string key, string value);

    /// <summary>
    /// Sets a <c>PrivateData.PSData</c> string array value.
    /// </summary>
    bool TrySetPsDataStringArray(string filePath, string key, string[] values);
}

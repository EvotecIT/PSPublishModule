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

    /// <summary>
    /// Sets a <c>PrivateData.PSData</c> boolean value.
    /// </summary>
    bool TrySetPsDataBool(string filePath, string key, bool value);

    /// <summary>
    /// Removes a top-level key from the manifest.
    /// </summary>
    bool TryRemoveTopLevelKey(string filePath, string key);

    /// <summary>
    /// Removes a <c>PrivateData.PSData</c> key from the manifest.
    /// </summary>
    bool TryRemovePsDataKey(string filePath, string key);

    /// <summary>
    /// Sets the manifest <c>RequiredModules</c> collection.
    /// </summary>
    bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules);

    /// <summary>
    /// Sets a nested <c>PrivateData.PSData</c> string value such as <c>Delivery.Schema</c>.
    /// </summary>
    bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value);

    /// <summary>
    /// Sets a nested <c>PrivateData.PSData</c> string array value.
    /// </summary>
    bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values);

    /// <summary>
    /// Sets a nested <c>PrivateData.PSData</c> boolean value.
    /// </summary>
    bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value);

    /// <summary>
    /// Sets a nested <c>PrivateData.PSData</c> hashtable-array value.
    /// </summary>
    bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values);

    /// <summary>
    /// Updates manifest export fields in a single operation.
    /// </summary>
    bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases);

    /// <summary>
    /// Updates manifest repository metadata.
    /// </summary>
    bool TrySetRepository(string filePath, string? branch, string[]? paths);
}

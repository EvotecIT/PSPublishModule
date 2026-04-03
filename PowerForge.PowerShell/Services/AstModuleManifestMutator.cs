using System.Collections.Generic;
using System.Linq;

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

    /// <inheritdoc />
    public bool TrySetPsDataBool(string filePath, string key, bool value)
        => ManifestEditor.TrySetPsDataBool(filePath, key, value);

    /// <inheritdoc />
    public bool TryRemoveTopLevelKey(string filePath, string key)
        => ManifestEditor.TryRemoveTopLevelKey(filePath, key);

    /// <inheritdoc />
    public bool TryRemovePsDataKey(string filePath, string key)
        => ManifestEditor.TryRemovePsDataKey(filePath, key);

    /// <inheritdoc />
    public bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules)
        => ManifestEditor.TrySetRequiredModules(filePath, modules);

    /// <inheritdoc />
    public bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value)
        => ManifestEditor.TrySetPsDataSubString(filePath, parentKey, key, value);

    /// <inheritdoc />
    public bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values)
        => ManifestEditor.TrySetPsDataSubStringArray(filePath, parentKey, key, values);

    /// <inheritdoc />
    public bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value)
        => ManifestEditor.TrySetPsDataSubBool(filePath, parentKey, key, value);

    /// <inheritdoc />
    public bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values)
        => ManifestEditor.TrySetPsDataSubHashtableArray(
            filePath,
            parentKey,
            key,
            values?.Select(static item => item.ToDictionary(static pair => pair.Key, static pair => pair.Value)).ToArray()
                ?? Array.Empty<Dictionary<string, string>>());

    /// <inheritdoc />
    public bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases)
        => BuildServices.SetManifestExports(filePath, functions, cmdlets, aliases);

    /// <inheritdoc />
    public bool TrySetRepository(string filePath, string? branch, string[]? paths)
        => BuildServices.SetRepository(filePath, branch, paths);
}

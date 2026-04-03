using System.Collections.Generic;

namespace PowerForge.Tests;

internal static class ModuleBuilderTestDependencies
{
    public static ModuleBuilder Create(ILogger? logger = null)
        => new(
            logger ?? new NullLogger(),
            new NoOpManifestMutator(),
            new NoOpScriptFunctionExportDetector());

    private sealed class NoOpManifestMutator : IModuleManifestMutator
    {
        public bool TrySetTopLevelModuleVersion(string filePath, string newVersion) => true;
        public bool TrySetTopLevelString(string filePath, string key, string newValue) => true;
        public bool TrySetTopLevelStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataString(string filePath, string key, string value) => true;
        public bool TrySetPsDataStringArray(string filePath, string key, string[] values) => true;
        public bool TrySetPsDataBool(string filePath, string key, bool value) => true;
        public bool TryRemoveTopLevelKey(string filePath, string key) => true;
        public bool TryRemovePsDataKey(string filePath, string key) => true;
        public bool TrySetRequiredModules(string filePath, RequiredModuleReference[] modules) => true;
        public bool TrySetPsDataSubString(string filePath, string parentKey, string key, string value) => true;
        public bool TrySetPsDataSubStringArray(string filePath, string parentKey, string key, string[] values) => true;
        public bool TrySetPsDataSubBool(string filePath, string parentKey, string key, bool value) => true;
        public bool TrySetPsDataSubHashtableArray(string filePath, string parentKey, string key, IReadOnlyList<IReadOnlyDictionary<string, string>> values) => true;
        public bool TrySetManifestExports(string filePath, string[]? functions, string[]? cmdlets, string[]? aliases) => true;
        public bool TrySetRepository(string filePath, string? branch, string[]? paths) => true;
    }

    private sealed class NoOpScriptFunctionExportDetector : IScriptFunctionExportDetector
    {
        public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
            => Array.Empty<string>();
    }
}

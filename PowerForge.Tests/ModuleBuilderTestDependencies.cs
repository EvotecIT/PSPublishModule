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
    }

    private sealed class NoOpScriptFunctionExportDetector : IScriptFunctionExportDetector
    {
        public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
            => Array.Empty<string>();
    }
}

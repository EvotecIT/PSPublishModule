using System;

namespace PowerForge;

internal static class ModuleManifestExportReader
{
    internal static ExportSet ReadExports(string psd1Path)
        => new(
            ModuleManifestValueReader.ReadTopLevelStringOrArray(psd1Path, "FunctionsToExport"),
            ModuleManifestValueReader.ReadTopLevelStringOrArray(psd1Path, "CmdletsToExport"),
            ModuleManifestValueReader.ReadTopLevelStringOrArray(psd1Path, "AliasesToExport"));
}

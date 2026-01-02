using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ManifestEditorStringArrayTests
{
    [Fact]
    public void TryGetTopLevelStringArray_ReadsAtSyntaxArrays()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var psd1Path = Path.Combine(tempRoot.FullName, "Test.psd1");
            File.WriteAllText(psd1Path,
                "@{\n" +
                "    CmdletsToExport = @('A', 'B')\n" +
                "    AliasesToExport = @('*')\n" +
                "    FunctionsToExport = @()\n" +
                "}\n");

            Assert.True(ManifestEditor.TryGetTopLevelStringArray(psd1Path, "CmdletsToExport", out var cmdlets));
            Assert.Equal(new[] { "A", "B" }, cmdlets);

            Assert.True(ManifestEditor.TryGetTopLevelStringArray(psd1Path, "AliasesToExport", out var aliases));
            Assert.Equal(new[] { "*" }, aliases);

            Assert.True(ManifestEditor.TryGetTopLevelStringArray(psd1Path, "FunctionsToExport", out var functions));
            Assert.NotNull(functions);
            Assert.Empty(functions!);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}


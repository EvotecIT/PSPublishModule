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

    [Fact]
    public void TryGetPsDataStringArray_ReadsExternalModuleDependencies()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var psd1Path = Path.Combine(tempRoot.FullName, "Test.psd1");
            File.WriteAllText(psd1Path,
                "@{\n" +
                "    PrivateData = @{\n" +
                "        PSData = @{\n" +
                "            ExternalModuleDependencies = @('Microsoft.PowerShell.Utility', 'ActiveDirectory')\n" +
                "        }\n" +
                "    }\n" +
                "}\n");

            Assert.True(ManifestEditor.TryGetPsDataStringArray(psd1Path, "ExternalModuleDependencies", out var externalDependencies));
            Assert.Equal(new[] { "Microsoft.PowerShell.Utility", "ActiveDirectory" }, externalDependencies);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TryRemoveTopLevelKey_RemovesEntryFromManifest()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var psd1Path = Path.Combine(tempRoot.FullName, "Test.psd1");
            File.WriteAllText(psd1Path,
                "@{\n" +
                "    RootModule = 'Test.psm1'\n" +
                "    ModuleVersion = '1.0.0'\n" +
                "    CompanyName = 'OldCompany'\n" +
                "}\n");

            Assert.True(ManifestEditor.TryRemoveTopLevelKey(psd1Path, "CompanyName"));
            Assert.False(ManifestEditor.TryGetTopLevelString(psd1Path, "CompanyName", out _));
            Assert.True(ManifestEditor.TryGetTopLevelString(psd1Path, "ModuleVersion", out var version));
            Assert.Equal("1.0.0", version);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void TryRemovePsDataKeys_RemovesRootAndSubKeys()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var psd1Path = Path.Combine(tempRoot.FullName, "Test.psd1");
            File.WriteAllText(psd1Path,
                "@{\n" +
                "    PrivateData = @{\n" +
                "        PSData = @{\n" +
                "            ExternalModuleDependencies = @('A', 'B')\n" +
                "            Delivery = @{\n" +
                "                IntroText = @('Hello')\n" +
                "            }\n" +
                "        }\n" +
                "    }\n" +
                "}\n");

            Assert.True(ManifestEditor.TryRemovePsDataKey(psd1Path, "ExternalModuleDependencies"));
            Assert.False(ManifestEditor.TryGetPsDataStringArray(psd1Path, "ExternalModuleDependencies", out _));

            Assert.True(ManifestEditor.TryRemovePsDataSubKey(psd1Path, "Delivery", "IntroText"));
            var content = File.ReadAllText(psd1Path);
            Assert.DoesNotContain("IntroText", content, StringComparison.Ordinal);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

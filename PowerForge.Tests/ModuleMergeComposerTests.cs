using System;
using System.IO;

namespace PowerForge.Tests;

public sealed class ModuleMergeComposerTests
{
    [Fact]
    public void BuildSources_RewritesLegacyPSScriptRootParentPathsByDefault()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMergeModule(root.FullName, moduleName);

            var sources = ModuleMergeComposer.BuildSources(
                root.FullName,
                moduleName,
                information: null,
                exports: new ExportSet(new[] { "Get-TestExample" }, Array.Empty<string>(), Array.Empty<string>()),
                fixRelativePaths: true);

            Assert.True(sources.HasScripts);
            Assert.DoesNotContain("$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("$PSScriptRoot\\Resources\\JS\\jquery.min.js", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, 'Resources')", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport", sources.MergedScriptContent, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildSources_PreservesLegacyPSScriptRootParentPathsWhenOptedOut()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            WriteMergeModule(root.FullName, moduleName);

            var sources = ModuleMergeComposer.BuildSources(
                root.FullName,
                moduleName,
                information: null,
                exports: new ExportSet(new[] { "Get-TestExample" }, Array.Empty<string>(), Array.Empty<string>()),
                fixRelativePaths: false);

            Assert.Contains("$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')", sources.MergedScriptContent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void SyncMergedPsm1WithGeneratedScripts_ReplacesTrailingExportBlock()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var manifestPath = Path.Combine(root.FullName, moduleName + ".psd1");
            var psm1Path = Path.Combine(root.FullName, moduleName + ".psm1");
            var generatedPath = Path.Combine(root.FullName, "Public", "Install-" + moduleName + ".ps1");

            Directory.CreateDirectory(Path.Combine(root.FullName, "Public"));
            File.WriteAllText(
                manifestPath,
                """
                @{
                    RootModule = 'TestModule.psm1'
                    ModuleVersion = '1.0.0'
                    FunctionsToExport = @('Get-TestExample', 'Install-TestModule')
                    CmdletsToExport = @()
                    AliasesToExport = @()
                }
                """);
            File.WriteAllText(
                psm1Path,
                """
                function Get-TestExample { 'ok' }

                $FunctionsToExport = @('Get-TestExample')
                $CmdletsToExport = @()
                $AliasesToExport = @()
                Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport
                """);
            File.WriteAllText(generatedPath, "function Install-TestModule { 'install' }");

            ModuleMergeComposer.SyncMergedPsm1WithGeneratedScripts(manifestPath, root.FullName, moduleName, new[] { generatedPath });

            var merged = File.ReadAllText(psm1Path);

            Assert.Contains("function Get-TestExample", merged, StringComparison.Ordinal);
            Assert.Contains("function Install-TestModule", merged, StringComparison.Ordinal);
            Assert.Equal(
                1,
                CountOccurrences(merged, "Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport"));
            Assert.Contains("$FunctionsToExport = @('Get-TestExample', 'Install-TestModule')", merged, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PrependFunctions_PrependsFunctionsBeforeExistingContent()
    {
        var merged = ModuleMergeComposer.PrependFunctions(
            new[] { "function Get-Helper { 'helper' }", string.Empty },
            "function Get-TestExample { 'ok' }");

        Assert.StartsWith("function Get-Helper", merged, StringComparison.Ordinal);
        Assert.Contains(Environment.NewLine + Environment.NewLine + "function Get-TestExample", merged, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void WriteMergeModule(string rootPath, string moduleName)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "Public"));

        File.WriteAllText(Path.Combine(rootPath, moduleName + ".psm1"), "# bootstrap");
        File.WriteAllText(
            Path.Combine(rootPath, moduleName + ".psd1"),
            "@{" + Environment.NewLine +
            "    RootModule = '" + moduleName + ".psm1'" + Environment.NewLine +
            "    ModuleVersion = '1.0.0'" + Environment.NewLine +
            "    FunctionsToExport = @('Get-TestExample')" + Environment.NewLine +
            "    CmdletsToExport = @()" + Environment.NewLine +
            "    AliasesToExport = @()" + Environment.NewLine +
            "}");

        File.WriteAllText(
            Path.Combine(rootPath, "Public", "Get-TestExample.ps1"),
            "function Get-TestExample {" + Environment.NewLine +
            "    $pathA = '$PSScriptRoot\\..\\Resources\\JS\\jquery.min.js'" + Environment.NewLine +
            "    $pathB = \"[IO.Path]::Combine($PSScriptRoot, '..', 'Resources')\"" + Environment.NewLine +
            "    $pathA" + Environment.NewLine +
            "    $pathB" + Environment.NewLine +
            "}");
    }
}

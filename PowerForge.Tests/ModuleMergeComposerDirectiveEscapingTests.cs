using System;
using System.IO;
using System.Management.Automation.Language;

namespace PowerForge.Tests;

public sealed class ModuleMergeComposerDirectiveEscapingTests
{
    [Theory]
    [InlineData("Scripts'Generated", "using module './Types.psm1'", "using module './Scripts''Generated/Types.psm1'")]
    [InlineData("Scripts$`Generated", "using module \"./Types.psm1\"", "using module \"./Scripts`$``Generated/Types.psm1\"")]
    [InlineData("Scripts Generated", "using module ./Types.psm1", "using module './Scripts Generated/Types.psm1'")]
    [InlineData("Scripts", "using module './Types''Generated.psm1'", "using module './Scripts/Types''Generated.psm1'")]
    [InlineData("Scripts", "using module \"./Types`u{27}Generated.psm1\"", "using module \"./Scripts/Types'Generated.psm1\"")]
    public void BuildSources_EscapesRebasedUsingPathLiterals(
        string sourceFolder,
        string directive,
        string expectedDirective)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, sourceFolder));
            var referencedModuleName = directive.Contains("Types''Generated.psm1", StringComparison.Ordinal) ||
                                       directive.Contains("Types`u{27}Generated.psm1", StringComparison.Ordinal)
                ? "Types'Generated.psm1"
                : "Types.psm1";
            File.WriteAllText(Path.Combine(sourceRoot.FullName, referencedModuleName), string.Empty);
            File.WriteAllText(
                Path.Combine(sourceRoot.FullName, "Get-Demo.ps1"),
                directive + Environment.NewLine +
                "function Get-Demo { 'ok' }");

            var sources = ModuleMergeComposer.BuildSources(
                root.FullName,
                "DemoModule",
                information: new InformationConfiguration { IncludePS1 = new[] { sourceFolder } },
                exports: new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>()),
                fixRelativePaths: true);

            Assert.StartsWith(expectedDirective, sources.MergedScriptContent, StringComparison.Ordinal);
            var mergedPath = Path.Combine(root.FullName, "DemoModule.psm1");
            File.WriteAllText(mergedPath, sources.MergedScriptContent);
            Parser.ParseFile(mergedPath, out _, out var errors);
            Assert.Empty(errors);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildSources_EscapesRebasedModuleSpecificationPath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string sourceFolder = "Scripts'Generated";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, sourceFolder));
            File.WriteAllText(Path.Combine(sourceRoot.FullName, "Types.psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(sourceRoot.FullName, "Types.psd1"),
                "@{ RootModule = 'Types.psm1'; ModuleVersion = '1.0.0'; GUID = '8f41bdd6-c00a-431f-b390-c431664b9e88' }");
            File.WriteAllText(
                Path.Combine(sourceRoot.FullName, "Get-Demo.ps1"),
                "using module @{" + Environment.NewLine +
                "    ModuleName = './Types.psd1'" + Environment.NewLine +
                "    ModuleVersion = '1.0.0'" + Environment.NewLine +
                "}" + Environment.NewLine +
                "function Get-Demo { 'ok' }");

            var sources = ModuleMergeComposer.BuildSources(
                root.FullName,
                "DemoModule",
                information: new InformationConfiguration { IncludePS1 = new[] { sourceFolder } },
                exports: new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>()),
                fixRelativePaths: true);

            Assert.StartsWith(
                "using module @{" + Environment.NewLine +
                "    ModuleName = './Scripts''Generated/Types.psd1'",
                sources.MergedScriptContent,
                StringComparison.Ordinal);
            var mergedPath = Path.Combine(root.FullName, "DemoModule.psm1");
            File.WriteAllText(mergedPath, sources.MergedScriptContent);
            Parser.ParseFile(mergedPath, out _, out var errors);
            Assert.DoesNotContain(
                errors,
                error => !string.Equals(error.ErrorId, "ModuleNotFoundDuringParse", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildSources_RebasesUnquotedModuleSpecificationPath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string sourceFolder = "Scripts";
            var sourceRoot = Directory.CreateDirectory(Path.Combine(root.FullName, sourceFolder));
            File.WriteAllText(Path.Combine(sourceRoot.FullName, "Types.psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(sourceRoot.FullName, "Types.psd1"),
                "@{ RootModule = 'Types.psm1'; ModuleVersion = '1.0.0'; GUID = '8f41bdd6-c00a-431f-b390-c431664b9e88' }");
            File.WriteAllText(
                Path.Combine(sourceRoot.FullName, "Get-Demo.ps1"),
                "using module @{" + Environment.NewLine +
                "    ModuleName = ./Types.psd1" + Environment.NewLine +
                "    ModuleVersion = '1.0.0'" + Environment.NewLine +
                "}" + Environment.NewLine +
                "function Get-Demo { 'ok' }");

            var sources = ModuleMergeComposer.BuildSources(
                root.FullName,
                "DemoModule",
                information: new InformationConfiguration { IncludePS1 = new[] { sourceFolder } },
                exports: new ExportSet(new[] { "Get-Demo" }, Array.Empty<string>(), Array.Empty<string>()),
                fixRelativePaths: true);

            Assert.StartsWith(
                "using module @{" + Environment.NewLine +
                "    ModuleName = ./Scripts/Types.psd1",
                sources.MergedScriptContent,
                StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}

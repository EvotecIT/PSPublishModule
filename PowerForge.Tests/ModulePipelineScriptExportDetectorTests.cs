using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineScriptExportDetectorTests
{
    [Fact]
    public void PowerShellDetector_FindsLiteralAndHashtableDrivenScriptAliases()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias -Name LiteralAlias -Value Invoke-Literal
$aliases = [ordered] @{
    DynamicAlias = 'Invoke-Dynamic'
    'Quoted-Alias' = 'Invoke-Quoted'
}
foreach ($alias in $aliases.GetEnumerator()) {
    Set-Alias -Name $alias.Key -Value $alias.Value
}
""");

        try
        {
            var aliases = new PowerShellScriptFunctionExportDetector().DetectScriptAliases(new[] { scriptPath });

            Assert.Equal(new[] { "DynamicAlias", "LiteralAlias", "Quoted-Alias" }, aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_IgnoresAliasesDeclaredInsideFunctionBodies()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias -Name ModuleAlias -Value Invoke-ModuleCommand

function Invoke-DeferredSetup {
    Set-Alias -Name DeferredLiteralAlias -Value Invoke-DeferredLiteral
    $aliases = @{
        DeferredTableAlias = 'Invoke-DeferredTable'
    }
    foreach ($alias in $aliases.GetEnumerator()) {
        New-Alias -Name $alias.Key -Value $alias.Value
    }
}
""");

        try
        {
            var aliases = new PowerShellScriptFunctionExportDetector().DetectScriptAliases(new[] { scriptPath });

            Assert.Equal(new[] { "ModuleAlias" }, aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ResolvesModuleScopeConstantVariableAlias()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "$name = 'gfoo'; Set-Alias -Name $name -Value Get-Foo");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "gfoo" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetForUnresolvedModuleScopeAlias()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "$name = Get-AliasName; New-Alias -Name $name -Value Get-Foo");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.False(analysis.IsComplete);
            Assert.Empty(analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetForReassignedAliasVariable()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "$name = 'FirstAlias'; $name = 'SecondAlias'; Set-Alias -Name $name -Value Get-Foo");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.False(analysis.IsComplete);
            Assert.Empty(analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_RecognizesQualifiedAndBuiltInAliasCommands()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Microsoft.PowerShell.Utility\Set-Alias -Name QualifiedSet -Value Get-Foo
Microsoft.PowerShell.Utility\New-Alias -Name QualifiedNew -Value Get-Bar
sal -Name ShortSet -Value Get-Baz
nal -Name ShortNew -Value Get-Qux
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "QualifiedNew", "QualifiedSet", "ShortNew", "ShortSet" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetForComputedLiteralAssignment()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "$name = 'foo' + 1; Set-Alias -Name $name -Value Get-Foo");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.False(analysis.IsComplete);
            Assert.Empty(analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetWhenDiscoveredScriptDisappears()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"), "Missing.ps1");

        var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { missingPath });

        Assert.False(analysis.IsComplete);
        Assert.Empty(analysis.Aliases);
    }

    [Fact]
    public void FunctionDetectorContract_DoesNotRequireAliasDetection()
    {
        IScriptFunctionExportDetector detector = new RecordingScriptFunctionExportDetector("Invoke-Test");

        Assert.Equal(new[] { "Invoke-Test" }, detector.DetectScriptFunctions(Array.Empty<string>()));
        Assert.False(detector is IScriptAliasExportDetector);
    }

    [Fact]
    public void UpdateManifestForGeneratedDeliveryCommands_UsesInjectedScriptFunctionExportDetector()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(root.FullName, moduleName, "1.0.0");

            var manifestPath = Path.Combine(root.FullName, $"{moduleName}.psd1");
            var manifestMutator = new ModulePipelineMissingAnalysisServiceTests.FakeManifestMutator();
            var scriptDetector = new RecordingScriptFunctionExportDetector("Install-TestModule");
            var runner = new ModulePipelineRunner(
                new NullLogger(),
                new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(),
                new ModulePipelineMissingAnalysisServiceTests.FakeDependencyMetadataProvider(),
                new ModulePipelineMissingAnalysisServiceTests.FakeHostedOperations(),
                manifestMutator,
                new ModulePipelineMissingAnalysisServiceTests.RecordingMissingFunctionAnalysisService(new MissingFunctionAnalysisResult(
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<MissingCommandReference>(),
                    Array.Empty<string>(),
                    Array.Empty<string>())),
                scriptDetector);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "1.0.0",
                    CsprojPath = null,
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions { Enabled = false },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationOptionsSegment
                    {
                        Options = new ConfigurationOptions
                        {
                            Delivery = new DeliveryOptionsConfiguration
                            {
                                Enable = true,
                                GenerateInstallCommand = true
                            }
                        }
                    }
                }
            };

            var plan = runner.Plan(spec);
            var buildResult = new ModuleBuildResult(root.FullName, manifestPath, new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

            runner.UpdateManifestForGeneratedDeliveryCommands(plan, buildResult, packageWithoutScriptFolders: false);

            Assert.Equal(1, scriptDetector.Calls);
            var write = Assert.Single(manifestMutator.ManifestExportWrites);
            Assert.Equal(new[] { "Install-TestModule" }, write.Functions);
            Assert.Empty(write.Cmdlets);
            Assert.Empty(write.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class RecordingScriptFunctionExportDetector : IScriptFunctionExportDetector
    {
        private readonly string[] _functions;

        public RecordingScriptFunctionExportDetector(params string[] functions)
        {
            _functions = functions;
        }

        public int Calls { get; private set; }

        public IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles)
        {
            Calls++;
            return _functions;
        }

    }
}

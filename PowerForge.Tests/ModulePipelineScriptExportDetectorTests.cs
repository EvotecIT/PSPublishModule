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
    public void PowerShellDetector_UsesOnlyAliasNameArgumentForHashtableExpansion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
$aliases = @{ WrongKey = 'Get-Wrong' }
foreach ($entry in $aliases.GetEnumerator()) {
    Set-Alias -Value $entry.Key -Name FixedAlias
}
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "FixedAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ExpandsPositionalHashtableAliasNameArgument()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
$aliases = @{ PositionalAlias = 'Get-Positional' }
foreach ($entry in $aliases.GetEnumerator()) {
    Set-Alias $entry.Key $entry.Value
}
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "PositionalAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_AppliesModuleScopeAliasRemovalsInSourceOrder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias RemovedByCommand Get-One
Set-Alias RemovedByProvider Get-Two
Set-Alias RecreatedAlias Get-Old
Remove-Alias RemovedByCommand
Microsoft.PowerShell.Management\Remove-Item -LiteralPath Alias:\RemovedByProvider
Remove-Item Alias:RecreatedAlias
Set-Alias RecreatedAlias Get-New
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "RecreatedAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetForConditionalOrDynamicAliasRemoval()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var sources = new[]
        {
            "Set-Alias RetainedAlias Get-One; if ($condition) { Remove-Alias RetainedAlias }",
            "Set-Alias RetainedAlias Get-One; Remove-Item \"Alias:$name\"",
        };

        try
        {
            foreach (var source in sources)
            {
                var scriptPath = Path.Combine(root.FullName, Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(scriptPath, source);

                var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

                Assert.False(analysis.IsComplete);
                Assert.Equal(new[] { "RetainedAlias" }, analysis.Aliases);
            }
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_IgnoresRemoveItemCommandsForOtherProviders()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "Set-Alias RetainedAlias Get-One; if ($condition) { Remove-Item C:\\Temp\\file.txt }");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "RetainedAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_RecognizesRemoveItemAliasesAndSwitchesBeforePositionalPaths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias RemovedByRm Get-One
Set-Alias RemovedByForce Get-Two
Set-Alias RemovedByCommonParameter Get-Three
rm Alias:RemovedByRm
ri -Force Alias:RemovedByForce
Remove-Item -ErrorAction SilentlyContinue Alias:RemovedByCommonParameter
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Empty(analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_AppliesDeterministicHashtableForeachRemovals()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
$aliases = @{ RemovedAlias = 'Get-One' }
foreach ($entry in $aliases.GetEnumerator()) {
    Set-Alias $entry.Key $entry.Value
    Remove-Alias $entry.Key
}
Set-Alias RemovedByProviderLoop Get-Two
$paths = @{ First = 'Alias:RemovedByProviderLoop' }
foreach ($entry in $paths.GetEnumerator()) {
    Remove-Item $entry.Value
}
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Empty(analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_RejectsGatedHashtableValueAsDeterministic()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
$aliases = if ($IsWindows) { @{ WinAlias = 'Get-One' } } else { @{ UnixAlias = 'Get-Two' } }
foreach ($entry in $aliases.GetEnumerator()) { Set-Alias $entry.Key $entry.Value }
""");

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
    public void PowerShellDetector_RespectsExplicitAliasScope()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias -Name GlobalOnly -Value Get-One -Scope Global
Set-Alias -Name RetainedModuleAlias -Value Get-Two
Remove-Alias -Name RetainedModuleAlias -Scope Global -ErrorAction SilentlyContinue
Set-Alias -Name RemovedScriptAlias -Value Get-Three -Scope Script
Remove-Alias -Name RemovedScriptAlias -Scope Local
Set-Alias -Name RemovedNumericAlias -Value Get-Four -Scope 0
Remove-Alias -Name RemovedNumericAlias -Scope 0
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "RetainedModuleAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_IgnoresComputedRemoveItemPathsWithoutAliasProviderEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "Set-Alias RetainedAlias Get-One; Remove-Item -LiteralPath (Join-Path $PSScriptRoot 'temp')");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "RetainedAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ModelsWhatIfWithoutApplyingSkippedAliasChanges()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, """
Set-Alias RetainedAlias Get-One
Remove-Item -WhatIf Alias:RetainedAlias
Set-Alias -WhatIf PhantomAlias Get-Two
Set-Alias RemovedAlias Get-Three
Remove-Item -WhatIf:$false Alias:RemovedAlias
""");

        try
        {
            var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

            Assert.True(analysis.IsComplete);
            Assert.Equal(new[] { "RetainedAlias" }, analysis.Aliases);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PowerShellDetector_ReportsIncompleteSetForComputedAliasProviderPaths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var sources = new[]
        {
            "Set-Alias RetainedAlias Get-One; Remove-Item ('Alias:' + $name)",
            "$path = 'Alias:' + $name; Set-Alias RetainedAlias Get-One; Remove-Item $path",
        };

        try
        {
            foreach (var source in sources)
            {
                var scriptPath = Path.Combine(root.FullName, Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(scriptPath, source);

                var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

                Assert.False(analysis.IsComplete);
                Assert.Equal(new[] { "RetainedAlias" }, analysis.Aliases);
            }
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
    public void PowerShellDetector_ReportsIncompleteSetForControlFlowAlias()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var scriptPath = Path.Combine(root.FullName, "Aliases.ps1");
        File.WriteAllText(scriptPath, "if ($false) { Set-Alias -Name ConditionalAlias -Value Get-Foo }");

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
    public void PowerShellDetector_ReportsIncompleteSetForGatedHashtableAliases()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var sources = new[]
        {
            "$aliases = @{ GatedAlias = 'Get-Foo' }; if ($IsWindows) { foreach ($alias in $aliases.GetEnumerator()) { Set-Alias -Name $alias.Key -Value $alias.Value } }",
            "$aliases = @{ NestedAlias = 'Get-Bar' }; foreach ($alias in $aliases.GetEnumerator()) { if ($true) { Set-Alias -Name $alias.Key -Value $alias.Value } }",
            "$aliases = @{ ExpressionAlias = 'Get-Baz' }; foreach ($alias in $aliases.GetEnumerator()) { $false -and (Set-Alias -Name $alias.Key -Value $alias.Value) }",
        };

        try
        {
            foreach (var source in sources)
            {
                var scriptPath = Path.Combine(root.FullName, Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(scriptPath, source);

                var analysis = new PowerShellScriptFunctionExportDetector().AnalyzeScriptAliases(new[] { scriptPath });

                Assert.False(analysis.IsComplete);
                Assert.Empty(analysis.Aliases);
            }
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

using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class MissingFunctionsAnalyzerBoundSourceTests
{
    [Fact]
    public void Analyze_UsesTheExactApprovedModulePathAndVersion()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.BoundDonor";
            var v1 = WriteModule(root.FullName, moduleName, "1.0.0", "wrong-version");
            var v2 = WriteModule(root.FullName, moduleName, "2.0.0", "selected-version");
            Assert.True(Directory.Exists(v1));

            var service = new PowerShellMissingFunctionAnalysisService();
            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", v2) }));

            Assert.True(
                result.Functions.Any(function => function.Contains("selected-version", StringComparison.Ordinal)),
                string.Join(" | ", result.Summary.Select(command => $"{command.Name}:{command.Source}:{command.Error}")));
            Assert.DoesNotContain(result.Functions, function => function.Contains("wrong-version", StringComparison.Ordinal));
            Assert.Equal(new[] { moduleName }, result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RejectsABoundPathWhoseManifestVersionDoesNotMatch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.BoundDonor";
            var v2 = WriteModule(root.FullName, moduleName, "2.0.0", "selected-version");
            var analyzer = new MissingFunctionsAnalyzer();

            var result = analyzer.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", v2) }));

            Assert.Empty(result.Functions);
            Assert.Contains(result.Summary, command =>
                string.Equals(command.Name, "Get-PowerForgeBoundThing", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(command.Error));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RecursiveDonorAnalysisTreatsConsumerFunctionsAsAlreadyAvailable()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.BoundDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-PowerForgeBoundThing { Get-ConsumerHelper }");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "function Get-ConsumerHelper { 'consumer' }\nGet-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) }));

            Assert.Single(result.Functions);
            Assert.DoesNotContain(result.Summary, command =>
                string.Equals(command.Name, "Get-ConsumerHelper", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { moduleName }, result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RecursiveDonorAnalysisPrefersDonorHelperOverSameNamedConsumerFunction()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.BoundDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-PowerForgeBoundThing { Get-CollidingHelper }\nfunction Get-CollidingHelper { 'donor-helper' }");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "function Get-CollidingHelper { 'consumer-helper' }\nGet-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) }));

            Assert.Equal(2, result.Functions.Length);
            Assert.Contains(result.Functions, function => function.Contains("donor-helper", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Functions, function => function.Contains("consumer-helper", StringComparison.Ordinal));
            Assert.Equal(new[] { moduleName }, result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RequiredBindingsDoNotFallBackToAnApprovedModuleOnPSModulePath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var previousModulePath = Environment.GetEnvironmentVariable("PSModulePath");
        try
        {
            const string moduleName = "PowerForge.UnboundDonor";
            WriteModule(root.FullName, moduleName, "1.0.0", "must-not-inline");
            Environment.SetEnvironmentVariable(
                "PSModulePath",
                root.FullName + Path.PathSeparator + previousModulePath);

            var analyzer = new MissingFunctionsAnalyzer();
            var result = analyzer.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    requireApprovedModuleSources: true));

            Assert.Empty(result.Functions);
            Assert.Contains(result.Summary, command =>
                string.Equals(command.Source, moduleName, StringComparison.OrdinalIgnoreCase) &&
                command.Error.Contains("no concrete source binding", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", previousModulePath);
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_DynamicInvocationPreventsDonorFromBeingDeclaredFullyInlined()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.DynamicDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-PowerForgeBoundThing { $helper = 'Get-PowerForgePrivateThing'; & $helper }\nfunction Get-PowerForgePrivateThing { 'private' }");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_AcceptsBoundPrereleaseModuleIdentity()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.PrereleaseDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-PowerForgeBoundThing { 'prerelease' }",
                prerelease: "beta.1");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0-beta.1", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.Single(result.Functions);
            Assert.Equal(new[] { moduleName }, result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_PrefersBoundDonorOverAmbientCommandCollision()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.CollisionDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-Date { 'bound-donor' }",
                functionsToExport: new[] { "Get-Date" });
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-Date",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("bound-donor", StringComparison.Ordinal));
            Assert.Equal(new[] { moduleName }, result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_UsesApprovedSourceOrderWhenDonorsExportTheSameCommand()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string firstModule = "PowerForge.FirstCollisionDonor";
            const string secondModule = "PowerForge.SecondCollisionDonor";
            var firstPath = WriteModuleBody(
                root.FullName,
                firstModule,
                "2.0.0",
                "function Get-PowerForgeBoundThing { 'first-donor' }");
            var secondPath = WriteModuleBody(
                root.FullName,
                secondModule,
                "2.0.0",
                "function Get-PowerForgeBoundThing { 'second-donor' }");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { firstModule, secondModule },
                    approvedModuleSources: new[]
                    {
                        new ApprovedModuleSource(firstModule, "2.0.0", firstPath),
                        new ApprovedModuleSource(secondModule, "2.0.0", secondPath)
                    },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("first-donor", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Functions, function => function.Contains("second-donor", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_ContinuesPastApprovedSourceThatDoesNotExportTheCommand()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string emptyModule = "PowerForge.EmptyDonor";
            const string matchingModule = "PowerForge.MatchingDonor";
            var emptyPath = WriteModuleBody(
                root.FullName,
                emptyModule,
                "2.0.0",
                "function Get-PowerForgeOtherThing { 'other' }",
                functionsToExport: new[] { "Get-PowerForgeOtherThing" });
            var matchingPath = WriteModule(
                root.FullName,
                matchingModule,
                "2.0.0",
                "matching-donor");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { emptyModule, matchingModule },
                    approvedModuleSources: new[]
                    {
                        new ApprovedModuleSource(emptyModule, "2.0.0", emptyPath),
                        new ApprovedModuleSource(matchingModule, "2.0.0", matchingPath)
                    },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("matching-donor", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("PowerForge.RuntimeDonor\\Get-PowerForgeBoundThing")]
    [InlineData("using module PowerForge.RuntimeDonor\nGet-PowerForgeBoundThing")]
    [InlineData("param([PowerForge.RuntimeDonor.Widget]$Value)\nGet-PowerForgeBoundThing")]
    public void Analyze_KeepsDonorDependencyForNonInlineableModuleReferences(string consumerCode)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.RuntimeDonor";
            var modulePath = WriteModule(root.FullName, moduleName, "2.0.0", "runtime-reference");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: consumerCode,
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RejectsBoundModuleWhoseGuidDoesNotMatch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.GuidDonor";
            const string actualGuid = "11111111-1111-1111-1111-111111111111";
            const string expectedGuid = "22222222-2222-2222-2222-222222222222";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                "function Get-PowerForgeBoundThing { 'guid' }",
                guid: actualGuid);
            var analyzer = new MissingFunctionsAnalyzer();

            var result = analyzer.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath, expectedGuid) },
                    requireApprovedModuleSources: true));

            Assert.Empty(result.Functions);
            Assert.Contains(result.Summary, command => command.Error.Contains("GUID", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("function Get-PowerForgeBoundThing { $helper = 'Get-PowerForgePrivateThing'; Invoke-Expression $helper }", "Invoke-Expression")]
    [InlineData("function Get-PowerForgeBoundThing { $helper = 'Get-PowerForgePrivateThing'; iex $helper }", "iex")]
    [InlineData("function Get-PowerForgeBoundThing { [scriptblock]::Create('Get-PowerForgePrivateThing').Invoke() }", "ScriptBlock.Create")]
    public void Analyze_RuntimeCodeEvaluationPreventsDonorFromBeingDeclaredFullyInlined(string donorFunction, string scenario)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.RuntimeCodeDonor";
            var modulePath = WriteModuleBody(
                root.FullName,
                moduleName,
                "2.0.0",
                donorFunction + "\nfunction Get-PowerForgePrivateThing { 'private' }");
            var service = new PowerShellMissingFunctionAnalysisService();

            var result = service.Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "2.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.True(result.Functions.Length > 0, scenario);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string WriteModule(string root, string moduleName, string version, string marker)
        => WriteModuleBody(
            root,
            moduleName,
            version,
            $"function Get-PowerForgeBoundThing {{ '{marker}' }}{Environment.NewLine}");

    [Fact]
    public void Analyze_RecursiveResolutionPrefersTheOriginatingDonor()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string firstModule = "PowerForge.FirstHelperDonor";
            const string secondModule = "PowerForge.SecondHelperDonor";
            var firstPath = WriteModuleBody(
                root.FullName,
                firstModule,
                "2.0.0",
                "function Invoke-PowerForgeSharedHelper { 'wrong-helper' }",
                functionsToExport: new[] { "Invoke-PowerForgeSharedHelper" });
            var secondPath = WriteModuleBody(
                root.FullName,
                secondModule,
                "2.0.0",
                "function Get-PowerForgeBoundThing { Invoke-PowerForgeSharedHelper }\nfunction Invoke-PowerForgeSharedHelper { 'right-helper' }");

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { firstModule, secondModule },
                    includeFunctionsRecursively: true,
                    approvedModuleSources: new[]
                    {
                        new ApprovedModuleSource(firstModule, "2.0.0", firstPath),
                        new ApprovedModuleSource(secondModule, "2.0.0", secondPath)
                    },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("right-helper", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Functions, function => function.Contains("wrong-helper", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_ImportsManifestlessScriptModule()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.ManifestlessDonor";
            var modulePath = Path.Combine(root.FullName, moduleName);
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(
                Path.Combine(modulePath, moduleName + ".psm1"),
                "function Get-PowerForgeBoundThing { 'manifestless' }\nExport-ModuleMember -Function Get-PowerForgeBoundThing\n");

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, null, modulePath) },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("manifestless", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_AddsDownloadedDonorRootToModuleSearchPath()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string dependencyName = "PowerForge.DonorDependency";
            var dependencyPath = WriteModuleBody(
                root.FullName,
                dependencyName,
                "1.0.0",
                "function Get-PowerForgeDependencyValue { 'dependency' }",
                functionsToExport: new[] { "Get-PowerForgeDependencyValue" });
            Assert.True(Directory.Exists(dependencyPath));

            const string donorName = "PowerForge.DependentDonor";
            var donorPath = WriteModuleBody(root.FullName, donorName, "2.0.0", "function Get-PowerForgeBoundThing { Get-PowerForgeDependencyValue }");
            var manifestPath = Path.Combine(donorPath, donorName + ".psd1");
            File.WriteAllText(
                manifestPath,
                $"@{{ RootModule = '{donorName}.psm1'; ModuleVersion = '2.0.0'; RequiredModules = @(@{{ ModuleName = '{dependencyName}'; RequiredVersion = '1.0.0' }}); FunctionsToExport = @('Get-PowerForgeBoundThing') }}\n");

            var originalModulePath = Environment.GetEnvironmentVariable("PSModulePath");
            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: "Get-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { donorName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(donorName, "2.0.0", donorPath, moduleSearchRoot: root.FullName) },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("Get-PowerForgeDependencyValue", StringComparison.Ordinal));
            Assert.Equal(originalModulePath, Environment.GetEnvironmentVariable("PSModulePath"));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RetainsBinaryDonorForTypeOutsideModuleNamespace()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.BinaryTypeDonor";
            var modulePath = Path.Combine(root.FullName, moduleName, "1.0.0");
            Directory.CreateDirectory(modulePath);
            var assemblyPath = BuildTypeFixture(root.FullName);
            File.Copy(assemblyPath, Path.Combine(modulePath, "Contoso.Types.dll"));
            File.WriteAllText(
                Path.Combine(modulePath, moduleName + ".psm1"),
                "Add-Type -Path (Join-Path $PSScriptRoot 'Contoso.Types.dll')\nfunction Get-PowerForgeBoundThing { 'binary-type' }\n");
            File.WriteAllText(
                Path.Combine(modulePath, moduleName + ".psd1"),
                $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-PowerForgeBoundThing') }}\n");

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: "param([ContosoWidget]$Value)\nGet-PowerForgeBoundThing",
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("Import-Module {0} -ErrorAction Stop")]
    [InlineData("Import-Module -Force {0} -ErrorAction Stop")]
    [InlineData("Import-Module -Force -Global {0}")]
    [InlineData("Import-Module -Prefix PF {0}")]
    [InlineData("ipmo -Force {0}")]
    [InlineData("Import-Module -Name '{0}' -ErrorAction Stop")]
    [InlineData("Microsoft.PowerShell.Core\\Import-Module -FullyQualifiedName @{{ ModuleName = '{0}'; RequiredVersion = '1.0.0' }}")]
    public void Analyze_RetainsDonorImportedExplicitlyAtRuntime(string importTemplate)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.RuntimeImportedDonor";
            var modulePath = WriteModule(root.FullName, moduleName, "1.0.0", "runtime-import");
            var code = string.Format(importTemplate, moduleName) + Environment.NewLine + "Get-PowerForgeBoundThing";

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: code,
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Analyze_RetainsDonorDeclaredByRequiresModules()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.RequiresDonor";
            var modulePath = WriteModule(root.FullName, moduleName, "1.0.0", "requires-module");
            var code = $"#requires -Modules @{{ ModuleName = '{moduleName}'; RequiredVersion = '1.0.0' }}{Environment.NewLine}Get-PowerForgeBoundThing";

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: code,
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("$name = '{0}'\nImport-Module $name -ErrorAction Stop")]
    [InlineData("$name = '{0}'\nImport-Module -Force $name")]
    [InlineData("$name = '{0}'\nImport-Module -Name $name")]
    [InlineData("$name = '{0}'\nImport-Module 'Unrelated.Static.Module', $name")]
    [InlineData("$name = '{0}'\nipmo $name")]
    [InlineData("$params = @{{ Name = '{0}' }}\nImport-Module @params")]
    public void Analyze_RetainsDonorWhenImportModuleTargetIsDynamic(string importTemplate)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.DynamicImportDonor";
            var modulePath = WriteModule(root.FullName, moduleName, "1.0.0", "dynamic-import");
            var code = string.Format(importTemplate, moduleName) + Environment.NewLine + "Get-PowerForgeBoundThing";

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(
                filePath: null,
                code: code,
                options: new MissingFunctionsOptions(
                    approvedModules: new[] { moduleName },
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.NotEmpty(result.Functions);
            Assert.Empty(result.FullyInlinedApprovedModules);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildTypeFixture(string root)
    {
        var projectRoot = Path.Combine(root, "Contoso.Types.Source");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(
            Path.Combine(projectRoot, "Contoso.Types.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>Contoso.Types</AssemblyName></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projectRoot, "ContosoWidget.cs"), "public sealed class ContosoWidget { }");
        var start = new ProcessStartInfo("dotnet", "build -c Release --nologo")
        {
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
        return Path.Combine(projectRoot, "bin", "Release", "net8.0", "Contoso.Types.dll");
    }

    private static string WriteModuleBody(
        string root,
        string moduleName,
        string version,
        string moduleBody,
        string? prerelease = null,
        string? guid = null,
        string[]? functionsToExport = null)
    {
        var modulePath = Path.Combine(root, moduleName, version);
        Directory.CreateDirectory(modulePath);
        File.WriteAllText(
            Path.Combine(modulePath, moduleName + ".psm1"),
            moduleBody + Environment.NewLine);
        var privateData = string.IsNullOrWhiteSpace(prerelease)
            ? string.Empty
            : $"; PrivateData = @{{ PSData = @{{ Prerelease = '{prerelease}' }} }}";
        var guidEntry = string.IsNullOrWhiteSpace(guid) ? string.Empty : $"; GUID = '{guid}'";
        var exports = string.Join(", ", (functionsToExport ?? new[] { "Get-PowerForgeBoundThing" }).Select(static name => $"'{name}'"));
        File.WriteAllText(
            Path.Combine(modulePath, moduleName + ".psd1"),
            $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '{version}'; FunctionsToExport = @({exports}){guidEntry}{privateData} }}{Environment.NewLine}");
        return modulePath;
    }
}

using PowerForge;
using System.Diagnostics;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridModuleWithNoTypedFunctionsPreservesCompletePowerShellFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FallbackOnly { return [int](Get-Date -Format yyyy) }",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.FallbackOnly",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        var proof = RunModuleProof(result.ArtifactPath!, "[int](Get-FallbackOnly) -gt 2000");
        Assert.Equal("True", proof);
    }

    [Fact]
    public void Build_HybridModuleKeepsMultilineNonCmdletFunctionOnFallback()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function HelperProof
            {
                return [int](Get-Date -Format yyyy)
            }
            """,
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MultilineFallback",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal("True", RunModuleProof(result.ArtifactPath!, "[int](HelperProof) -gt 2000"));
    }

    [Fact]
    public void Build_HybridModuleReplacesMultilineTypedFunctionWithCompiledCmdlet()
    {
        using var fixture = ArtifactFixture.Create(
            "    function Get-MultilineTyped" + Environment.NewLine +
            "    {" + Environment.NewLine +
            "        return 42" + Environment.NewLine +
            "    }" + Environment.NewLine +
            "    Export-ModuleMember -Function Get-MultilineTyped",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MultilineTyped",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal("Cmdlet:42", RunModuleProof(
            result.ArtifactPath!,
            "(Get-Command Get-MultilineTyped).CommandType.ToString() + ':' + (Get-MultilineTyped)"));
    }

    [Fact]
    public void Build_FileListDotSourceIsTransformedByHybridComposerWithoutCollision()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Public/Get-Value.ps1\"; Export-ModuleMember -Function Get-Value",
            ".psm1");
        var publicDirectory = Path.Combine(fixture.RootPath, "Public");
        Directory.CreateDirectory(publicDirectory);
        File.WriteAllText(Path.Combine(publicDirectory, "Get-Value.ps1"), "function Get-Value { return 42 }");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-Value'); CmdletsToExport = @(); FileList = @('Public/Get-Value.ps1') }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.FileListComposition");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var stagedDependency = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "Public", "Get-Value.ps1");
        Assert.True(File.Exists(stagedDependency));
        Assert.DoesNotContain("function Get-Value", File.ReadAllText(stagedDependency), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("42", RunModuleProof(result.ArtifactPath!, "Get-Value"));
    }

    [Fact]
    public void Build_ConventionalModuleLoaderCompilesAndStagesDiscoveredFunctionFiles()
    {
        using var fixture = ArtifactFixture.Create(
            """
            $Public = @(Get-ChildItem -Path $PSScriptRoot\Public\*.ps1 -Recurse)
            $Private = @(Get-ChildItem -Path $PSScriptRoot\Private\*.ps1 -Recurse)
            foreach ($Import in @($Private + $Public)) { . $Import.FullName }
            Export-ModuleMember -Function Get-TypedValue, Get-FallbackValue
            """,
            ".psm1");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Public"));
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Private"));
        File.WriteAllText(Path.Combine(fixture.RootPath, "Public", "Get-TypedValue.ps1"), "function Get-TypedValue { return 42 }");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Private", "Get-FallbackValue.ps1"), "function Get-FallbackValue { return [int](Get-Date -Format yyyy) }");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-TypedValue', 'Get-FallbackValue'); CmdletsToExport = @() }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.ConventionalModule");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        var outputRoot = Path.GetDirectoryName(result.ArtifactPath!)!;
        var typedSource = Path.Combine(outputRoot, "Public", "Get-TypedValue.ps1");
        var fallbackSource = Path.Combine(outputRoot, "Private", "Get-FallbackValue.ps1");
        Assert.True(File.Exists(typedSource));
        Assert.True(File.Exists(fallbackSource));
        Assert.DoesNotContain("function Get-TypedValue", File.ReadAllText(typedSource), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("function Get-FallbackValue", File.ReadAllText(fallbackSource), StringComparison.OrdinalIgnoreCase);
        var proof = RunModuleProof(result.ArtifactPath!, "Get-TypedValue; [int](Get-FallbackValue) -gt 2000");
        Assert.Equal(new[] { "42", "True" }, proof.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_PreservesFunctionsExportedByNestedScriptModule()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RootValue { return 1 }; Export-ModuleMember -Function Get-RootValue",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Nested.psm1"), "function Get-NestedValue { return 9 }; Export-ModuleMember -Function Get-NestedValue");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('Nested.psm1'); FunctionsToExport = @('Get-RootValue', 'Get-NestedValue'); CmdletsToExport = @() }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.NestedScript");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "9" }, RunModuleProof(result.ArtifactPath!, "Get-RootValue; Get-NestedValue").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StagesNestedManifestRootAndTransitiveDotSourceClosure()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RootValue { return 1 }; Export-ModuleMember -Function Get-RootValue",
            ".psm1");
        var nested = Path.Combine(fixture.RootPath, "Nested");
        Directory.CreateDirectory(Path.Combine(nested, "Public"));
        File.WriteAllText(Path.Combine(nested, "Nested.psd1"), "@{ RootModule = 'Nested.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-NestedValue') }");
        File.WriteAllText(Path.Combine(nested, "Nested.psm1"), ". \"$PSScriptRoot/Public/Get-NestedValue.ps1\"; Export-ModuleMember -Function Get-NestedValue");
        File.WriteAllText(Path.Combine(nested, "Public", "Get-NestedValue.ps1"), "function Get-NestedValue { return 17 }");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('Nested/Nested.psd1'); FunctionsToExport = @('Get-RootValue', 'Get-NestedValue'); CmdletsToExport = @() }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.NestedManifest");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var outputRoot = Path.GetDirectoryName(result.ArtifactPath!)!;
        Assert.True(File.Exists(Path.Combine(outputRoot, "Nested", "Nested.psd1")));
        Assert.True(File.Exists(Path.Combine(outputRoot, "Nested", "Nested.psm1")));
        Assert.True(File.Exists(Path.Combine(outputRoot, "Nested", "Public", "Get-NestedValue.ps1")));
        Assert.Equal(new[] { "1", "17" }, RunModuleProof(result.ArtifactPath!, "Get-RootValue; Get-NestedValue").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_PreservesOmittedFunctionExportsForNestedScriptModule()
    {
        using var fixture = ArtifactFixture.Create("function Get-RootValue { return 1 }", ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Nested.psm1"), "function Get-NestedDefault { return 23 }");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('Nested.psm1'); CmdletsToExport = @() }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.NestedDefaults");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.DoesNotContain("FunctionsToExport", File.ReadAllText(result.ArtifactPath!), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "1", "23" }, RunModuleProof(result.ArtifactPath!, "Get-RootValue; Get-NestedDefault").Split(Environment.NewLine));
    }

    [Fact]
    public void Build_DualRoleRuntimeHookStaysUncompiledAndIsPresentInArtifact()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Shared.ps1\"; Export-ModuleMember -Function Get-SharedValue",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Shared.ps1"), "function Get-SharedValue { return 42 }");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; ScriptsToProcess = @('Shared.ps1'); FunctionsToExport = @('Get-SharedValue'); CmdletsToExport = @() }");

        var result = BuildResolvedModule(fixture.RootPath, fixture.OutputPath, "PowerForge.DualRoleHook");

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        var staged = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "Shared.ps1");
        Assert.True(File.Exists(staged));
        Assert.Contains("function Get-SharedValue", File.ReadAllText(staged), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("42", RunModuleProof(result.ArtifactPath!, "Get-SharedValue"));
    }

    [Fact]
    public void Build_RejectsExplicitCompilationSourceThatIsAlsoManifestRuntimeHook()
    {
        using var fixture = ArtifactFixture.Create(". \"$PSScriptRoot/Shared.ps1\"", ".psm1");
        var shared = Path.Combine(fixture.RootPath, "Shared.ps1");
        File.WriteAllText(shared, "function Get-SharedValue { return 42 }");
        var manifest = Path.ChangeExtension(fixture.ScriptPath, ".psd1");
        File.WriteAllText(manifest, "@{ RootModule = 'input.psm1'; ScriptsToProcess = @('Shared.ps1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvalidDualOwner",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifest,
            CompilationSourcePaths = new[] { fixture.ScriptPath, shared }
        });

        Assert.False(result.Succeeded);
        Assert.Contains("both an explicit compilation source and a manifest runtime hook", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_UsesExplicitManifestWhenPreservingNestedModuleFunctionExports()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RootValue { return 1 }; Export-ModuleMember -Function Get-RootValue",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Nested.psm1"), "function Get-NestedValue { return 9 }; Export-ModuleMember -Function Get-NestedValue");
        var manifest = Path.Combine(fixture.RootPath, "Product.psd1");
        File.WriteAllText(
            manifest,
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; NestedModules = @('Nested.psm1'); FunctionsToExport = @('Get-RootValue', 'Get-NestedValue'); CmdletsToExport = @() }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ExplicitManifestNested",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = manifest,
            CompilationSourcePaths = new[] { fixture.ScriptPath }
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "1", "9" }, RunModuleProof(result.ArtifactPath!, "Get-RootValue; Get-NestedValue").Split(Environment.NewLine));
    }

    private static PowerShellCompilationBuildResult BuildResolvedModule(string inputPath, string outputPath, string artifactName)
    {
        var resolved = new PowerShellCompilationInputResolver().Resolve(inputPath);
        return new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            outputPath,
            artifactName,
            resolved.Kind,
            resolved.Mode, allowUnreviewedDependencyResolution: true)
        {
            ModuleManifestPath = resolved.ModuleManifestPath,
            CompilationSourcePaths = resolved.CompilationSourceFiles
        });
    }

    private static string RunModuleProof(string modulePath, string command)
        => RunModuleProof(modulePath, command, "pwsh");

    private static string RunModuleProof(string modulePath, string command, string host)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = host,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($"Import-Module -Name '{modulePath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {command}");
        using var process = Process.Start(startInfo)!;
        var rawOutput = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Module rebuild proof did not exit within 60 seconds.");
        Assert.True(process.ExitCode == 0, error + Environment.NewLine + rawOutput);
        Assert.True(string.IsNullOrWhiteSpace(error), error);
        return string.Join(
            Environment.NewLine,
            rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
}

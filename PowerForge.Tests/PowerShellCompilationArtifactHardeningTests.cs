using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    public void Build_PackagedExecutablePreservesOrderedOutputPathSemanticsAndValueValidation()
    {
        using var fixture = ArtifactFixture.Create(
            """
            [CmdletBinding()]
            param([string] $Name, [switch] $Force)
            Write-Host 'host-' -NoNewline
            Write-Host 'joined'
            Write-Information 'information-before' -InformationAction Continue
            "output:$Name"
            "switch:$($Force.IsPresent)"
            Write-Information 'information-after' -InformationAction Continue
            "root:$PSScriptRoot"
            "path:$PSCommandPath"
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedSemantics",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var success = Run(result.ArtifactPath!, "--Name", "Ada", "--Force:$false");
        Assert.True(success.ExitCode == 0, success.StandardError + Environment.NewLine + success.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(success.StandardError), success.StandardError);
        var lines = success.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("host-joined", lines[0]);
        Assert.Equal("information-before", lines[1]);
        Assert.Equal("output:Ada", lines[2]);
        Assert.Equal("switch:False", lines[3]);
        Assert.Equal("information-after", lines[4]);
        AssertPathsEqual(Path.GetDirectoryName(result.ArtifactPath!)!, lines[5].Substring("root:".Length));
        AssertPathsEqual(result.ArtifactPath!, lines[6].Substring("path:".Length));

        var missingValue = Run(result.ArtifactPath!, "--Name", "--Verbose");
        Assert.Equal(1, missingValue.ExitCode);
        Assert.Contains("requires a value", missingValue.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_BinaryModuleRejectsPowerShellCommonParameterCollision()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-Collision {
                param([string] $Verbose)
                return $Verbose
            }
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CommonParameterCollision",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("common parameter", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsManifestRuntimeScriptHooks()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, "initialize.ps1"), "Set-Variable -Name Initialized -Value $true -Scope Script");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); ScriptsToProcess = @('initialize.ps1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictManifestHook",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("runtime script hook", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsNonLiteralManifestRuntimeScriptHooks()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); ScriptsToProcess = @($PSScriptRoot + '\\initialize.ps1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictDynamicManifestHook",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("literal string", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridBinaryModuleReportsManifestRuntimeScriptHooksAsFallback()
    {
        using var fixture = ArtifactFixture.Create("function Get-PublicValue { return 1 }", ".psm1");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.ScriptPath)!, "initialize.ps1"), "Set-Variable -Name Initialized -Value $true -Scope Script");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; FunctionsToExport = @('Get-PublicValue'); CmdletsToExport = @(); VariablesToExport = @(); AliasesToExport = @(); ScriptsToProcess = @('initialize.ps1') }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridManifestHook",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.Contains(result.Manifest.Files, file => file.Path.EndsWith("initialize.ps1", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("ProcessRecord")]
    [InlineData("WriteObject")]
    public void Build_BinaryModuleRejectsGeneratedOrInheritedMemberCollision(string parameterName)
    {
        using var fixture = ArtifactFixture.Create($"function Get-Collision {{ param([string] ${parameterName}); return ${parameterName} }}");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MemberCollision",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("binary-cmdlet member", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_BinaryModuleRejectsParameterMatchingGeneratedClassName()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Collision { param([string] $GetCollisionCommand); return $GetCollisionCommand }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ClassMemberCollision",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("binary-cmdlet member", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_BinaryModuleRejectsDuplicateSanitizedClassNames()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Ab { return 1 }; function GetA-b { return 2 }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DuplicateCmdletClass",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("duplicate binary-cmdlet class", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_RejectsExecutableOnlyPublicationOptionsForLibrary()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.LibraryOptions",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            SelfContained = true
        };

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains("executable-only", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_RejectsSuffixBearingArtifactNameWithoutClaimingSiblingFiles()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var builder = new PowerShellCompilationArtifactBuilder();
        var sibling = builder.Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Sibling",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));
        Assert.True(sibling.Succeeded, sibling.Error + Environment.NewLine + sibling.BuildOutput);
        var artifactHash = Hash(sibling.ArtifactPath!);
        var manifestHash = Hash(sibling.ManifestPath!);

        var exception = Assert.Throws<ArgumentException>(() => builder.Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Sibling.dll",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)));

        Assert.Contains("generated artifact suffix", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(artifactHash, Hash(sibling.ArtifactPath!));
        Assert.Equal(manifestHash, Hash(sibling.ManifestPath!));
    }

    [Fact]
    public void Build_HybridModuleKeepsConditionalFunctionsOnPowerShellFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "if ($false) { function Get-ConditionalValue { return 1 } }; function Get-TopValue { return 2 }; Export-ModuleMember -Function @('Get-TopValue')");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalFunction",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-ConditionalValue -ErrorAction SilentlyContinue); Get-TopValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleDoesNotTreatConditionalExportAsUnconditional()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HiddenValue { return 1 }; function Get-PublicValue { return 2 }; if ($false) { Export-ModuleMember -Function Get-HiddenValue }; Export-ModuleMember -Function Get-PublicValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-HiddenValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "True", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesConditionalOnlyExportSurface()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-HiddenValue { return 1 }; function Get-PublicValue { return 2 }; if ($true) { Export-ModuleMember -Function Get-PublicValue }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalOnlyExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-HiddenValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "True", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesDefaultExportsWhenConditionalOnlyExportDoesNotRun()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FirstValue { return 1 }; function Get-SecondValue { return 2 }; if ($false) { Export-ModuleMember -Function Get-SecondValue }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConditionalFalseExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-FirstValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-SecondValue -ErrorAction SilentlyContinue); Get-FirstValue; Get-SecondValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "True", "True", "1", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModulePreservesColonAttachedLiteralExport()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PublicValue { return 1 }; function Get-PrivateValue { return 2 }; Export-ModuleMember -Function:Get-PublicValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AttachedExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-PublicValue -ErrorAction SilentlyContinue); [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "True", "False", "1" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_StrictTypedArtifactRejectsSourceRequiresBeforeDotNetCompilation()
    {
        using var fixture = ArtifactFixture.Create("#Requires -RunAsAdministrator" + Environment.NewLine + "param([int] $Value)" + Environment.NewLine + "return $Value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SourceRequires",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("#requires", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_LibraryAvoidsMethodContainerNameCollision()
    {
        using var fixture = ArtifactFixture.Create("function FooMethods { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Foo",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFile(result.ArtifactPath!);
        var type = assembly.GetType("PowerForge.Compiled._FooMethods", throwOnError: true)!;
        Assert.Equal(42, type.GetMethod("FooMethods")!.Invoke(null, null));
    }

    [Fact]
    public void Build_Net472RejectsRuntimeTypeUnavailableToRequestedTargetBeforeDotNetCompilation()
    {
        using var fixture = ArtifactFixture.Create("function Get-DateValue { return [System.DateOnly]::MinValue }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TargetFrameworkType",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net472"
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("reference set", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_Net472RejectsFrameworkTypeOutsideGeneratedProjectReferencesBeforeDotNetCompilation()
    {
        using var fixture = ArtifactFixture.Create("function Get-EncodedValue { return [System.Web.HttpUtility]::HtmlEncode('value') }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.UnreferencedFrameworkType",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net472"
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("reference set", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(result.BuildOutput), result.BuildOutput);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModuleRetainsPrivateTypedHelperNeededByFallbackFunction()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PrivateValue { return 7 }; function Get-PublicValue { return Get-PrivateValue }; Export-ModuleMember -Function @('Get-PublicValue')",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PrivateHelper",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(2, result.Manifest.RuntimeFallbackUnits);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; [bool](Get-Command Get-PrivateValue -ErrorAction SilentlyContinue); Get-PublicValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "False", "7" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Theory]
    [InlineData(PowerShellCompilationArtifactKind.Library)]
    [InlineData(PowerShellCompilationArtifactKind.BinaryModule)]
    public void Build_StrictDllRejectsEligibleTopLevelUnitThatCannotBeEmitted(PowerShellCompilationArtifactKind kind)
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }; return 2");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TopLevelOmission",
            kind,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("top-level script unit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_ReplacesTheCompleteArtifactShapeWithoutLeavingPriorFiles()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Get-TypedValue {
                param([int] $Value)
                return $Value
            }
            function Get-DynamicValue {
                param([string] $Path)
                return Get-Item -LiteralPath $Path
            }
            """);
        var librarySpec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ShapeReplacement",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Hybrid);
        var library = new PowerShellCompilationArtifactBuilder().Build(librarySpec);
        Assert.True(library.Succeeded, library.Error + Environment.NewLine + library.BuildOutput);
        var previousLibraryPath = library.ArtifactPath!;
        Assert.True(File.Exists(previousLibraryPath));

        var moduleSpec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ShapeReplacement",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid);
        var module = new PowerShellCompilationArtifactBuilder().Build(moduleSpec);

        Assert.True(module.Succeeded, module.Error + Environment.NewLine + module.BuildOutput);
        Assert.False(File.Exists(previousLibraryPath));
        Assert.True(Directory.Exists(Path.Combine(fixture.OutputPath, "PowerForge.ShapeReplacement")));
        Assert.All(module.Manifest!.Files, file => Assert.True(File.Exists(file.Path), file.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath, ".PowerForge.ShapeReplacement.artifact-*"));
    }

    [Fact]
    public void Build_RestoresPreviousArtifactSetWhenDurableCommitFails()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.AtomicRollback",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict);
        var first = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
        var originalArtifactHash = Hash(first.ArtifactPath!);
        var originalManifestHash = Hash(first.ManifestPath!);
        File.WriteAllText(fixture.ScriptPath, "function Get-Value { return 2 }");

        PowerShellCompilationBuildResult failed;
        using (new FileStream(first.ManifestPath!, FileMode.Open, FileAccess.Read, FileShare.None))
            failed = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(failed.Succeeded);
        Assert.Contains("previous durable artifact set was restored", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(originalArtifactHash, Hash(first.ArtifactPath!));
        Assert.Equal(originalManifestHash, Hash(first.ManifestPath!));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath, ".PowerForge.AtomicRollback.artifact-*"));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "Packaged executable did not exit within 60 seconds.");
        return (process.ExitCode, standardOutput, standardError);
    }

    private static void AssertPathsEqual(string expected, string actual)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Assert.True(Path.GetFullPath(expected).Equals(Path.GetFullPath(actual), comparison), $"Expected '{expected}', got '{actual}'.");
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class ArtifactFixture : IDisposable
    {
        private ArtifactFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        public string RootPath { get; }
        public string ScriptPath { get; }
        public string OutputPath { get; }

        public static ArtifactFixture Create(string source, string extension = ".ps1")
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(rootPath, "output");
            Directory.CreateDirectory(outputPath);
            var scriptPath = Path.Combine(rootPath, "input" + extension);
            File.WriteAllText(scriptPath, source);
            return new ArtifactFixture(rootPath, scriptPath, outputPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }
}

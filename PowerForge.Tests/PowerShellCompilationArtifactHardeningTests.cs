using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModulePreservesMandatoryMetadataForTypedHelper(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        using var fixture = ArtifactFixture.Create(
            """
            function Convert-IpAddressToPtrString {
                [CmdletBinding()]
                param([Parameter(Mandatory = $true)] [string] $IPAddress)
                $octets = $IPAddress -split "\."
                [array]::Reverse($octets)
                $ptrString = ($octets -join ".") + ".in-addr.arpa"
                $ptrString
            }
            Export-ModuleMember -Function Convert-IpAddressToPtrString
            """,
            ".psm1");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PowerInfoBloxHelper",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; $p=(Get-Command Convert-IpAddressToPtrString).Parameters['IPAddress']; $p.Attributes.Mandatory; Convert-IpAddressToPtrString -IPAddress '192.168.1.20'");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "True", "20.1.168.192.in-addr.arpa" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

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
            "invocation:$($MyInvocation.MyCommand.Path)"
            "definition:$($MyInvocation.MyCommand.Definition)"
            function Get-NestedInvocationPath { "nested:$($MyInvocation.MyCommand.Path)" }
            Get-NestedInvocationPath
            """);
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedSemantics",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true);

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
        AssertPathsEqual(result.ArtifactPath!, lines[7].Substring("invocation:".Length));
        AssertPathsEqual(result.ArtifactPath!, lines[8].Substring("definition:".Length));
        Assert.Equal("nested:", lines[9]);

        var missingValue = Run(result.ArtifactPath!, "--Name", "--Verbose");
        Assert.Equal(1, missingValue.ExitCode);
        Assert.Contains("requires a value", missingValue.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PackagedExecutableRewritesInvocationPathInsideExitExpressionWithoutOverlap()
    {
        using var fixture = ArtifactFixture.Create("exit [int]($MyInvocation.MyCommand.Path -eq $PSCommandPath)");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedExitPath",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal(1, run.ExitCode);
        Assert.DoesNotContain("ParserError", run.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_PackagedExecutableRejectsFileResolvedUsingDirective()
    {
        using var fixture = ArtifactFixture.Create("using module ./Helper.psm1; Get-HelperValue");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.psm1"), "function Get-HelperValue { return 9 }; Export-ModuleMember -Function Get-HelperValue");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PackagedUsingModule",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("using module/assembly directives", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutableFormatsStructuredPipelineOutputLikePowerShell()
    {
        using var fixture = ArtifactFixture.Create("[pscustomobject]@{ Name = 'Ada'; Count = 2 }");
        var original = Run("pwsh", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath);
        Assert.Equal(0, original.ExitCode);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StructuredOutput",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var packaged = Run(result.ArtifactPath!);
        Assert.Equal(0, packaged.ExitCode);
        static string[] Lines(string value) => value
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.TrimEnd())
            .ToArray();
        Assert.Equal(Lines(original.StandardOutput), Lines(packaged.StandardOutput));
        Assert.DoesNotContain("@{Name=Ada", packaged.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HybridModuleAcceptsAlreadyCorrectGeneratedRootModule()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 1 }; function Get-FallbackValue { Write-Output 2 }; Export-ModuleMember -Function @('Get-TypedValue', 'Get-FallbackValue')",
            ".psm1");
        File.WriteAllText(
            Path.ChangeExtension(fixture.ScriptPath, ".psd1"),
            "@{ RootModule = 'input.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-TypedValue', 'Get-FallbackValue'); CmdletsToExport = @() }");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "input",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-TypedValue; Get-FallbackValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "1", "2" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true);

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true);

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true);

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.False(result.Succeeded);
        Assert.Contains("duplicate binary-cmdlet class", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_HybridModuleRoutesNonCmdletFunctionNameToScriptFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TypedValue { return 7 }; function Helper" + Environment.NewLine + "{ return 11 }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NonCmdletFallback",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        Assert.True(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("Verb-Noun", StringComparison.OrdinalIgnoreCase));
        var assemblyPath = Path.Combine(Path.GetDirectoryName(result.ArtifactPath!)!, "PowerForge.NonCmdletFallback.dll");
        var assembly = System.Reflection.Assembly.LoadFile(assemblyPath);
        var methodContainer = assembly.GetTypes().Single(type =>
            type.GetMethod("Get_TypedValue", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) is not null);
        Assert.Null(methodContainer.GetMethod("Helper", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-TypedValue; Helper");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "7", "11" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Build_HybridModuleRoutesDuplicateConditionalFunctionNameToFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "if ($true) { function Get-Value { return 1 } }; function Get-Value { return 2 }; function Get-TypedValue { return 3 }; Export-ModuleMember -Function @('Get-Value', 'Get-TypedValue')",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.DuplicateFunctionFallback",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("multiple retained definitions", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; (Get-Command Get-Value).CommandType; Get-Value; (Get-Command Get-TypedValue).CommandType; Get-TypedValue");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "Function", "2", "Cmdlet", "3" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    public void Signing_SelectsOnlyBuildOwnedArtifacts()
    {
        var files = new[]
        {
            new PowerShellCompilationArtifactFile { Path = "tool.exe", Role = "Primary" },
            new PowerShellCompilationArtifactFile { Path = "module.psm1", Role = "PrimaryModule" },
            new PowerShellCompilationArtifactFile { Path = "typed.dll", Role = "TypedAssembly" },
            new PowerShellCompilationArtifactFile { Path = "tool.dll", Role = "GeneratedAssembly" },
            new PowerShellCompilationArtifactFile { Path = "module.psd1", Role = "PrimaryModuleManifest" },
            new PowerShellCompilationArtifactFile { Path = "Generated.Private.ps1", Role = "GeneratedModuleDependency" },
            new PowerShellCompilationArtifactFile { Path = "Microsoft.PowerShell.SDK.dll", Role = "RuntimeDependency" },
            new PowerShellCompilationArtifactFile { Path = "Vendor.Cmdlets.dll", Role = "ModuleDependency" },
            new PowerShellCompilationArtifactFile { Path = "Nested.psm1", Role = "ModuleDependency" }
        };

        var selected = PowerShellCompilationArtifactSigner.GetBuildOwnedSignableFiles(files);

        Assert.Equal(new[] { "tool.exe", "module.psm1", "typed.dll", "tool.dll", "module.psd1", "Generated.Private.ps1" }, selected);
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.True(sibling.Succeeded, sibling.Error + Environment.NewLine + sibling.BuildOutput);
        var artifactHash = Hash(sibling.ArtifactPath!);
        var manifestHash = Hash(sibling.ManifestPath!);

        var exception = Assert.Throws<ArgumentException>(() => builder.Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Sibling.dll",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
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
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
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
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("top-level script unit", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
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
        Assert.True(
            PowerShellCompilationPathSafety.PathEquals(Path.GetFullPath(expected), Path.GetFullPath(actual)),
            $"Expected '{expected}', got '{actual}'.");
    }

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

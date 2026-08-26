using System.Diagnostics;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationCurrentReviewRegressionTests
{
    [Theory]
    [InlineData("param([Alias('x')][string] $One, [Alias('x')][string] $Two); return $One + $Two")]
    [InlineData("param([Alias('Two')][string] $One, [string] $Two); return $One + $Two")]
    [InlineData("param([Alias('One')][string] $One); return $One")]
    [InlineData("[CmdletBinding()] param([Alias('Verbose')][string] $Value); return $Value")]
    [InlineData("[CmdletBinding()] param([string] $Verbose); return $Verbose")]
    public void Build_PackagedExecutableRejectsAmbiguousParameterAliasOwnership(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.PackageAliasOwnership", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("[CmdletBinding()] param([Alias('Verbose')][string] $Value); return $Value")]
    [InlineData("[CmdletBinding()] param([string] $Verbose); return $Verbose")]
    public void Build_StrictExecutableRejectsAuthoredCommonParameterCollisions(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.StrictCommonCollision", PowerShellCompilationMode.Strict);

        Assert.False(result.Succeeded);
        Assert.Contains("ambiguous", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("[CmdletBinding()] param([Parameter(Mandatory)][Parameter(Position=0)][string] $Value); return $Value", "duplicate metadata")]
    [InlineData("[CmdletBinding()] param([Parameter(Position=0)][string] $One, [Parameter(Position=0)][string] $Two); return $One + $Two", "position 0")]
    [InlineData("[CmdletBinding()] param([Parameter(ValueFromRemainingArguments)][string[]] $One, [Parameter(ValueFromRemainingArguments)][string[]] $Two); return $One.Length + $Two.Length", "ValueFromRemainingArguments")]
    public void Build_StrictExecutableRejectsInvalidParameterBindingMetadata(string source, string expectedError)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.StrictBindingMetadata", PowerShellCompilationMode.Strict);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Analyze_RejectsDuplicateEffectiveParameterSetMetadata()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { [CmdletBinding(DefaultParameterSetName='ByName')] " +
            "param([Parameter()][Parameter(ParameterSetName='ByName')][string] $Value) return $Value }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            PowerShellCompilationMode.Strict,
            targetFramework: "net8.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("duplicate metadata", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("ByName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_BinaryModuleReservesRemainingArgumentsMemberOnlyWhenGenerated()
    {
        const string parameterName = "__PowerForgeRemainingArguments";
        using var advancedFixture = ArtifactFixture.Create(
            $"function Get-AdvancedValue {{ [CmdletBinding()] param([string] ${parameterName}) return ${parameterName} }}; Export-ModuleMember -Function Get-AdvancedValue",
            ".psm1");
        var advanced = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            advancedFixture.ScriptPath,
            advancedFixture.OutputPath,
            "PowerForge.AdvancedRemainingArgumentsName",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(advanced.Succeeded, advanced.Error + Environment.NewLine + advanced.BuildOutput);
        var escapedPath = advanced.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Get-AdvancedValue -{parameterName} preserved");
        Assert.Equal((0, "preserved", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));

        using var simpleFixture = ArtifactFixture.Create(
            $"function Get-SimpleValue {{ param([string] ${parameterName}) return ${parameterName} }}; Export-ModuleMember -Function Get-SimpleValue",
            ".psm1");
        var simple = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            simpleFixture.ScriptPath,
            simpleFixture.OutputPath,
            "PowerForge.SimpleRemainingArgumentsName",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.False(simple.Succeeded);
        Assert.Contains("generated or inherited", simple.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(simpleFixture.OutputPath));
    }

    [Fact]
    public void Build_BinaryModuleMatchesValidateNotNullCollectionElementBinding()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-StringElementCount { [CmdletBinding()] param([ValidateNotNull()] [string[]] $Values) return $Values.Length }; " +
            "function Get-ObjectElementCount { [CmdletBinding()] param([ValidateNotNull()] [object[]] $Values) return $Values.Length }; " +
            "Export-ModuleMember -Function Get-StringElementCount, Get-ObjectElementCount",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ValidateNotNullElements",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; " +
            "try { 'string=' + (Get-StringElementCount -Values @('ok', $null)) } catch { 'string-rejected=' + $_.Exception.Message }; " +
            "try { Get-ObjectElementCount -Values @([object] 'ok', $null); 'unexpected-object' } catch { 'object-rejected=' + $_.Exception.Message }");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("string=2", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("object-rejected=", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected-object", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsRuntimeControlledExportContract()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PublicValue { return 1 }; if ($true) { Export-ModuleMember -Function Get-PublicValue }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictConditionalExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.False(result.Succeeded);
        Assert.Contains("runtime-controlled", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Analyze_RoutesMixedExplicitAndTerminalNumericReturnTypesToFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { param([bool] $Early) if ($Early) { return 1 }; 2L }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath));
        var unit = Assert.Single(Assert.Single(plan.Files).Units);

        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("branch-specific runtime types", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_StrictExecutableIncludesAdvancedCommonParametersInAbbreviationResolution()
    {
        using var fixture = ArtifactFixture.Create("[CmdletBinding()] param([string] $Value); return $Value");
        var result = BuildExecutable(fixture, "PowerForge.StrictCommonParameters", PowerShellCompilationMode.Strict);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var ambiguous = Run(result.ArtifactPath!, "-V", "Ada");
        Assert.NotEqual(0, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError, StringComparison.OrdinalIgnoreCase);

        var exactCommon = Run(result.ArtifactPath!, "-Verbose");
        Assert.NotEqual(0, exactCommon.ExitCode);
        Assert.Contains("common parameter", exactCommon.StandardError, StringComparison.OrdinalIgnoreCase);

        var exactAuthored = Run(result.ArtifactPath!, "-Value", "Ada");
        Assert.Equal(0, exactAuthored.ExitCode);
        Assert.Equal("Ada", exactAuthored.StandardOutput.Trim());
    }

    [Fact]
    public void Build_PackagedExecutableIncludesNonSwitchCommonParametersInAbbreviationResolution()
    {
        using var fixture = ArtifactFixture.Create("[CmdletBinding()] param([string] $ErrorCode); return $ErrorCode");
        var result = BuildExecutable(fixture, "PowerForge.PackageCommonParameters", PowerShellCompilationMode.Package);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var ambiguous = Run(result.ArtifactPath!, "-E", "Stop");
        Assert.NotEqual(0, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError, StringComparison.OrdinalIgnoreCase);

        var exact = Run(result.ArtifactPath!, "-ErrorCode", "Stop");
        Assert.Equal(0, exact.ExitCode);
        Assert.Equal("Stop", exact.StandardOutput.Trim());
    }

    [Fact]
    public void Resolve_DefaultOutputAvoidsRecursiveLoaderAndExplicitOverlapFailsBeforePublication()
    {
        using var fixture = ArtifactFixture.Create(
            "$Files = @(Get-ChildItem -Path \"$PSScriptRoot/*.ps1\" -Recurse); foreach ($File in $Files) { . $File.FullName }; Export-ModuleMember -Function Get-One",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Get-One.ps1"), "function Get-One { return 1 }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);

        var defaultOutput = PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved);
        Assert.False(defaultOutput.StartsWith(
            fixture.RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));

        Directory.CreateDirectory(Path.Combine(defaultOutput, "Generated"));
        File.WriteAllText(Path.Combine(defaultOutput, "Generated", "Generated.ps1"), "function Get-Generated { return 2 }");
        var secondResolution = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);
        Assert.DoesNotContain(secondResolution.SourceFiles, path => path.Contains("Generated.ps1", StringComparison.OrdinalIgnoreCase));

        var unsafeOutput = Path.Combine(fixture.RootPath, "artifacts");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                unsafeOutput,
                "PowerForge.RecursiveLoaderOverlap",
                resolved.Kind,
                resolved.Mode)
            {
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                ModuleManifestPath = resolved.ModuleManifestPath
            }));
        Assert.Contains("recursive conventional loader root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(unsafeOutput));
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsMandatoryNullCollectionsAndHonorsAllowNull()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RequiredLength { [CmdletBinding()] param([Parameter(Mandatory)] [int[]] $Values) return $Values.Length } " +
            "function Get-AllowedLength { [CmdletBinding()] param([Parameter(Mandatory)] [AllowNull()] [int[]] $Values) return $Values.Length } " +
            "function Invoke-RequiredLength { return Get-RequiredLength -Values $null } " +
            "function Invoke-AllowedLength { return Get-AllowedLength -Values $null } " +
            "Export-ModuleMember -Function Get-RequiredLength, Get-AllowedLength, Invoke-RequiredLength, Invoke-AllowedLength",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MandatoryCollection",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; try {{ Invoke-RequiredLength; 'unexpected-null' }} catch {{ 'required-null=' + $_.Exception.Message }}; 'allowed-null=' + (Invoke-AllowedLength); try {{ Get-RequiredLength -Values @(); 'unexpected-empty' }} catch {{ 'required-empty=' + $_.Exception.Message }}; try {{ Get-AllowedLength -Values @(); 'unexpected-allowed-empty' }} catch {{ 'allowed-empty=' + $_.Exception.Message }}");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("required-null=Mandatory parameter '-Values' does not allow null values.", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("allowed-null=0", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("required-empty=", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("allowed-empty=", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected-", run.StandardOutput, StringComparison.Ordinal);
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("Values is null", generated, StringComparison.Ordinal);
        Assert.Contains("Values is not null && Values.Length == 0", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesConsumedCommandRegionOutputByRoutingCallerToFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RegionHelper { [CmdletBinding()] param(); Write-Output 'region'; return 7 } " +
            "function Get-RegionConsumer { [CmdletBinding()] param(); $value = Get-RegionHelper; return $value.Count } " +
            "Export-ModuleMember -Function Get-RegionHelper, Get-RegionConsumer",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConsumedRegionOutput",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Contains(result.Manifest!.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("command-region success output", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-RegionConsumer");
        Assert.Equal((0, "2", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridBinaryModulePreservesSimpleFunctionParameterAbbreviations()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-AbbreviationValue { param([string] $Value) return $Value }; Export-ModuleMember -Function Get-AbbreviationValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.SimpleAbbreviation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Contains(result.Manifest.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("becomes ambiguous", StringComparison.OrdinalIgnoreCase));
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-AbbreviationValue -V preserved");
        Assert.Equal((0, "preserved", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PositionalConventionalLoaderDiscoversAndStagesFunctions()
    {
        using var fixture = ArtifactFixture.Create(
            "$Public = @(Get-ChildItem \"$PSScriptRoot/Public/*.ps1\"); " +
            "foreach ($Import in $Public) { . $Import.FullName }; Export-ModuleMember -Function Get-PositionalValue",
            ".psm1");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Public"));
        var source = Path.Combine(fixture.RootPath, "Public", "Get-PositionalValue.ps1");
        File.WriteAllText(source, "function Get-PositionalValue { return 23 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);
        Assert.Contains(source, resolved.CompilationSourceFiles, PowerShellCompilationPathSafety.PathComparer);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            fixture.OutputPath,
            "PowerForge.PositionalConventionalLoader",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-PositionalValue");
        Assert.Equal((0, "23", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData(PowerShellCompilationMode.Package)]
    [InlineData(PowerShellCompilationMode.Strict)]
    public void Build_ExecutableRejectsModuleSourceAtPublicBuilderBoundary(PowerShellCompilationMode mode)
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { return 1 }", ".psm1");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.InvalidModuleExecutable",
            PowerShellCompilationArtifactKind.Executable,
            mode);

        var exception = Assert.Throws<ArgumentException>(() => new PowerShellCompilationArtifactBuilder().Build(spec));

        Assert.Contains(".ps1 entrypoint", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(fixture.OutputPath));
    }

    [Fact]
    public void Build_PackagedExecutablePreservesExternalScriptCommandType()
    {
        using var fixture = ArtifactFixture.Create("$MyInvocation.MyCommand.CommandType");
        var result = BuildExecutable(fixture, "PowerForge.PackageCommandType", PowerShellCompilationMode.Package);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "ExternalScript", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Theory]
    [InlineData("$MyInvocation.MyCommand.ModuleName")]
    [InlineData("$MyInvocation.MyCommand")]
    public void Build_PackagedExecutableRejectsUnmodeledTopLevelMyCommandMetadata(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var result = BuildExecutable(fixture, "PowerForge.PackageUnsupportedMetadata", PowerShellCompilationMode.Package);

        Assert.False(result.Succeeded);
        Assert.Contains("MyCommand", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictBinaryModuleAcceptsQualifiedLiteralExportCommand()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-QualifiedStrictValue { return 29 }; Microsoft.PowerShell.Core\\Export-ModuleMember -Function Get-QualifiedStrictValue",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.QualifiedStrictExport",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-QualifiedStrictValue");
        Assert.Equal((0, "29", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_PackagedExecutableConvertsExplicitBooleanParameterValues()
    {
        using var fixture = ArtifactFixture.Create("param([bool] $Flag); return $Flag");
        var result = BuildExecutable(fixture, "PowerForge.PackageBooleanParameter", PowerShellCompilationMode.Package);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var falseWord = Run(result.ArtifactPath!, "--Flag=false");
        var falseVariable = Run(result.ArtifactPath!, "--Flag=$false");
        var trueWord = Run(result.ArtifactPath!, "--Flag=true");

        Assert.Equal((0, "False"), (falseWord.ExitCode, falseWord.StandardOutput.Trim()));
        Assert.Equal((0, "False"), (falseVariable.ExitCode, falseVariable.StandardOutput.Trim()));
        Assert.Equal((0, "True"), (trueWord.ExitCode, trueWord.StandardOutput.Trim()));
        Assert.True(string.IsNullOrWhiteSpace(falseWord.StandardError), falseWord.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(falseVariable.StandardError), falseVariable.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(trueWord.StandardError), trueWord.StandardError);
    }

    [Fact]
    public void Build_StrictExecutableAcceptsSurplusPositionalsOnlyForSimpleEntryPoint()
    {
        using var simpleFixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var simple = BuildExecutable(simpleFixture, "PowerForge.StrictSimpleSurplus", PowerShellCompilationMode.Strict);
        Assert.True(simple.Succeeded, simple.Error + Environment.NewLine + simple.BuildOutput);

        var simpleRun = Run(simple.ArtifactPath!, "7", "ignored");
        Assert.Equal((0, "7", string.Empty), (simpleRun.ExitCode, simpleRun.StandardOutput.Trim(), simpleRun.StandardError.Trim()));

        using var advancedFixture = ArtifactFixture.Create("[CmdletBinding()] param([int] $Value); return $Value");
        var advanced = BuildExecutable(advancedFixture, "PowerForge.StrictAdvancedSurplus", PowerShellCompilationMode.Strict);
        Assert.True(advanced.Succeeded, advanced.Error + Environment.NewLine + advanced.BuildOutput);

        var advancedRun = Run(advanced.ArtifactPath!, "7", "ignored");
        Assert.NotEqual(0, advancedRun.ExitCode);
        Assert.Contains("Unexpected positional argument", advancedRun.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_HybridModuleMarksRewrittenDependenciesAsGeneratedSignableArtifacts()
    {
        using var fixture = ArtifactFixture.Create(
            ". \"$PSScriptRoot/Private.ps1\"; Export-ModuleMember -Function Get-RewrittenValue",
            ".psm1");
        File.WriteAllText(
            Path.Combine(fixture.RootPath, "Private.ps1"),
            "function Get-RewrittenValue { return 42 }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            fixture.OutputPath,
            "PowerForge.GeneratedHybridDependency",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var dependency = Assert.Single(result.Manifest!.Files, file =>
            file.Path.EndsWith("Private.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("GeneratedModuleDependency", dependency.Role);
        Assert.Contains(dependency.Path, PowerShellCompilationArtifactSigner.GetBuildOwnedSignableFiles(result.Manifest.Files));

        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-RewrittenValue");
        Assert.Equal((0, "42", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void Build_QualifiedConventionalLoaderDiscoversAndStagesFunctions()
    {
        using var fixture = ArtifactFixture.Create(
            "$Public = @(Microsoft.PowerShell.Management\\Get-ChildItem -Path \"$PSScriptRoot/Public/*.ps1\"); " +
            "foreach ($Import in $Public) { . $Import.FullName }; Export-ModuleMember -Function Get-QualifiedValue",
            ".psm1");
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Public"));
        var source = Path.Combine(fixture.RootPath, "Public", "Get-QualifiedValue.ps1");
        File.WriteAllText(source, "function Get-QualifiedValue { return 17 }");

        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);
        Assert.Contains(source, resolved.CompilationSourceFiles, PowerShellCompilationPathSafety.PathComparer);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            fixture.OutputPath,
            "PowerForge.QualifiedConventionalLoader",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            ModuleManifestPath = resolved.ModuleManifestPath
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module -Name '{escapedPath}' -Force; Get-QualifiedValue");
        Assert.Equal((0, "17", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    private static PowerShellCompilationBuildResult BuildExecutable(
        ArtifactFixture fixture,
        string name,
        PowerShellCompilationMode mode)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            name,
            PowerShellCompilationArtifactKind.Executable,
            mode)
        {
            SingleFile = false,
            EmitSource = true
        });

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName}' did not exit within 120 seconds.");
        }
        return (process.ExitCode, stdout, stderr);
    }

    private sealed class ArtifactFixture : IDisposable
    {
        private ArtifactFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        internal string RootPath { get; }

        internal string ScriptPath { get; }

        internal string OutputPath { get; }

        internal static ArtifactFixture Create(string source, string extension = ".ps1")
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeCompilationReview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var script = Path.Combine(root, "Source" + extension);
            File.WriteAllText(script, source);
            return new ArtifactFixture(root, script, Path.Combine(root, "output"));
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
            var siblingArtifacts = Path.Combine(
                Directory.GetParent(RootPath)?.FullName ?? RootPath,
                "artifacts",
                new DirectoryInfo(RootPath).Name);
            try { Directory.Delete(siblingArtifacts, recursive: true); } catch { }
        }
    }
}

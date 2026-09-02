using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_StrictExecutableCompilesTypedThrowAndRethrow()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Message); " +
            "function Invoke-Failure { param([string] $Text) try { throw [System.InvalidOperationException]::new($Text) } " +
            "catch [System.InvalidOperationException] { throw } }; " +
            "try { Invoke-Failure -Text $Message; return 0 } catch [System.InvalidOperationException] { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedThrow",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!, "--Message=boom");
        Assert.Equal((0, "42", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains("throw new global::System.InvalidOperationException", generated, StringComparison.Ordinal);
        Assert.Contains("throw;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictExecutableCompilesArrayExpressionsAndNegativeIndexMutation()
    {
        using var fixture = ArtifactFixture.Create(
            "param([int] $Value); [int[]] $values = @(1, 2, 3); [int[]] $empty = @(); " +
            "$values[-1] = $Value; [int] $sum = 0; foreach ($item in $values) { $sum += $item }; return $sum");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedArrayMutation",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!, "--Value=9");
        Assert.Equal((0, "12", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains("new int[] { 1, 2, 3 }", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Array.Empty<int>()", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictExecutableKeepsScalarStringForEachBodyInsideLoop()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Value); [int] $Count = 0; foreach ($Item in $Value) { $Count += 1; break }; return $Count");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedScalarStringForEach",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var process = RunProcess(result.ArtifactPath!, "--Value=PowerForge");
        Assert.Equal((0, "1", string.Empty), (process.ExitCode, process.StandardOutput.Trim(), process.StandardError.Trim()));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShellScript.cs"));
        Assert.Contains("foreach (string", generated, StringComparison.Ordinal);
        Assert.Contains("break;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleCompilesWritableClrMemberMutation()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-FrontierLength { param([string] $Name) " +
            "$builder = [System.Text.StringBuilder]::new($Name); $builder.Length = 1; return $builder.ToString() }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedMemberMutation",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal("A", RunModuleProof(result.ArtifactPath!, "Set-FrontierLength -Name Ada"));
    }

    [Fact]
    public void Build_StrictBinaryModulePreservesBoundParameterPresenceAcrossCmdletsAndLocalCalls()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-FrontierBound { param([string] $Name) if ($PSBoundParameters.ContainsKey('Name')) { return 'bound' }; return 'missing' }; " +
            "function Invoke-FrontierOmitted { return Test-FrontierBound }; " +
            "function Invoke-FrontierPresent { param([string] $Name) return Test-FrontierBound -Name $Name }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedBoundParameters",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var output = RunModuleProof(
            result.ArtifactPath!,
            "Test-FrontierBound; Test-FrontierBound -Name ''; Invoke-FrontierOmitted; Invoke-FrontierPresent");
        Assert.Equal(new[] { "missing", "bound", "missing", "bound" }, output.Split(Environment.NewLine));
    }

    [Fact]
    public void Build_StrictExecutablePreservesBoundParameterPresenceWithoutPowerShellRuntime()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Name); if ($PSBoundParameters.ContainsKey('Name')) { return 'bound' }; return 'missing'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedExecutableBoundParameters",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var omitted = RunProcess(result.ArtifactPath!);
        var present = RunProcess(result.ArtifactPath!, "--Name=");
        Assert.Equal((0, "missing", string.Empty), (omitted.ExitCode, omitted.StandardOutput.Trim(), omitted.StandardError.Trim()));
        Assert.Equal((0, "bound", string.Empty), (present.ExitCode, present.StandardOutput.Trim(), present.StandardError.Trim()));
    }

    [Fact]
    public void Build_StrictBinaryModuleConstructsGenuinePowerShellObject()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierObject { param([string] $Name) " +
            "return [pscustomobject]@{ Name = $Name; Length = $Name.Length } }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedPowerShellObject",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(
            "Ada|3|System.Management.Automation.PSCustomObject",
            RunModuleProof(result.ArtifactPath!, "$value = Get-FrontierObject -Name Ada; \"$($value.Name)|$($value.Length)|$($value.PSObject.TypeNames[0])\""));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("new global::System.Management.Automation.PSObject", generated, StringComparison.Ordinal);
        Assert.Contains("PSNoteProperty", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleContinuesTypedCodeAfterDiscardedCommandRegion()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-FrontierRegion { [CmdletBinding()] param([int] $Value) [int] $result = $Value; " +
            "$null = Write-Output 'hidden'; Write-Output 'visible'; $result += 1; return $result }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedNonTerminalRegion",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(new[] { "visible", "2" }, RunModuleProof(result.ArtifactPath!, "Invoke-FrontierRegion -Value 1").Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("$null = Write-Output 'hidden'", generated, StringComparison.Ordinal);
        Assert.Contains("result = checked((int)(result + 1))", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModulePropagatesCommandRegionHostAcrossTypedLocalCall()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-FrontierRegionHelper { [CmdletBinding()] param([int] $Value) Microsoft.PowerShell.Utility\\Write-Verbose 'detail'; Get-RegionText; " +
            "[int] $result = $Value; $result += 1; return $result }; " +
            "function Get-FrontierRegionOuter { [CmdletBinding()] param([int] $Value) return Get-FrontierRegionHelper -Value $Value }",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedRegionGraph",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.Equal(new[] { "region", "2" }, RunModuleProof(result.ArtifactPath!, "function global:Get-RegionText { 'region' }; Get-FrontierRegionOuter -Value 1").Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains(
            "Get_FrontierRegionHelper(Value, __writeOutput, __writeVerbose, __writeDebug, __writeWarning, __writeInformation, __writeHost, __writeError, __invokePowerShellRegion, __invokePowerShellCapture)",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Transpile_BoundParametersRejectsUndeclaredCanonicalName()
    {
        using var fixture = ArtifactFixture.Create(
            "function Test-FrontierBound { param([string] $Name) return $PSBoundParameters.ContainsKey('Missing') }",
            ".psm1");

        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.TypedBoundBoundary",
            "CompiledPowerShell",
            "net10.0");

        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, diagnostic =>
            diagnostic.Message.Contains("literal canonical name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_StrictExecutableRejectsPowerShellObjectConstruction()
    {
        using var fixture = ArtifactFixture.Create("return [pscustomobject]@{ Name = 'Ada' }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RuntimeFreeObjectBoundary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("requires PowerShell runtime", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsStringThrowWithoutClrException()
    {
        using var fixture = ArtifactFixture.Create("throw 'boom'");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.TypedThrowBoundary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("exception expression", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictExecutableRejectsNonPublicClrMemberSetter()
    {
        using var fixture = ArtifactFixture.Create(
            "$encoding = [System.Text.UTF8Encoding]::new(); $encoding.IsReadOnly = $true; return 0");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.PrivateSetterBoundary",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));

        Assert.False(result.Succeeded);
        Assert.Contains("readable and writable", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }
}

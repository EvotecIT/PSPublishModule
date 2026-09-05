using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string NullSeededReferenceLocalSource =
        "function Get-NullSeededLength { [CmdletBinding()] param([bool] $Create) " +
        "$Stream = $null; if ($Create) { $Stream = [System.IO.MemoryStream]::new() }; " +
        "if ($null -eq $Stream) { return [long] 0 }; try { return [long] 1 } finally { $Stream.Dispose() } }";

    private const string NullSeededOptionalFlowSource =
        "function Get-NullSeededCanRead { [CmdletBinding()] param([bool] $Create) " +
        "$Stream = $null; if ($Create) { $Stream = [System.IO.MemoryStream]::new() }; return $Stream.CanRead } " +
        "function Get-LoopSeededCanRead { [CmdletBinding()] param([int[]] $Items) " +
        "$Stream = $null; foreach ($Item in $Items) { $Stream = [System.IO.MemoryStream]::new() }; return $Stream.CanRead }";

    [Fact]
    public void Analyze_InfersOneExactReferenceTypeAfterNullSeed()
    {
        using var fixture = ArtifactFixture.Create(NullSeededReferenceLocalSource, ".psm1");

        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.ParseFile(fixture.ScriptPath) },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(semantic.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var local = Assert.Single(Assert.Single(semantic.Bound.Functions).Locals, static item => item.Symbol.Name.Equals("Stream", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(typeof(MemoryStream), local.Type.ClrType);
        Assert.Equal(PowerShellTypeFactProvenance.Inferred, local.Type.Provenance);
        Assert.Contains("null-seeded", local.Type.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("$Value = $null; $Value = [System.DateTime]::new(2020, 1, 1); return $Value.Year")]
    [InlineData("param([bool] $First); $Value = $null; if ($First) { $Value = [System.IO.MemoryStream]::new() } else { $Value = [System.Text.StringBuilder]::new() }; return 1")]
    public void Analyze_RejectsNullSeedWithoutOneExactReferenceType(string body)
    {
        using var fixture = ArtifactFixture.Create("function Test-NullSeed { " + body + " }", ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("inferred local", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_RejectsNullLengthBeforeFutureConstruction()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NullLength { $Stream = $null; return $Stream.Length; $Stream = [System.IO.MemoryStream]::new() }",
            ".psm1");

        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath,
            targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.BinaryModule));

        var unit = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(unit.IsCompilable);
        Assert.Contains(unit.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("Int32 zero", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_RuntimeFreeLifecyclePreservesPotentialNullAfterZeroProcessIterations()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-ProcessSeed { [CmdletBinding()] [OutputType([bool])] " +
            "param([Parameter(ValueFromPipeline)][int] $Value) begin { $Stream = $null } " +
            "process { $Stream = [System.IO.MemoryStream]::new() } end { $Stream.CanRead } } " +
            "function Invoke-ProcessSeed { param([int[]] $Values) $Values | Test-ProcessSeed }",
            Path.Combine(Path.GetTempPath(), "null-seeded-lifecycle.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.TypedExecutable);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Test_ProcessSeed").Source;
        Assert.Contains("(Stream)?.CanRead", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_TypedCatchForgetsValueStateMutatedBeforeThrow()
    {
        var document = PowerShellSourceParser.Parse(
            "function Test-CatchState { $Stream = [System.IO.MemoryStream]::new(); " +
            "try { $Stream = $null; throw [System.InvalidOperationException]::new() } " +
            "catch [System.InvalidOperationException] { return $Stream.CanRead } }",
            Path.Combine(Path.GetTempPath(), "null-seeded-catch-state.ps1"));

        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { document },
            "net10.0",
            PowerShellCompilationCapabilities.BinaryModule);

        Assert.Empty(result.Emitted.Diagnostics.Select(static diagnostic => diagnostic.Code + ": " + diagnostic.Message));
        var source = Assert.Single(result.Emitted.Methods, static method => method.GeneratedName == "Test_CatchState").Source;
        Assert.Contains("(Stream)?.CanRead", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesNullSeededReferenceLocalBehavior(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(NullSeededReferenceLocalSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullSeededReferenceLocal",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            EmitSource = true,
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string proof = "Get-NullSeededLength -Create $false; Get-NullSeededLength -Create $true";
        var original = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(original, compiled);
        Assert.Equal(new[] { "0", "1" }, compiled.Split(Environment.NewLine));
        var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
        Assert.Contains("System.IO.MemoryStream", generated, StringComparison.Ordinal);
        Assert.Contains(" = null;", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_BinaryModulePreservesOptionalBranchAndLoopNullState(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(NullSeededOptionalFlowSource, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.NullSeededOptionalFlow",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string proof =
            "@((Get-NullSeededCanRead -Create $false)).Count; " +
            "Get-NullSeededCanRead -Create $true; " +
            "@((Get-LoopSeededCanRead -Items @())).Count; " +
            "Get-LoopSeededCanRead -Items @(1)";
        var original = RunModuleProof(fixture.ScriptPath, proof, host);
        var compiled = RunModuleProof(result.ArtifactPath!, proof, host);

        Assert.Equal(original, compiled);
        Assert.Equal(new[] { "1", "True", "1", "True" }, compiled.Split(Environment.NewLine));
    }
}

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private const string NativeLateDeclarationSource = """
        param([string] $Mode = 'owned')
        $ErrorActionPreference = 'Stop'
        $global:LateRootCalls = 0
        $before = Get-Command -Name Read-LateRoot -ErrorAction Ignore
        if ($before) { throw 'The root function was registered before its declaration.' }
        Write-Output 'before:missing'
        function Read-PureRoot { param([int] $Value) return $Value + 1 }
        function Read-LateRoot([int] $Value = 3) {
            $global:LateRootCalls++
            return "$Mode|$Value|$global:LateRootCalls"
        }
        function Read-AdvancedRoot {
            [CmdletBinding()]
            param([Alias('n')][ValidateRange(1,9)][int] $Value = 4)
            return "$Mode|$Value"
        }
        Write-Output (Read-PureRoot -Value 7)
        Write-Output (Read-LateRoot)
        Write-Output (Read-AdvancedRoot -n 5)
        $helperPath = Join-Path $PSScriptRoot 'Helper.ps1'
        . $helperPath
        Read-LateHelper
        """;

    [Fact]
    public void Explain_NativeRootDeclarationsUseAuthoredAvailabilityWithPureAdvancedAndDynamicStorage()
    {
        using var fixture = ArtifactFixture.Create(NativeLateDeclarationSource, compactPath: true);
        File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.ps1"), "function Read-LateHelper { return 'helper' }");
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var shaped = PowerShellCompilationExplainShaper.Shape(input, plan, "net10.0", out var entry)!;
        Assert.NotNull(entry);
        Assert.NotNull(entry.EntryPointMethod.NativeFunctionBinding);
        Assert.Equal(4, shaped.Methods.Length);
        Assert.All(shaped.Methods, static method => Assert.NotNull(method.NativeFunctionBinding));
        Assert.Empty(PowerShellBinaryCmdletSourceGenerator.GetCommandIdentities(shaped, exportedFunctions: null));
        var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, "net10.0");
        Assert.All(explanation.Files.SelectMany(static file => file.Units), static unit =>
        {
            Assert.True(unit.Emitted, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        });
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeRootLateDeclarationsPreserveOriginalTimingScopeAndExactSource()
    {
        using var fixture = ArtifactFixture.Create(NativeLateDeclarationSource, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "function Read-LateHelper { return 'helper' }");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.LateRootDeclarations", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath, helper },
            EmitSource = true,
            TargetContract = PowerShellCompilationTargetContractService.Create(
                PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
                "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true)
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var built = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(File.ReadAllBytes(fixture.ScriptPath), File.ReadAllBytes(Path.Combine(built.GeneratedSourcePath!, "Source.ps1")));
        Assert.All(built.Manifest!.UnitDispositionLedger!.Entries, static unit =>
        {
            Assert.True(unit.EmittedClrMethod, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
            Assert.False(unit.EmittedBinaryCmdlet, unit.Name);
        });
        foreach (var mode in new[] { "owned", "second" })
        {
            var original = RunProcess("pwsh", "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Mode", mode);
            var generated = RunProcess(built.ArtifactPath!, "--Mode=" + mode);
            Assert.Equal((0, original.StandardOutput, string.Empty),
                (generated.ExitCode, generated.StandardOutput, generated.StandardError));
            Assert.Equal(new[] { "before:missing", "8", mode + "|3|1", mode + "|5", "helper" },
                generated.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static line => line.TrimEnd('\r')).ToArray());
        }
    }

    [Theory]
    [InlineData("if ($true) { function Read-ConditionalRoot { 7 } }; Read-ConditionalRoot")]
    [InlineData("function Read-BlockedRoot { exit 1 }; Read-BlockedRoot")]
    [InlineData("function Read-ProviderRoot { 1 }; ${function:Read-ProviderRoot} = { 2 }; Read-ProviderRoot")]
    public void Explain_NativeRootTimingDoesNotWidenConditionalExitOrFunctionProviderOwners(string source)
    {
        using var fixture = ArtifactFixture.Create(source);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid,
            "net10.0", PowerShellCompilationResourceMode.Declared, null, null, fixture.OutputPath, target);
        var root = Assert.Single(PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, "net10.0")
            .Files.SelectMany(static file => file.Units), static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        Assert.False(root.Emitted);
        Assert.True(root.RetainedHostedSource);
    }
}

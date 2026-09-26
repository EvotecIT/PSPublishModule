using PowerForge;
using System.Management.Automation.Language;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridExecutablePreservesOmittedStringParameter()
    {
        using var fixture = ArtifactFixture.Create("param([string] $Value)\nConvertTo-Json -InputObject $Value");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.HybridOmittedStringEntry",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).Emitted);
        var original = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath);
        var generated = RunProcess(result.ArtifactPath!);
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (generated.ExitCode, generated.StandardOutput.Trim(), generated.StandardError.Trim()));
    }

    [Fact]
    public void Build_HybridExecutableRunsCompiledPreboundScriptRoot()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Pattern = 'yyyy', [string] $When = '2024-04-05')\nGet-Date -Date $When -Format $Pattern");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.HybridCompiledEntry",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true);
        var input = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath,
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, PowerShellCompilationMode.Hybrid, "net10.0");
        var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, "net10.0");
        var explainedRoot = Assert.Single(Assert.Single(explanation.Files).Units);
        Assert.True(explainedRoot.Emitted);
        Assert.False(explainedRoot.RetainedHostedSource);
        var census = new PowerShellCompilationCensusRunner().RunWithOptions(
            new[] { fixture.ScriptPath }, new PowerShellCompilationCensusOptions
            {
                TargetFramework = "net10.0",
                Mode = PowerShellCompilationMode.Hybrid,
                ArtifactKind = PowerShellCompilationArtifactKind.Executable
            });
        Assert.True(census.Passed, string.Join(System.Environment.NewLine,
            census.InputFailures.Select(static failure => failure.Message)));
        Assert.Equal(1, Assert.Single(census.Products).CompilableUnits);

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + System.Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        var builtRoot = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries);
        Assert.True(builtRoot.Emitted);
        Assert.False(builtRoot.RetainedHostedSource);
        Assert.True(builtRoot.RuntimeRouted);
        var omitted = RunProcess(result.ArtifactPath!);
        var explicitArgument = RunProcess(result.ArtifactPath!, "-Pattern", "MM");
        var originalOmitted = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath);
        var originalExplicit = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-Pattern", "MM");
        var invalid = RunProcess(result.ArtifactPath!, "-When", "not-a-date");
        var originalInvalid = RunProcess("pwsh", "-NoProfile", "-File", fixture.ScriptPath, "-When", "not-a-date");
        Assert.Equal((0, "2024", string.Empty),
            (omitted.ExitCode, omitted.StandardOutput.Trim(), omitted.StandardError.Trim()));
        Assert.Equal((0, "04", string.Empty),
            (explicitArgument.ExitCode, explicitArgument.StandardOutput.Trim(), explicitArgument.StandardError.Trim()));
        Assert.Equal((originalOmitted.ExitCode, originalOmitted.StandardOutput.Trim(), originalOmitted.StandardError.Trim()),
            (omitted.ExitCode, omitted.StandardOutput.Trim(), omitted.StandardError.Trim()));
        Assert.Equal((originalExplicit.ExitCode, originalExplicit.StandardOutput.Trim(), originalExplicit.StandardError.Trim()),
            (explicitArgument.ExitCode, explicitArgument.StandardOutput.Trim(), explicitArgument.StandardError.Trim()));
        Assert.Equal(originalInvalid.ExitCode, invalid.ExitCode);
        Assert.Equal(originalInvalid.StandardOutput.Trim(), invalid.StandardOutput.Trim());
        Assert.Contains("not-a-date", originalInvalid.StandardError, System.StringComparison.Ordinal);
        Assert.Contains("not-a-date", invalid.StandardError, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_HybridPreboundEntryPreservesAuthoredRegionSource()
    {
        using var fixture = ArtifactFixture.Create("param([string] $Pattern = 'yyyy')\nGet-Date -Format $Pattern");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable));
        Assert.True(plan.CanProceed);
        var compiled = PowerShellTypedExecutableCompiler.CompileHybridPreboundEntry(fixture.ScriptPath,
            new[] { fixture.ScriptPath }, plan, "net10.0",
            PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId);
        Assert.True(compiled.EntryPointMethod.RequiresPowerShellCommandRegions);
        Assert.NotEmpty(compiled.EntryPointMethod.SourceMap);
        Assert.DoesNotContain("Pattern = \"yyyy\"", compiled.EntryPointMethod.Source);
        Assert.DoesNotContain(".powerforge-entry.ps1", compiled.EntryPointMethod.Source);
        Assert.Contains("PowerShellHostedRegionSource", compiled.EntryPointMethod.Source);
        Assert.Contains(PowerShellCSharpLiteral.QuoteString(System.IO.File.ReadAllText(fixture.ScriptPath)),
            compiled.EntryPointMethod.Source);
        var commandOffset = System.IO.File.ReadAllText(fixture.ScriptPath).IndexOf("Get-Date", System.StringComparison.Ordinal);
        Assert.Contains($"new int[] {{ {commandOffset}, {commandOffset + "Get-Date -Format $Pattern".Length} }}",
            compiled.EntryPointMethod.Source);
    }

    [Fact]
    public void Compile_HybridPreboundEntryRejectsIneligibleRoot()
    {
        using var fixture = ArtifactFixture.Create("switch -Wildcard ('a') { 'a' { 1 } }");
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, PowerShellCompilationMode.Hybrid, targetFramework: "net10.0",
            capabilities: PowerShellCompilationCapabilities.HybridExecutable));
        var root = Assert.Single(Assert.Single(plan.Files).Units);
        Assert.False(root.IsCompilable);

        var error = Assert.Throws<System.InvalidOperationException>(() =>
            PowerShellTypedExecutableCompiler.CompileHybridPreboundEntry(fixture.ScriptPath,
                new[] { fixture.ScriptPath }, plan, "net10.0",
                PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId));
        Assert.Contains("eligible authored script root", error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Project_HybridEntryRejectsChangedHostedStatement()
    {
        using var fixture = ArtifactFixture.Create("Get-Item");
        var authored = PowerShellSourceParser.ParseFile(fixture.ScriptPath);
        var synthetic = PowerShellSourceParser.Parse("function Invoke { Get-Date }",
            fixture.ScriptPath + ".powerforge-entry.ps1");
        var statement = Assert.Single(Assert.Single(synthetic.SyntaxRoot.EndBlock!.Statements
            .OfType<FunctionDefinitionAst>()).Body.EndBlock!.Statements);
        var projection = new PowerShellAuthoredSourceProjection(authored,
            new[] { new PowerShellRegionSourceRemap(statement.Extent.StartOffset,
                statement.Extent.EndOffset, 0, authored.Text.Length) });

        Assert.Throws<System.IO.InvalidDataException>(() =>
            projection.Select(synthetic, new[] { statement.Extent }));
    }
}

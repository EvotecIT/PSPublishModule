using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorArtifact_DefaultBinaryHostPreservesNestedContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-InnerValue {
                [CmdletBinding()] param([string]$Text)
                [int]$value = 99
                $value = [int]::Parse($Text)
                $value
            }
            function Read-OuterValues {
                [CmdletBinding()] param([string[]]$Values)
                'before'
                foreach ($text in $Values) { Read-InnerValue -Text $text }
                'after'
            }
            """, ".psm1");
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "Generated.StatementErrors", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework
        };
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.True(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.UsesPowerShellRuntimeFallback);
        Assert.Equal(2, result.Manifest.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        Assert.NotNull(result.Manifest.PublicAbi);
        Assert.All(result.Manifest.PublicAbi.Methods, method =>
        {
            Assert.Equal("PowerShellStatementErrors", method.ExceptionContract);
            var context = Assert.Single(method.Parameters, parameter => parameter.CompilerPurpose == "PowerShellStatementErrors");
            Assert.Equal("PowerForge.Generated.Runtime.PowerShellStatementErrorContext", context.TypeName);
            Assert.True(context.Required);
        });
        const string probe = """
            $records = @(Read-OuterValues -Values '1','bad','2' -ErrorAction Continue 2>&1)
            ($records | ForEach-Object {
                if ($_ -is [Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                else { $_.GetType().Name + ':' + $_ }
            }) -join '|'
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-artifact-errors");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-artifact-errors");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("Int32:99|Int32:2|String:after", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Fact]
    public void StatementErrorArtifact_LoweringRejectsAHostWithoutTheErrorCapability()
    {
        var document = PowerShellSourceParser.Parse("function Read-Value { [CmdletBinding()] param([string]$Text) return [int]::Parse($Text) }",
            Path.Combine(Path.GetTempPath(), "statement-error-capability.ps1"));
        var result = new PowerShellSemanticCompilationPipeline().Compile(new[] { document }, "net8.0",
            PowerShellCompilationCapabilities.BinaryModule);
        Assert.Empty(result.Emitted.Diagnostics);
        Assert.True(Assert.Single(result.Lowered.Functions).RequiresPowerShellStatementErrors);
        var rejected = new PowerShellTypedLowerer().Lower(result.Analyzed,
            PowerShellCompilationCapabilities.BinaryModule & ~PowerShellCompilationCapability.PowerShellStatementErrors);
        Assert.Empty(rejected.Functions);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "PSL1012");
        Assert.False(PowerShellCompilationCapabilities.TypedExecutable.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors));
        Assert.False(PowerShellCompilationCapabilities.StaticRuntimeFacts.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors));
    }

    [Theory]
    [InlineData("return $Error.Count", "")]
    [InlineData("return $ErrorActionPreference", "")]
    [InlineData("Read-Snapshot", "function Read-Snapshot { [CmdletBinding()] param() return $Error.Count }")]
    public void StatementErrorArtifact_RetainsErrorStateReadsThatWouldUseAnEntrySnapshot(string read, string helper)
    {
        using var fixture = ArtifactFixture.Create("function Read-ChangedState { [CmdletBinding()] param([string]$Text) " +
            "[void][int]::Parse($Text); " + read + " } " + helper, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "PowerForge.Compiled", "StatementErrorMethods", "net10.0");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Read-ChangedState");
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("invocation-entry runtime-state snapshot", StringComparison.Ordinal));
    }
}

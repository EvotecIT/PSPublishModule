using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_HybridWholeFunctionOwnsItsBoundPrefix()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-WholePrefix {
                [CmdletBinding()]
                param()
                $First = 'hello'
                $Second = 'world'
                Write-Output "$First $($Second.ToUpperInvariant())"
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "WholePrefixMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Single(typed.Methods);
        Assert.Empty(typed.PromotedRegions);
        Assert.Contains(typed.RegionCandidates, candidate => candidate.DecisionCode == "region.whole-function-selected");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.WholePrefix",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var run = RunProcess("pwsh", "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; Get-WholePrefix");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("hello WORLD", run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Build_HybridPrefixPreservesNullableConstraintAndNullTransfer(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-NullablePrefix {
                [CmdletBinding()]
                param([Nullable[int]] $Number)
                [Nullable[int]] $Value = $Number
                $Count = 1
                data NullablePrefixBarrier { }
                & { if ($null -eq $Value) { 'null' } else { $Value } }
                $Value = '9'
                $Value.GetType().Name
                $Count
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.NullablePrefix",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.PromotedTypedRegions > 0);
        const string probe = "@(Get-NullablePrefix $null; Get-NullablePrefix 0; Get-NullablePrefix 7) -join '|'";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("null|Int32|1|0|Int32|1|7|Int32|1", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Build_HybridPrefixTransfersEveryScalarLocalInOrder(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-PrefixReport {
                [CmdletBinding()]
                param([string] $Title, [bool] $Selected)
                [int] $Count = 1
                $Label = "$Title report"
                [bool] $Flag = $false
                if ($Selected) { $Count = 7; $Flag = $true }
                [string] $Tail = 'end'
                data MultiLocalPrefixBarrier { }
                & { "$Count|$Label|$Flag|$Tail" }
                $Count = '9'
                $Label = 3
                "$($Count.GetType().Name)|$($Label.GetType().Name)|$($Flag.GetType().Name)"
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "MultiLocalMethods", framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.True(typed.PromotedRegions.Any(candidate => candidate.ContinuationLocals.Count > 0),
            "Methods: " + string.Join(", ", typed.Methods.Select(method => method.SourceName)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(candidate => candidate.DecisionCode + ": " + candidate.Reason)));
        var region = Assert.Single(typed.PromotedRegions, candidate => candidate.ContinuationLocals.Count > 0);
        Assert.Equal(new[] { "Count", "Label", "Flag", "Tail" }, region.ContinuationLocals.Select(local => local.Name));
        Assert.Equal(new[] { true, false, true, true }, region.ContinuationLocals.Select(local => local.HasTypeConstraint));
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
            System.Text.Json.JsonSerializer.Serialize(region));
        Assert.Equal(4, roundTrip!.ContinuationLocals.Count);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.PrefixMultiLocal",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.PromotedTypedRegions > 0,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        const string probe = "@(Get-PrefixReport -Title '' -Selected $false; Get-PrefixReport -Title 'Sample' -Selected $true) -join '~'";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("1| report|False|end~Int32|Int32|Boolean~7|Sample report|True|end~Int32|Int32|Boolean", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Number += 1")]
    [InlineData("$Number++")]
    [InlineData("$Number %= 0")]
    public void Transpile_HybridTerminalGraphIncludesNumericMutationFailures(string mutation)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-NumericRegion { [CmdletBinding()] param([int]$Number); " +
            "data HostedData { 'before' }; " + mutation + "; return $Number }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericRegionMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(typed.PromotedRegions);
        var candidate = Assert.Single(typed.RegionCandidates);
        Assert.Equal("region.statement-errors", candidate.DecisionCode);
        Assert.Contains("PowerShellStatementError", Assert.Single(candidate.RegionGraph!.Regions).Errors);
        Assert.Contains("ClrException", Assert.Single(candidate.RegionGraph!.Regions).Errors);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh", "[int]", "System.Int32")]
    [InlineData("net10.0", "pwsh", "", "System.String")]
    [InlineData("net472", "powershell.exe", "[int]", "System.Int32")]
    public void Build_HybridPrefixResumesWithSameOutputAndVariableConstraint(
        string framework, string host, string constraint, string finalType)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-PrefixValue {
                [CmdletBinding()]
                param([bool] $Enabled)
                CONSTRAINT $result = 0
                if ($Enabled) { $result = 7 }
                data PrefixContinuationBarrier { }
                & { "hosted:$result" }
                $result = '9'
                "after:$($result.GetType().FullName):$result"
            }
            Export-ModuleMember -Function Get-PrefixValue
            """.Replace("CONSTRAINT", constraint, StringComparison.Ordinal), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.PrefixContinuation",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.PromotedTypedRegions > 0,
            string.Join(Environment.NewLine, result.Manifest.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        const string probe = "@(Get-PrefixValue $false; Get-PrefixValue $true) -join '|'";
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Equal("hosted:0|after:" + finalType + ":9|hosted:7|after:" + finalType + ":9", original.StandardOutput.Trim());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("[int]$result = 0; $result += $Number; & { $result }", 1)]
    [InlineData("[int]$result = 0; $Number = 7; & { $result; $Number }", 1)]
    [InlineData("[int]$result = 0; Write-Warning 'before'; $result = 7; & { $result }", 1)]
    [InlineData("& { 'before' }; [int]$result = 0; $result = 7; & { $result }", 2)]
    public void Transpile_HybridPrefixSelectsOnlyClosedInitializationRunAroundHostedEffects(
        string body,
        int expectedStatementCount)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-PrefixValue { [CmdletBinding()] param([int]$Number); " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "PrefixMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var prefixes = typed.PromotedRegions.Where(region => region.ContinuationLocals.Count != 0).ToArray();
        var prefix = Assert.Single(prefixes);
        var selected = File.ReadAllText(fixture.ScriptPath)
            .Substring(prefix.StartOffset, prefix.EndOffset - prefix.StartOffset);
        Assert.Contains("[int]$result = 0", selected, StringComparison.Ordinal);
        Assert.Equal(expectedStatementCount == 2, selected.Contains("$result = 7", StringComparison.Ordinal));
        Assert.DoesNotContain("& {", selected, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_LaterFreshRegionDoesNotCompeteWithWholeMethodOwnership()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-WholeValue { param([int]$Number); $Number = 7; [int]$result = 0; return $result }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "WholeMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Single(typed.Methods);
        Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_LaterFreshRegionDoesNotDetachFromAssignedNativeScriptBlock()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-WorkerValue { $worker = { & { 'hosted' }; [int]$result = 0; $result = 7 }; & $worker }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "WorkerMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.Empty(typed.PromotedRegions);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridPrefixTransfersScalarAndRetainsContinuation()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-PrefixValue {
                [CmdletBinding()]
                param([bool] $Enabled)
                [int] $result = 0
                if ($Enabled) { $result = 7 }
                data ScalarPrefixBarrier { }
                & { "hosted:$result" }
                return $result
            }
            Export-ModuleMember -Function Get-PrefixValue
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "PrefixMethods", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var prefix = Assert.Single(typed.PromotedRegions, region => region.ContinuationLocals.Count != 0);
        var local = Assert.Single(prefix.ContinuationLocals);
        Assert.Equal("result", local.Name, ignoreCase: true);
        Assert.Equal("System.Int32", local.TypeName);
        Assert.True(local.HasTypeConstraint);
        Assert.Equal(new[] { "Enabled" }, prefix.InputParameters.Select(parameter => parameter.Name));
        Assert.Empty(Assert.Single(prefix.RegionGraph.Regions).Errors);
        Assert.DoesNotContain("hosted:", typed.SourceCode, StringComparison.Ordinal);
    }
}

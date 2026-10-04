using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Build_HybridPrefixPreservesEachInheritedAssignmentFailure(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-AssignmentOwnership {
                [CmdletBinding()]
                param([bool] $Enabled, [bool] $FailAfter)
                $ProtectedRegionValue = 1
                if ($Enabled) { $ProtectedRegionValue = 2 }
                data OwnershipBarrier { 'retained' }
                if ($FailAfter) { throw 'retained failure' }
                & { "$ProtectedRegionValue" }
            }
            function Get-RegionMetadata {
                <#
                .SYNOPSIS
                Region ownership metadata.
                #>
                [CmdletBinding(DefaultParameterSetName='First', SupportsShouldProcess=$true)]
                [OutputType([string])]
                param(
                    [Parameter(ParameterSetName='First')][Alias('Go')][bool] $Enabled,
                    [Parameter(ParameterSetName='Second')][switch] $Second
                )
                $HeaderValue = 1
                if ($Enabled) { $HeaderValue = 2 }
                data HeaderBarrier { 'retained' }
                & { "$HeaderValue" }
            }
            function Get-CombinedRegions {
                [CmdletBinding()]
                param([bool] $Enabled, [int] $Number)
                $ProtectedRegionValue = 1
                if ($Enabled) { $ProtectedRegionValue = 2 }
                data CombinedBarrier { 'retained' }
                & { "$ProtectedRegionValue" }
                return $Number
            }
            Export-ModuleMember -Function Get-AssignmentOwnership, Get-RegionMetadata, Get-CombinedRegions
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "AssignmentOwnership", framework,
            PowerShellCompilationCapabilities.HybridModule);
        var region = Assert.Single(typed.PromotedRegions, candidate => candidate.SourceName == "Get-AssignmentOwnership");
        Assert.True(region.RequiresLocalOwnershipGuard);
        Assert.True(Assert.Single(typed.RegionCandidates, candidate => candidate.SourceName == region.SourceName).RequiresLocalOwnershipGuard);
        Assert.Equal(2, typed.PromotedRegions.Count(candidate => candidate.SourceName == "Get-CombinedRegions"));
        Assert.All(typed.PromotedRegions, promoted =>
            Assert.All(new[] { typed.IrSnapshots!.Bound, typed.IrSnapshots.Lowered }, units =>
                Assert.Equal(promoted.RequiresLocalOwnershipGuard ? new[] { "FreshInvocationLocalTargets" } : Array.Empty<string>(),
                    Assert.Single(units, unit => unit.UnitId == promoted.RegionId).ExecutionConditions)));
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<PowerShellCompiledRegion>(
            System.Text.Json.JsonSerializer.Serialize(region))!.RequiresLocalOwnershipGuard);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.RegionAssignmentOwnership",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(4, result.Manifest!.PromotedTypedRegions);
        const string probe = """
            $ErrorActionPreference = 'SilentlyContinue'
            $observations = @(foreach ($storage in @('Fresh','ReadOnly','Mutable','Validated')) {
                foreach ($mode in @('Continue','SilentlyContinue','Ignore','Stop')) {
                    & $module {
                        param($storage)
                        Remove-Variable -Scope Script -Name ProtectedRegionValue -Force -ErrorAction SilentlyContinue
                        if ($storage -eq 'ReadOnly') {
                            New-Variable -Scope Script -Name ProtectedRegionValue -Value 9 -Option AllScope,ReadOnly
                        } elseif ($storage -eq 'Mutable') {
                            New-Variable -Scope Script -Name ProtectedRegionValue -Value 9 -Option AllScope
                        } elseif ($storage -eq 'Validated') {
                            New-Variable -Scope Script -Name ProtectedRegionValue -Value 0 -Option AllScope
                            $variable = $ExecutionContext.SessionState.PSVariable.Get('script:ProtectedRegionValue')
                            $variable.Attributes.Add([System.Management.Automation.ValidateRangeAttribute]::new(0, 1))
                        }
                    } $storage
                    $Error.Clear()
                    $seenErrors = @()
                    $caught = $null
                    $values = @()
                    if ($mode -eq 'Stop') {
                        try { $values = @(Get-AssignmentOwnership $true -ErrorAction $mode -ErrorVariable seenErrors 2>$null) }
                        catch { $caught = $_.Exception.GetType().FullName }
                    } else {
                        $values = @(Get-AssignmentOwnership $true -ErrorAction $mode -ErrorVariable seenErrors 2>$null)
                    }
                    $errors = @($Error | ForEach-Object {
                        [pscustomobject]@{
                            Id = $_.FullyQualifiedErrorId
                            Line = $_.InvocationInfo.ScriptLineNumber
                            Column = $_.InvocationInfo.OffsetInLine
                            Text = $_.InvocationInfo.Line.TrimEnd()
                            Command = $_.InvocationInfo.MyCommand.Name
                        }
                    })
                    $state = & $module {
                        $variable = $ExecutionContext.SessionState.PSVariable.Get('script:ProtectedRegionValue')
                        if ($null -ne $variable) { $variable.Value }
                    }
                    $combinedCaught = $null
                    $combined = @()
                    $Error.Clear()
                    if ($mode -eq 'Stop') {
                        try { $combined = @(Get-CombinedRegions $true 42 -ErrorAction $mode 2>$null) }
                        catch { $combinedCaught = $_.Exception.GetType().FullName }
                    } else {
                        $combined = @(Get-CombinedRegions $true 42 -ErrorAction $mode 2>$null)
                    }
                    $combinedErrors = @($Error | ForEach-Object {
                        [pscustomobject]@{ Id = $_.FullyQualifiedErrorId; Line = $_.InvocationInfo.ScriptLineNumber; Column = $_.InvocationInfo.OffsetInLine; Text = $_.InvocationInfo.Line.TrimEnd() }
                    })
                    [pscustomobject]@{ Storage = $storage; Mode = $mode; Values = $values; State = $state; Errors = $errors; ErrorVariableCount = $seenErrors.Count; Caught = $caught; Combined = $combined; CombinedCaught = $combinedCaught; CombinedErrors = $combinedErrors }
                }
            })
            & $module { Remove-Variable -Scope Script -Name ProtectedRegionValue -Force -ErrorAction SilentlyContinue }
            $Error.Clear()
            $seenErrors = @()
            $caught = $null
            try { Get-AssignmentOwnership $true -FailAfter $true -ErrorVariable seenErrors 2>$null }
            catch { $caught = $_.Exception.GetType().FullName }
            $observations += [pscustomobject]@{
                Storage = 'FreshFailure'
                Errors = @($Error | ForEach-Object {
                    [pscustomobject]@{ Id = $_.FullyQualifiedErrorId; Line = $_.InvocationInfo.ScriptLineNumber; Column = $_.InvocationInfo.OffsetInLine; Text = $_.InvocationInfo.Line.TrimEnd() }
                })
                ErrorVariableCount = $seenErrors.Count
                Caught = $caught
            }
            $metadata = Get-Command Get-RegionMetadata
            $observations += [pscustomobject]@{
                Storage = 'Metadata'
                ParameterSets = @($metadata.ParameterSets.Name | Sort-Object)
                Aliases = @($metadata.Parameters['Enabled'].Aliases)
                WhatIf = $metadata.Parameters.ContainsKey('WhatIf')
                Synopsis = (Get-Help Get-RegionMetadata).Synopsis.Trim()
                OutputTypes = @($metadata.OutputType.Name)
                Values = @(Get-RegionMetadata -Go $true; Get-RegionMetadata -Second)
            }
            ConvertTo-Json -InputObject $observations -Depth 8 -Compress
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "$module = Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "' -PassThru; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "$module = Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "' -PassThru; " + probe);
        Assert.Equal(0, original.ExitCode);
        var observations = System.Text.Json.Nodes.JsonNode.Parse(original.StandardOutput)!.AsArray();
        Assert.Equal(18, observations.Count);
        Assert.Equal("2", observations[0]!["Values"]![0]!.GetValue<string>());
        Assert.Equal("9", observations[4]!["Values"]![0]!.GetValue<string>());
        Assert.Equal(2, observations[4]!["Errors"]!.AsArray().Count);
        Assert.Equal("1", observations[12]!["Values"]![0]!.GetValue<string>());
        Assert.Equal("Region ownership metadata.", observations[17]!["Synopsis"]!.GetValue<string>());
        Assert.Equal(new[] { "2", "1" }, observations[17]!["Values"]!.AsArray().Select(value => value!.GetValue<string>()));
        var delivered = System.Text.Json.Nodes.JsonNode.Parse(compiled.StandardOutput)!.AsArray();
        Assert.Equal(observations.Count, delivered.Count);
        for (var index = 0; index < observations.Count; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(observations[index], delivered[index]),
                "Observation " + index + " differs. Original: " + observations[index]!.ToJsonString() + " Artifact: " + delivered[index]!.ToJsonString());
        Assert.Equal((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()),
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()));
    }

    [Theory]
    [InlineData("trap { 99; continue }; throw 'boom'; return 2")]
    [InlineData("if ($true) { trap { 99; continue }; throw 'boom' }; return 2")]
    [InlineData("try { trap { 99; continue }; throw 'boom' } finally { 'done' }; return 2")]
    public void Transpile_HybridRetainsUnrepresentedTrapRoutes(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-TrapOwnership { [CmdletBinding()] param([int] $Number); " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "TrapOwnership", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(typed.Methods);
        Assert.Contains(typed.Diagnostics, diagnostic => diagnostic.Message.Contains("trap", StringComparison.OrdinalIgnoreCase));
    }
}

using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompleteWorkflow_PinnedEnumHashtablePreservesIdentityErrorsStoppingAndState(
        string framework,
        string host)
    {
        var source = FindCompleteConversionWorkflow(
            "PSSharedGoods",
            "FullModule",
            "Private",
            "Deprecated",
            "Objects",
            "Get-ObjectEnumValues.ps1");
        Assert.Equal(
            "d5622ecacebcc19f16d793c1527ad2bb5233398dc78f6d6738560cef362682d3",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(
            File.ReadAllText(source) + Environment.NewLine + "Export-ModuleMember -Function Get-ObjectEnumValues" + Environment.NewLine,
            ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "PinnedEnumHashtableMethods",
            framework,
            PowerShellCompilationCapabilities.HybridModule);
        var regions = typed.PromotedRegions.OrderBy(static region => region.StartOffset).ToArray();
        Assert.True(regions.Length == 2,
            "Promoted: " + regions.Length + Environment.NewLine +
            string.Join(Environment.NewLine, typed.Diagnostics.Select(static item => item.Code + ": " + item.Message)) + Environment.NewLine +
            string.Join(Environment.NewLine, typed.RegionCandidates.Select(static item =>
                item.StartLine + "-" + item.EndLine + " inputs=" +
                string.Join(",", item.InputLocals.Select(static local => local.Name + ":" + local.TypeName)) + " " +
                item.DecisionCode + ": " + item.Reason)));
        Assert.Equal(new[] { 24, 29 }, regions.Select(static region => region.StartLine));
        Assert.True(regions[0].RequiresLocalOwnershipGuard);
        Assert.Equal("System.Collections.Hashtable", regions[0].ReturnType);
        var initialized = Assert.Single(regions[0].ContinuationLocals);
        Assert.Equal("enumValues", initialized.Name, ignoreCase: true);
        Assert.False(initialized.HasTypeConstraint);
        Assert.False(regions[1].RequiresLocalOwnershipGuard);
        Assert.Equal("System.Collections.Hashtable", regions[1].ReturnType);
        var input = Assert.Single(regions[1].InputLocals);
        Assert.Equal("enumValues", input.Name, ignoreCase: true);
        Assert.Equal(initialized.TypeName, input.TypeName);
        Assert.False(input.HasTypeConstraint);
        Assert.Empty(regions[1].ContinuationLocals);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "Generated.PinnedEnumHashtable",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.PromotedTypedRegions);
        Assert.Equal(2, Assert.Single(result.Manifest.UnitDispositionLedger!.Entries,
            static entry => entry.Name == "Get-ObjectEnumValues").PromotedTypedRegions);

        const string probe = """
            $ErrorActionPreference = 'Continue'
            function Describe-Table($table) {
                $pairs = @($table.GetEnumerator() | Sort-Object { [int]$_.Value } | ForEach-Object {
                    $_.Key.GetType().FullName + ':' + [string]$_.Key + ':' + $_.Value.GetType().FullName + ':' + [string]$_.Value
                })
                $table.GetType().FullName + '/count=' + $table.Count + '/pairs=' + ($pairs -join ',')
            }
            $first = Get-ObjectEnumValues -enum System.DayOfWeek
            'first=' + (Describe-Table $first)
            $first['owned'] = 99
            $second = Get-ObjectEnumValues -enum System.DayOfWeek
            'state=' + $first.ContainsKey('owned') + '/' + $second.ContainsKey('owned') + '/same=' + [object]::ReferenceEquals($first, $second)
            'ordered-before'
            Describe-Table (Get-ObjectEnumValues -enum System.ConsoleColor)
            'ordered-after'
            $continuedErrors = @()
            $continued = @(Get-ObjectEnumValues -enum System.String -ErrorAction Continue -ErrorVariable continuedErrors 2>$null)
            'continue=' + @($continued).Count + '/errors=' + $continuedErrors.Count + '/type=' + @($continued | ForEach-Object { $_.GetType().FullName })
            $stopped = [Collections.Generic.List[object]]::new()
            try {
                Get-ObjectEnumValues -enum Missing.Type -ErrorAction Stop 2>$null | ForEach-Object { [void]$stopped.Add($_) }
            } catch {
                'stop=outputs:' + $stopped.Count + '/error:' + $_.Exception.GetType().FullName
            }
            $downstream = @(& {
                foreach ($name in 'System.DayOfWeek','System.ConsoleColor') {
                    Get-ObjectEnumValues -enum $name
                    'after:' + $name
                }
            } | Select-Object -First 1)
            'downstream=' + $downstream.Count + '/' + $downstream[0].GetType().FullName
            'reuse=' + (Get-ObjectEnumValues -enum System.DayOfWeek).Count
            Remove-Module $module.Name
            $module = Import-Module $modulePath -PassThru
            'reimport=' + (Get-ObjectEnumValues -enum System.ConsoleColor).Count
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "pinned-enum-hashtable");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath,
            "pinned-enum-hashtable");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("first=System.Collections.Hashtable/count=7", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("state=True/False/same=False", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ordered-before", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ordered-after", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("continue=1/errors=0/type=System.Collections.Hashtable", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stop=outputs:0/error:", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("downstream=1/System.Collections.Hashtable", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reuse=7", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reimport=16", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
        VerifyPinnedEnumHashtableCancellation(fixture, result.ArtifactPath!, host);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridHashtableTransferLeavesMutableOperationOnPowerShellPath()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-HashtableMutationBoundary {
                $table = @{}
                data BeforeMutation { }
                $table.Add('owned', 1)
                data AfterMutation { }
                $table
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HashtableMutationBoundaryMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        var regions = typed.PromotedRegions.OrderBy(static region => region.StartOffset).ToArray();

        Assert.Equal(new[] { 2, 6 }, regions.Select(static region => region.StartLine));
        Assert.DoesNotContain(regions, static region => region.StartLine == 4);
        Assert.All(regions, static region => Assert.Equal("System.Collections.Hashtable", region.ReturnType));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_HybridHashtableParameterReturnRemainsOutsideRegionTransferContract()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-HashtableParameter {
                param([hashtable] $Table)
                $Table
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath },
            "PowerForge.Compiled",
            "HashtableParameterMethods",
            "net10.0",
            PowerShellCompilationCapabilities.HybridModule);

        Assert.DoesNotContain(typed.Methods, static method => method.SourceName == "Get-HashtableParameter");
        Assert.DoesNotContain(typed.PromotedRegions, static region => region.SourceName == "Get-HashtableParameter");
    }

    private static void VerifyPinnedEnumHashtableCancellation(
        ArtifactFixture fixture,
        string artifactPath,
        string host)
    {
        const string probe = AcknowledgedStopProbe + """
            Add-Type -TypeDefinition @'
            using System.Threading;
            public sealed class EnumHashtableStopState {
                public readonly ManualResetEvent Started = new ManualResetEvent(false);
                public readonly ManualResetEvent Release = new ManualResetEvent(false);
                public int Calls;
                public int Cleanups;
            }
            '@
            $state = [EnumHashtableStopState]::new()
            $ps = [powershell]::Create()
            try {
                $configure = {
                    param($path, $state)
                    $target = Import-Module $path -PassThru
                    & $target {
                        param($state)
                        $script:EnumHashtableStopState = $state
                        function script:ForEach-Object {
                            [CmdletBinding()]
                            param(
                                [Parameter(Position = 0, Mandatory = $true)][scriptblock] $Process,
                                [Parameter(ValueFromPipeline = $true)][object] $InputObject
                            )
                            process {
                                try {
                                    $script:EnumHashtableStopState.Calls++
                                    [void]$script:EnumHashtableStopState.Started.Set()
                                    if (!$script:EnumHashtableStopState.Release.WaitOne(10000)) {
                                        throw 'Retained provider was not released.'
                                    }
                                    & $Process
                                } finally {
                                    $script:EnumHashtableStopState.Cleanups++
                                }
                            }
                        }
                    } $state
                    $target.Name + '\Get-ObjectEnumValues'
                }
                [void]$ps.AddScript($configure.ToString()).AddArgument($modulePath).AddArgument($state)
                $selected = @($ps.Invoke())
                if ($ps.HadErrors -or $selected.Count -ne 1) { throw ('Setup failed: ' + $ps.Streams.Error) }
                $ps.Commands.Clear()
                [void]$ps.AddCommand([string]$selected[0]).AddParameter('enum', 'System.DayOfWeek')
                $running = $ps.BeginInvoke()
                if (!$state.Started.WaitOne(10000)) { throw ('Retained stage was not entered: ' + $ps.InvocationStateInfo.State) }
                $stop=Start-CompilerTestStop $ps
                [void]$state.Release.Set()
                if (!$stop.AsyncWaitHandle.WaitOne(5000)) { throw 'Cancellation did not complete.' }
                $ps.EndStop($stop)
                $records = @()
                try { $records = @($ps.EndInvoke($running)) } catch [Management.Automation.PipelineStoppedException] { }
                'cancel=' + $ps.InvocationStateInfo.State + '/calls=' + $state.Calls + '/cleanups=' + $state.Cleanups + '/records=' + $records.Count
                $ps.Commands.Clear()
                $ps.Streams.Error.Clear()
                $reuse = {
                    param($path)
                    Get-Module | Where-Object Path -eq $path | Remove-Module -Force
                    Import-Module $path
                    (Get-ObjectEnumValues -enum System.DayOfWeek).Count
                }
                [void]$ps.AddScript($reuse.ToString()).AddArgument($modulePath)
                $later = @($ps.Invoke())
                'reuse=' + $later.Count + '/' + $later[0] + '/errors=' + $ps.Streams.Error.Count
            } finally {
                [void]$state.Release.Set()
                $ps.Dispose()
                $state.Started.Dispose()
                $state.Release.Dispose()
            }
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath,
            "original-pinned-enum-hashtable-cancellation");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(artifactPath) + "'; " + probe,
            fixture.RootPath,
            "compiled-pinned-enum-hashtable-cancellation");

        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Contains("cancel=Stopped/calls=1/cleanups=1/records=0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reuse=1/7/errors=0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}

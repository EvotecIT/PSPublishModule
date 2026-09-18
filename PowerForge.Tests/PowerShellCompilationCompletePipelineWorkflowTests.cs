using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    private static readonly (string[] Path, string Sha256)[] CompletePipelineWorkflows =
    {
        (new[] { "PSSharedGoods", "FullModule", "Private", "Deprecated", "Objects", "Get-ObjectEnumValues.ps1" },
            "d5622ecacebcc19f16d793c1527ad2bb5233398dc78f6d6738560cef362682d3"),
        (new[] { "PSSharedGoods", "FullModule", "Public", "Connectivity", "Get-MyIpAddress.ps1" },
            "ffc1bae3ebb7444de354812f9668430b8b397f2cd642b552f3b692fefa4e1183"),
        (new[] { "PSSharedGoods", "FullModule", "Public", "Objects", "Remove-DuplicateObjects.ps1" },
            "4278aada6b66f0e90cbf4ed26ca85890c1fd0b0bbb8a0162ed29762e2617da3b")
    };

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompletePipelines_SelectNativeOwnershipWithoutBroadeningRuntimeFreeCompilation()
    {
        const string source = """
            function Read-TypedValue { param([int]$Value); return $Value }
            function Read-CompletePipeline { param([object[]]$Values); return $Values | ForEach-Object { $_ } }
            function Read-SuppressedPipeline { param([object[]]$Values); $Values | Out-Null; return 9 }
            function Read-QualifiedSuppressedPipeline { param([object[]]$Values); $Values | Microsoft.PowerShell.Core\Out-Null; return 9 }
            function Read-ParenthesizedProducerPipeline {
                $map=@{}
                ([enum]::GetValues([DayOfWeek])) | ForEach-Object { $map.Add($_,$_.value__) }
                $map
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var transpiler = new PowerShellTypedCompilationTranspiler();
        var hybrid = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath }, "Generated.CompletePipelinePolicy", "Methods",
            "net10.0", PowerShellCompilationCapabilities.HybridModule);
        var typed = Assert.Single(hybrid.Methods, static method => method.SourceName == "Read-TypedValue");
        Assert.Null(typed.NativeFunctionBinding);
        var complete = Assert.Single(hybrid.Methods, static method => method.SourceName == "Read-CompletePipeline");
        Assert.NotNull(complete.NativeFunctionBinding);
        Assert.NotNull(complete.RegionGraph);
        Assert.DoesNotContain(hybrid.Methods, static method => method.SourceName == "Read-SuppressedPipeline");
        Assert.DoesNotContain(hybrid.Methods, static method => method.SourceName == "Read-QualifiedSuppressedPipeline");
        Assert.DoesNotContain(hybrid.Methods, static method => method.SourceName == "Read-ParenthesizedProducerPipeline");
        Assert.Equal(2, hybrid.PromotedRegions.Count(static region => region.SourceName == "Read-ParenthesizedProducerPipeline"));

        var runtimeFree = transpiler.TranspileForBinaryModule(new[] { fixture.ScriptPath }, "Generated.CompletePipelineStrict", "Methods", "net10.0");
        Assert.Single(runtimeFree.Methods, static method => method.SourceName == "Read-TypedValue");
        Assert.DoesNotContain(runtimeFree.Methods, static method => method.SourceName == "Read-CompletePipeline");
        Assert.DoesNotContain(runtimeFree.Methods, static method => method.SourceName == "Read-SuppressedPipeline");
        Assert.DoesNotContain(runtimeFree.Methods, static method => method.SourceName == "Read-QualifiedSuppressedPipeline");
        Assert.DoesNotContain(runtimeFree.Methods, static method => method.SourceName == "Read-ParenthesizedProducerPipeline");
        Assert.Contains(runtimeFree.Diagnostics, static diagnostic =>
            diagnostic.Message.Contains("pipeline", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompletePipeline_PinnedEnumMapRetainsTypedContinuationOwnershipAndMutation(string framework, string host)
    {
        var workflow = CompletePipelineWorkflows[0];
        var path = FindCompleteConversionWorkflow(workflow.Path);
        Assert.Equal(workflow.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(path), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompleteEnumPipeline", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.False(unit.EmittedClrMethod);
        Assert.False(unit.UsesNativeFunctionBinding);
        Assert.Equal(2, unit.PromotedTypedRegions);
        Assert.True(unit.RetainedHostedSource);
        const string probe = """
            foreach($type in 'System.DayOfWeek','System.ConsoleColor') {
                $map=Get-ObjectEnumValues -enum $type
                $values=@($map.Keys | Sort-Object | ForEach-Object { [string]$_+':' + $map[$_] })
                $type+':'+$map.GetType().FullName+':'+($values -join ',')
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "';" + probe,
            fixture.RootPath, "complete-enum-pipeline-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "';" + probe,
            fixture.RootPath, "complete-enum-pipeline-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("Friday:5", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("DarkRed:4", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void CompletePipelines_PreserveLifecycleStreamsRetainedCollectionsAndRealWorkflows(string framework, string host)
    {
        var pinnedSources = CompletePipelineWorkflows.Select(workflow =>
        {
            var path = FindCompleteConversionWorkflow(workflow.Path);
            Assert.Equal(workflow.Sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            return File.ReadAllText(path);
        });
        const string contractSource = """
            function Invoke-CompletePipelineLifecycle {
                [CmdletBinding()] param(
                    [Parameter(ValueFromPipeline)][AllowNull()][object]$Value,
                    [Parameter(Mandatory)][object]$Trace)
                begin { [void]$Trace.Add('begin') }
                process {
                    [void]$Trace.Add('process:' + [string]$Value)
                    if ($Value -eq 'bad') { Write-Error 'bad-value' }
                    Write-Warning ('warning:' + [string]$Value)
                    Write-Information ('information:' + [string]$Value) -InformationAction Continue
                    $Value
                }
                end { [void]$Trace.Add('end') }
            }
            function Invoke-CompletePipelineMutation {
                [CmdletBinding()] param(
                    [AllowNull()][object[]]$Values,
                    [Parameter(Mandatory)][object]$Map,
                    [Parameter(Mandatory)][object]$Ordered,
                    [Parameter(Mandatory)][object]$ArrayList,
                    [Parameter(Mandatory)][object]$List,
                    [Parameter(Mandatory)][object]$Array,
                    [Parameter(Mandatory)][object]$Trace)
                $index=0
                $Values | Invoke-CompletePipelineLifecycle -Trace $Trace | CompletePipelineEach {
                    $key=[string]$_
                    $Map.Add($key,$index)
                    $Ordered.Add($key,$index)
                    [void]$ArrayList.Add($key)
                    [void]$List.Add($key)
                    $Array[$index]=$key
                    $index+=1
                    'stage:'+$key
                }
                'after:'+$index
            }
            """;
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, pinnedSources.Append(contractSource)), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompletePipelines", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.Equal(4, result.Manifest.CompiledMethods);
        var units = result.Manifest.UnitDispositionLedger!.Entries;
        var enumUnit = Assert.Single(units, static unit => unit.Name == "Get-ObjectEnumValues");
        Assert.False(enumUnit.EmittedClrMethod);
        Assert.False(enumUnit.UsesNativeFunctionBinding);
        Assert.Equal(2, enumUnit.PromotedTypedRegions);
        Assert.True(enumUnit.RetainedHostedSource);
        Assert.All(units.Where(static unit => unit.Name != "Get-ObjectEnumValues"), unit =>
        {
            Assert.True(unit.EmittedClrMethod, unit.Name);
            Assert.True(unit.UsesNativeFunctionBinding, unit.Name);
            Assert.True(unit.RuntimeCommandRegions > 0, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        });

        const string probe = """
            Set-Alias -Name CompletePipelineEach -Value ForEach-Object -Scope Global
            function Describe-CompletePipelineRecord($record) {
                if ($record -is [Management.Automation.ErrorRecord]) { return 'error:'+$record.FullyQualifiedErrorId+':'+$record.Exception.GetType().FullName }
                if ($record -is [Management.Automation.WarningRecord]) { return 'warning:'+$record.Message }
                if ($record -is [Management.Automation.InformationRecord]) { return 'information:'+[string]$record.MessageData }
                if ($record -is [Management.Automation.IContainsErrorRecord]) { return 'contained:'+$record.ErrorRecord.FullyQualifiedErrorId+':'+$record.GetType().FullName }
                if ($record -is [Exception]) { return 'exception:'+$record.GetType().FullName+':'+$record.Message }
                if ($null -eq $record) { return 'output:null' }
                return 'output:'+$record.GetType().FullName+':'+[string]$record
            }
            $items=@([pscustomobject]@{Name='A';Value=1},[pscustomobject]@{Name='A';Value=2},[pscustomobject]@{Name='B';Value=3})
            'unique:'+((Remove-DuplicateObjects -Object $items -Property Name | ForEach-Object Name) -join ',')
            $cases=@(
                @{name='empty';values=@();capacity=0},
                @{name='single';values=@('one');capacity=1},
                @{name='many';values=@('one','two','three');capacity=3},
                @{name='null';values=@($null);capacity=1},
                @{name='duplicate';values=@('one','one','two');capacity=3},
                @{name='bounds';values=@('one','two');capacity=1},
                @{name='error';values=@('one','bad','two');capacity=3})
            foreach($case in $cases) {
                foreach($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    foreach($first in $false,$true) {
                        $map=@{}; $ordered=[Collections.Specialized.OrderedDictionary]::new()
                        $arrayList=[Collections.ArrayList]::new(); $list=[Collections.Generic.List[string]]::new()
                        $array=[string[]]::new($case.capacity); $trace=[Collections.Generic.List[string]]::new()
                        $faults=@(); $Error.Clear(); $caught=$null
                        try {
                            if($first) {
                                $records=@(Invoke-CompletePipelineMutation -Values $case.values -Map $map -Ordered $ordered -ArrayList $arrayList -List $list -Array $array -Trace $trace -ErrorAction $action -ErrorVariable faults *>&1 | Select-Object -First 2)
                            } else {
                                $records=@(Invoke-CompletePipelineMutation -Values $case.values -Map $map -Ordered $ordered -ArrayList $arrayList -List $list -Array $array -Trace $trace -ErrorAction $action -ErrorVariable faults *>&1)
                            }
                        } catch { $caught=Describe-CompletePipelineRecord $_ }
                        [pscustomobject]@{
                            case=$case.name;action=$action;first=$first
                            records=@($records | ForEach-Object { Describe-CompletePipelineRecord $_ })
                            caught=$caught;faults=@($faults | ForEach-Object { Describe-CompletePipelineRecord $_ })
                            trace=@($trace);map=@($map.Keys | Sort-Object);ordered=@($ordered.Keys)
                            arrayList=@($arrayList);list=@($list);array=@($array | ForEach-Object { if($null -eq $_){'<null>'}else{$_} })
                        } | ConvertTo-Json -Compress -Depth 8
                    }
                }
            }
            Remove-Module $module.Name -Force
            $module=Import-Module $modulePath -PassThru -Force
            $map=@{}; $ordered=[Collections.Specialized.OrderedDictionary]::new()
            $arrayList=[Collections.ArrayList]::new(); $list=[Collections.Generic.List[string]]::new()
            $array=[string[]]::new(1); $trace=[Collections.Generic.List[string]]::new()
            'reimport:'+((Invoke-CompletePipelineMutation -Values 'again' -Map $map -Ordered $ordered -ArrayList $arrayList -List $list -Array $array -Trace $trace 3>$null 6>$null) -join ',')+':'+($trace -join ',')
            """;
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "';$module=Import-Module $modulePath -PassThru;" + probe,
            fixture.RootPath, "complete-pipeline-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "';$module=Import-Module $modulePath -PassThru;" + probe,
            fixture.RootPath, "complete-pipeline-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Equal(7 * 4 * 2 + 2, original.StandardOutput.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Contains("unique:A,B", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("reimport:stage:again,after:1:begin,process:again,end", original.StandardOutput, StringComparison.Ordinal);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            string.Join(Environment.NewLine, original.StandardOutput.Split('\n').Zip(compiled.StandardOutput.Split('\n'))
                .Where(pair => pair.First != pair.Second).Take(8)
                .Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}

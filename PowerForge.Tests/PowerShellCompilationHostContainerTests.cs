using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeHostContainers_PreserveBindingIdentityAndOfflineSidSelection(string framework, string host)
    {
        var request = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Request-ADSIDHistory.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(request) + Environment.NewLine + """
            function Write-Color { [CmdletBinding()]param([object]$Text,[object]$Color) }
            function Write-HostContainers {
                [CmdletBinding()]param(
                    [System.Collections.Generic.List[PSObject]]$Values,
                    [System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[PSObject]]]$Lookup,
                    [System.Collections.Generic.HashSet[PSObject]]$Unique,
                    [nullable[System.Management.Automation.ErrorCategory]]$Category,
                    [PSObject]$Item,[string]$Key,[System.Collections.Generic.List[string]]$Trace)
                try {
                    $Values.Add($Item)
                    $Lookup.Add($Key,$Values)
                    $added=$Unique.Add($Item)
                    [pscustomobject]@{added=$added;category=$Category;count=$Values.Count}
                } catch { 'error:'+ $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName+':'+$_.CategoryInfo.Category+':'+$_.InvocationInfo.OffsetInLine }
                finally { $Trace.Add('finally') }
            }
            function New-HostContainer {
                [CmdletBinding()]param([PSObject]$Item)
                $values=[System.Collections.Generic.List[PSObject]]::new()
                $values.Add($Item)
                return ,$values
            }
            function Invoke-HostContainerCallbacks {
                [CmdletBinding()]param([System.Collections.Generic.List[scriptblock]]$Callbacks)
                foreach($callback in $Callbacks) { $callback.Invoke() }
            }
            function Read-ConvertedHostList {
                [CmdletBinding()]param([System.Collections.Generic.List[PSObject]]$Values)
                if($null -eq $Values) { return 'null' }
                $Values.GetType().GetGenericArguments()[0].FullName
                $Values.Count
                foreach($entry in $Values) { $entry }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HostContainers", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Request-ADSIDHistory", "Write-HostContainers", "New-HostContainer", "Invoke-HostContainerCallbacks", "Read-ConvertedHostList" })
            Assert.Contains(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name && unit.EmittedClrMethod && unit.UsesNativeFunctionBinding);
        const string probe = """
            foreach($category in $null,'InvalidOperation') {
                $values=[Collections.Generic.List[psobject]]::new()
                $lookup=[Collections.Generic.Dictionary[string,Collections.Generic.List[psobject]]]::new()
                $unique=[Collections.Generic.HashSet[psobject]]::new()
                $item=[pscustomobject]@{Name='owned';Value=0}
                $trace=[Collections.Generic.List[string]]::new()
                foreach($key in 'first','first','second') {
                    $result=@(Write-HostContainers -Values $values -Lookup $lookup -Unique $unique -Category $category -Item $item -Key $key -Trace $trace)
                    [pscustomobject]@{category=$category;key=$key;result=$result;values=@($values|ForEach-Object {$_.Name});unique=$unique.Count;lookup=$lookup.Count;same=[object]::ReferenceEquals($lookup['first'],$values);trace=@($trace)}|ConvertTo-Json -Depth 6 -Compress
                }
            }
            $created=New-HostContainer -Item ([pscustomobject]@{Name='created'})
            [pscustomobject]@{container=$created.GetType().GetGenericTypeDefinition().FullName;element=$created.GetType().GetGenericArguments()[0].FullName;count=$created.Count;name=$created[0].Name}|ConvertTo-Json -Compress
            $marker='closed'
            $callbacks=[Collections.Generic.List[scriptblock]]::new()
            $callbacks.Add({$marker}.GetNewClosure())
            $marker='caller'
            [pscustomobject]@{callbacks=@(Invoke-HostContainerCallbacks -Callbacks $callbacks)}|ConvertTo-Json -Compress
            foreach($limit in 0,1,2) {
                foreach($disabledOnly in $false,$true) {
                    foreach($includeType in @(),@('External')) {
                        $first=[pscustomobject]@{Name='one';Domain='local';Enabled=$false;ObjectClass='user';OrganizationalUnit='OU=Owned';SIDHistory=@('S-1-5-21-1-10','S-1-5-21-2-20','unrelated')}
                        $second=[pscustomobject]@{Name='two';Domain='local';Enabled=$true;ObjectClass='computer';OrganizationalUnit='OU=Other';SIDHistory=@('S-1-5-21-1-11')}
                        $output=@{'S-1-5-21-1'=@($first,$second);'S-1-5-21-2'=@($first);DomainSIDs=@{'S-1-5-21-1'=[pscustomobject]@{Type='Domain';Domain='owned.internal'};'S-1-5-21-2'=[pscustomobject]@{Type='Trust';Domain='owned.external'}}}
                        $forest=@{QueryServers=@{local=[pscustomobject]@{HostName=@('offline.invalid')}}}
                        $selected=[Collections.Generic.List[pscustomobject]]::new()
                        $result=@(Request-ADSIDHistory -DomainNames @('S-1-5-21-1','S-1-5-21-2') -Output $output -ForestInformation $forest -ObjectsToProcess $selected -DisabledOnly:$disabledOnly -IncludeType $includeType -RemoveLimitObject $limit -LimitPerObject ($limit -gt 0))
                        $rows=@($selected|ForEach-Object {[pscustomobject]@{name=$_.Object.Name;domain=$_.Domain;type=$_.DomainType;server=$_.QueryServer;sids=@($_.SIDHistoryToRemove);same=[object]::ReferenceEquals($_.Object,$first)}})
                        [pscustomobject]@{limit=$limit;disabled=$disabledOnly;include=@($includeType);result=$result;rows=$rows}|ConvertTo-Json -Depth 6 -Compress
                    }
                }
            }
            foreach($mode in 'null','scalar','array','empty','typed') {
                $parameters=@{Values=$null}
                if($mode -eq 'scalar') {$parameters.Values=42}
                elseif($mode -eq 'array') {$parameters.Values=@(0,42)}
                elseif($mode -eq 'empty') {$parameters.Values=@()}
                elseif($mode -eq 'typed') {$parameters.Values=$created}
                [pscustomobject]@{conversion=$mode;result=@(Read-ConvertedHostList @parameters)}|ConvertTo-Json -Depth 5 -Compress
            }
            try { Write-HostContainers -Lookup 42 -ErrorAction Stop } catch { 'binding:'+ $_.FullyQualifiedErrorId }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"same\":true", generated);
        Assert.Contains("\"callbacks\":[\"closed\"]", generated);
        Assert.Contains("offline.invalid", generated);
        Assert.Contains("ParameterArgumentTransformationError", generated);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void HostContainers_StaticSdkArgumentUsesTargetShape(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function New-SerializerList {
                [CmdletBinding()]param()
                return ,[Collections.Generic.List[Management.Automation.PSSerializer]]::new()
            }
            function New-SerializerArray {
                [CmdletBinding()]param()
                return ,[Management.Automation.PSSerializer[]]@()
            }
            function New-MathArray {
                [CmdletBinding()]param()
                return ,[math[]]@()
            }
            """, ".psm1");
        const string probe = """
            $value=New-SerializerList
            [pscustomobject]@{count=$value.Count;argument=$value.GetType().GetGenericArguments()[0].FullName}|ConvertTo-Json -Compress
            foreach($name in 'New-SerializerArray','New-MathArray') {
                $value=& $name
                [pscustomobject]@{name=$name;count=$value.Count;element=$value.GetType().GetElementType().FullName}|ConvertTo-Json -Compress
            }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        Assert.Contains("\"count\":0", original);
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticHostContainer", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(framework == "net472", Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "New-SerializerList").EmittedClrMethod);
        foreach (var name in new[] { "New-SerializerArray", "New-MathArray" })
            Assert.Contains(built.Manifest.UnitDispositionLedger.Entries, unit => unit.Name == name && unit.EmittedClrMethod);
        Assert.Equal(original, RunModuleProof(built.ArtifactPath!, probe, host));
    }

    [Theory]
    [InlineData("[Collections.Generic.HashSet[Management.Automation.PSSerializer]]::new()")]
    [InlineData("[Collections.Generic.Dictionary[string,Management.Automation.PSSerializer]]::new()")]
    [InlineData("[Collections.Generic.List[Collections.Generic.List[Management.Automation.PSSerializer]]]::new()")]
    [InlineData("[Collections.Generic.List[Management.Automation.PSSerializer[]]]::new()")]
    [InlineData("[Collections.Generic.List[math]]::new()")]
    public void HostContainers_StaticElementShapesRemainHostedOnModernTarget(string expression)
    {
        using var fixture = ArtifactFixture.Create("function New-StaticContainer { [CmdletBinding()]param();return ,"+expression+" }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "Generated.StaticElementBoundary", "CompiledPowerShell", "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(result.Methods);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List[PSObject]", false)]
    [InlineData("System.Collections.Generic.Dictionary[string,System.Management.Automation.ErrorRecord]", false)]
    [InlineData("System.Collections.Generic.List[System.Management.Automation.Runspaces.Runspace]", true)]
    [InlineData("System.Collections.Generic.IEnumerable[PSObject]", true)]
    public void HostContainers_UnqualifiedElementsOrHostFreeTargetsRemainClosed(string typeName, bool hybrid)
    {
        using var fixture = ArtifactFixture.Create("function Read-Container { [CmdletBinding()]param(["+typeName+"]$Value);return $Value }", ".psm1");
        var capabilities = hybrid ? PowerShellCompilationCapabilities.HybridModule : PowerShellCompilationCapabilities.TypedExecutable;
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
            fixture.ScriptPath, hybrid ? PowerShellCompilationMode.Hybrid : PowerShellCompilationMode.Strict,
            targetFramework: "net10.0", capabilities: capabilities));
        Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
    }
}

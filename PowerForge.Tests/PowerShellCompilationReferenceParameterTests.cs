using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void ReferenceParameter_PreservesCallerCellsForwardingAndOfflineChildDeletion(string framework, string host)
    {
        var paths = new[] {
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Objects", "Remove-EmptyValue.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Remove-ChildItems.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Remove-FileItem.ps1")
        };
        Assert.Equal(new[] { "AA13FE23888586CC76B0CD32B01BF95EA1445F06E3385E08BE134397FC0939B0", "B368FBA68B2315F9C2A9E0A16504349E71BDAE2BCA210F8A74F06BC27A2B6193", "E1CE8D334214A50E92F269312B597A32B62726A26F4D0BD0ED7810671426FEEF" },
            paths.Select(path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))).ToArray());
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, paths.Select(File.ReadAllText)) + """

            function Set-OwnedReference {
                [CmdletBinding()]param([ref]$Value,$Next,$Trace,[switch]$Fail)
                try {
                    $Trace.Add('before')
                    $Value.Value=$Next
                    $Trace.Add('written')
                    if($Fail){throw [InvalidOperationException]::new('after-write')}
                } catch {[pscustomobject]@{kind='error';id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally {$Trace.Add('finally')}
                [pscustomobject]@{kind='reference';same=[object]::ReferenceEquals($Value,$PSBoundParameters.Value);value=$Value.Value;trace=@($Trace.ToArray())}
            }
            function Invoke-ForwardReference {
                [CmdletBinding()]param([ref]$Value,$Next,$Trace,[switch]$Fail)
                Set-OwnedReference @PSBoundParameters
            }
            function Save-OwnedReference {
                [CmdletBinding()]param([ref]$Value,$Store)
                $Store['reference']=$Value
            }
            function Set-ReferenceVector {
                [CmdletBinding()]param([ref[]]$Values,$Next)
                foreach($item in $Values){$item.Value=$Next}
            }
            function Set-ReferenceList {
                [CmdletBinding()]param([Collections.Generic.List[System.Management.Automation.PSReference]]$Values,$Next)
                foreach($item in $Values){$item.Value=$Next}
            }
            function Save-LocalReference {
                [CmdletBinding()]param($Store,[switch]$Missing)
                $ownedLocalReferenceValue='initial'
                if($Missing){Remove-Variable ownedLocalReferenceValue}
                try {
                    $Store.reference=[ref]$ownedLocalReferenceValue
                    $Store.reference.Value='inside'
                    $Store.read=$ownedLocalReferenceValue
                } catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ReferenceParameters", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(9, result.Manifest!.CompiledMethods);
        foreach(var name in new[] { "Remove-ChildItems", "Set-OwnedReference", "Invoke-ForwardReference", "Save-OwnedReference", "Set-ReferenceVector", "Set-ReferenceList", "Save-LocalReference" }) {
            var entry = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, entry => entry.Name == name);
            Assert.True(entry.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(entry));
            Assert.True(entry.UsesNativeFunctionBinding);
            Assert.False(entry.RetainedHostedSource);
        }
        var probe = """
            foreach($forward in $false,$true){foreach($fail in $false,$true){
                $value='prior';$trace=[Collections.Generic.List[string]]::new();$cell=[ref]$value
                $records=if($forward){@(Invoke-ForwardReference -Value $cell -Next 'changed' -Trace $trace -Fail:$fail)}else{@(Set-OwnedReference -Value $cell -Next 'changed' -Trace $trace -Fail:$fail)}
                [pscustomobject]@{kind='cell';forward=$forward;fail=$fail;caller=$value;records=$records}|ConvertTo-Json -Depth 8 -Compress
            }}
            foreach($next in '42','not-number',$null){
                [int]$typed=7;$trace=[Collections.Generic.List[string]]::new()
                [pscustomobject]@{kind='constraint';next=$next;records=@(Set-OwnedReference -Value ([ref]$typed) -Next $next -Trace $trace);caller=$typed}|ConvertTo-Json -Depth 8 -Compress
            }
            $value=3;$store=@{};$cell=[ref]$value;Save-OwnedReference -Value $cell -Store $store
            $store.reference.Value=99
            [pscustomobject]@{kind='retained';same=[object]::ReferenceEquals($cell,$store.reference);caller=$value}|ConvertTo-Json -Compress
            $a=1;$b=2;Set-ReferenceVector -Values @([ref]$a,[ref]$b) -Next 33
            [pscustomobject]@{kind='vector';a=$a;b=$b}|ConvertTo-Json -Compress
            $a=1;$b=2;$list=[Collections.Generic.List[Management.Automation.PSReference]]::new();$list.Add([ref]$a);$list.Add([ref]$b)
            Set-ReferenceList -Values $list -Next 44
            [pscustomobject]@{kind='list';a=$a;b=$b}|ConvertTo-Json -Compress
            foreach($missing in $false,$true){
                $store=@{};$records=@(Save-LocalReference -Store $store -Missing:$missing)
                if($store.ContainsKey('reference')){$store.reference.Value='after'}
                [pscustomobject]@{kind='local';missing=$missing;records=$records;read=$store.read;retained=$store.reference.Value}|ConvertTo-Json -Depth 8 -Compress
            }
            foreach($argument in $null,42,'scalar'){
                try {Set-OwnedReference -Value $argument -Next 12 -Trace ([Collections.Generic.List[string]]::new())|ConvertTo-Json -Depth 8 -Compress}
                catch {[pscustomobject]@{kind='binding';input=$argument;id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName}|ConvertTo-Json -Compress}
            }
            foreach($case in 'files','nonempty-directory','recursive','whatif','include','exclude','zero-retries'){
                $root='OWNED_ROOT'
                if(Test-Path -LiteralPath $root){[IO.Directory]::Delete($root,$true)}
                [void][IO.Directory]::CreateDirectory($root)
                [IO.File]::WriteAllText((Join-Path $root 'one.txt'),'owned')
                [IO.File]::WriteAllText((Join-Path $root 'two.log'),'owned')
                if($case -in 'nonempty-directory','recursive'){
                    [void][IO.Directory]::CreateDirectory((Join-Path $root 'nested'))
                    [IO.File]::WriteAllText((Join-Path $root 'nested/child.txt'),'owned')
                }
                $options=@{Paths=$root;DeleteMethod='DotNetDelete';SkipTopFolder=$true;Recursive=($case -eq 'recursive');WhatIf=($case -eq 'whatif');Confirm=$false;Retries=$(if($case -eq 'zero-retries'){0}else{1});SimpleReturn=$true}
                if($case -eq 'include'){$options.Include='*.txt'}
                if($case -eq 'exclude'){$options.Exclude='*.log'}
                $warnings=@();$records=@(Remove-FileItem @options -WarningVariable warnings 3>$null)
                [pscustomobject]@{kind='children';case=$case;records=$records;rootExists=(Test-Path -LiteralPath $root);remaining=@(Get-ChildItem -LiteralPath $root -Recurse|ForEach-Object {$_.FullName.Substring($root.Length)}|Sort-Object);warnings=@($warnings|ForEach-Object {[string]$_})}|ConvertTo-Json -Depth 8 -Compress
            }
            """.Replace("OWNED_ROOT", EscapeStatementErrorPath(Path.Combine(fixture.RootPath, "owned-children")), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original:" + original + Environment.NewLine + "Generated:" + generated);
        Assert.Contains("\"same\":true", generated);
        Assert.Contains("\"caller\":99", generated);
        Assert.Contains("\"a\":33", generated);
        Assert.Contains("\"a\":44", generated);
        Assert.Contains("\"read\":\"inside\"", generated);
        Assert.Contains("NonExistingVariableReference", generated);
        Assert.Equal(22, generated.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Count(static line => line.StartsWith('{')));
        Assert.Contains("after-write", generated);
        Assert.Contains("\"rootExists\":true", generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void ReferenceParameter_RequiresHostBindingAndKeepsStrictClosed(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Set-Reference {[CmdletBinding()]param([ref]$Value);$Value.Value=42}
            function Read-References {[CmdletBinding()]param([ref[]]$Values);$Values.Count}
            function Read-ReferenceList {[CmdletBinding()]param([Collections.Generic.List[System.Management.Automation.PSReference]]$Values);$Values.Count}
            function Get-Cell {[CmdletBinding()]param([int]$Value);[ref]$Value}
            function Save-Scoped {[CmdletBinding()]param([ref]$Cell,$Store);$Store.reference=[ref]$script:value}
            function Read-Rectangle {[CmdletBinding()]param([ref[,]]$Values);$Values.Count}
            """, Path.Combine(Path.GetTempPath(), "reference-parameter-boundaries.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(4, hybrid.Emitted.Methods.Length);
        Assert.All(hybrid.Emitted.Methods, method => Assert.NotNull(method.NativeFunctionBinding));
        var withoutNativeBinding = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding);
        Assert.Empty(withoutNativeBinding.Emitted.Methods);
        var withoutHostTypes = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes);
        Assert.Empty(withoutHostTypes.Emitted.Methods);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}

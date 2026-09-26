using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeTypeTests_PreserveRuntimeOperandsLazyNamesAndOfflineWorkflows(string framework, string host)
    {
        var reader = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "FilesFolders", "Get-FileEncoding.ps1");
        var converter = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "Convert-FileEncodingSingle.ps1");
        var licenses = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "Convert-Office365License.ps1");
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, File.ReadAllText(reader), File.ReadAllText(converter), File.ReadAllText(licenses)) + Environment.NewLine + """
            function Test-RuntimeType {
                [CmdletBinding()]param([object]$Value,[object]$Target,[bool]$Negate)
                try { if($Negate) { $Value -isnot $Target } else { $Value -is $Target } }
                catch { 'error:'+ $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.CategoryInfo.Category + ':' + $_.InvocationInfo.OffsetInLine }
                finally { 'finally' }
            }
            function Test-DeferredType {
                [CmdletBinding()]param([bool]$Reach,[object]$Value)
                try { if($Reach) { $Value -is [PFC.RuntimeTypeProbe] } else { 'not reached' } }
                catch { 'error:'+ $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.CategoryInfo.Category + ':' + $_.InvocationInfo.OffsetInLine }
                finally { 'finally' }
            }
            function Test-DeferredArrayType {
                [CmdletBinding()]param([object]$Value)
                try { $Value -is [PFC.MissingTypeProbe[]] }
                catch { 'error:'+ $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.CategoryInfo.Category + ':' + $_.InvocationInfo.OffsetInLine }
            }
            function Test-TypeOperandOrder {
                [CmdletBinding()]param([object]$Observer)
                try { $Observer.Value() -is $Observer.Target() }
                catch { 'error:'+ $_.FullyQualifiedErrorId }
            }
            """, ".psm1");
        File.WriteAllText(fixture.ScriptPath, File.ReadAllText(fixture.ScriptPath), new System.Text.UTF8Encoding(true));
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeRuntimeTypes", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Get-FileEncoding", "Convert-Office365License", "Test-RuntimeType", "Test-DeferredType", "Test-DeferredArrayType", "Test-TypeOperandOrder" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        var retainedConverter = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == "Convert-FileEncodingSingle");
        Assert.True(retainedConverter.RetainedHostedSource);
        Assert.Contains(retainedConverter.DiagnosticChain, cause => cause.FeatureId == "syntax.invokememberexpression");
        var data = Path.Combine(fixture.RootPath, "proof-data");
        Directory.CreateDirectory(data);
        var probe = """
            $ErrorActionPreference='Stop'
            $rows=[Collections.Generic.List[string]]::new()
            foreach($target in [int],[psobject],'int','System.String',$null,7,'PFC.MissingTypeProbe') {
                foreach($value in $null,1,[psobject]2,'text',@(1,2)) {
                    foreach($negate in $false,$true) { $rows.Add((@(Test-RuntimeType -Value $value -Target $target -Negate $negate) -join '|')) }
                }
            }
            $rows.Add((@(Test-DeferredType -Reach $false -Value 1) -join '|'))
            $rows.Add((@(Test-DeferredType -Reach $true -Value 1) -join '|'))
            Add-Type 'namespace PFC { public sealed class RuntimeTypeProbe {} }'
            $rows.Add((@(Test-DeferredType -Reach $true -Value ([PFC.RuntimeTypeProbe]::new())) -join '|'))
            $rows.Add((@(Test-DeferredArrayType -Value $null) -join '|'))
            $observer=[pscustomobject]@{Trace=[Collections.Generic.List[string]]::new()}
            $observer | Add-Member ScriptMethod Value {$this.Trace.Add('left');1}
            $observer | Add-Member ScriptMethod Target {$this.Trace.Add('right');[int]}
            $rows.Add((@(Test-TypeOperandOrder -Observer $observer) -join '|')+':'+($observer.Trace -join ','))
            $root='__PROOF_ROOT__'
            foreach($file in Get-ChildItem -LiteralPath $root -File) { Remove-Item -LiteralPath $file.FullName }
            $encodings=@([Text.Encoding]::ASCII,[Text.UTF8Encoding]::new($false),[Text.UTF8Encoding]::new($true),[Text.Encoding]::Unicode,[Text.Encoding]::BigEndianUnicode,[Text.Encoding]::UTF32)
            for($i=0;$i -lt $encodings.Count;$i++) {
                $path=Join-Path $root ('input-'+$i+'.txt')
                $text=if($i -eq 0){'plain'}else{'café'}
                [IO.File]::WriteAllText($path,$text,$encodings[$i])
                $result=Get-FileEncoding -Path $path -AsObject
                $rows.Add($result.EncodingName+':'+$result.Encoding.GetType().FullName+':'+(@(Get-FileEncoding -Path $path) -join '|'))
                # The reader must release the file on both hosts.
                $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$stream.Dispose()
            }
            $conversion=Join-Path $root 'conversion.txt'
            [IO.File]::WriteAllText($conversion,'plain',[Text.Encoding]::ASCII)
            $result=Convert-FileEncodingSingle -FilePath $conversion -SourceEncoding ([Text.Encoding]::ASCII) -TargetEncoding ([Text.UTF8Encoding]::new($true)) -CreateBackup -Confirm:$false
            $rows.Add(($result.Status+':'+$result.TargetEncoding)+':'+[Convert]::ToBase64String([IO.File]::ReadAllBytes($conversion))+':'+[Convert]::ToBase64String([IO.File]::ReadAllBytes($conversion+'.backup')))
            $rows.Add((@(('VISIOCLIENT','tenant:VISIOCLIENT','unknown') | Convert-Office365License -ReturnArray) -join '|'))
            $rows.Add((@(Convert-Office365License -License 'VISIOCLIENT','unknown' -Separator ';') -join '|'))
            $rows.Add((@(Convert-Office365License -License 'Visio Plan 2','unknown' -ToSku -ReturnArray) -join '|'))
            $rows | ConvertTo-Json -Compress
            """;
        probe = probe.Replace("__PROOF_ROOT__", EscapeStatementErrorPath(data), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("True:left,right", generated);
        Assert.Contains("not reached|finally", generated);
        Assert.Contains("Converted:UTF8BOM", generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeTypeTests_RuntimeFreeDynamicOrUnresolvedTargetRemainsClosed(string framework)
    {
        foreach (var source in new[] { "function Test-Type { param([object]$Value,[object]$Target); return $Value -is $Target }", "function Test-Type { param([object]$Value); return $Value -is [PFC.MissingTypeProbe] }" })
        {
            using var fixture = ArtifactFixture.Create(source);
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
                PowerShellCompilationMode.Strict, targetFramework: framework, capabilities: PowerShellCompilationCapabilities.TypedExecutable));
            Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
        }
    }
}

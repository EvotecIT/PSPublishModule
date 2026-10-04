using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeValueArgument_PreservesInventoryAttemptAndCaughtConversion(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Invoke-ADComputerInventoryAttempt.ps1");
        Assert.Equal("198B6CE84D8EF6C26D6A2E9E5FB3DD7DB2B86CBF14AA9745C15A343AFF9467DC",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))));
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function ConvertTo-InterpolatedEncoding {
                [CmdletBinding()]param($Value,$Trace,[switch]$Alternate)
                $valueType=[int]
                try {
                    if($Alternate){$text="alternate:${Value}:$valueType"}else{$text="value:${Value}:$valueType"}
                    $alias=$text
                    $Trace.Add('encode')
                    [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($alias))
                }catch{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
            }
            function ConvertTo-OfflineByteEncoding {
                [CmdletBinding()]param($Value,$Trace,[switch]$Bad)
                $anchor=[int];$values=if($Bad){@($anchor,$Value)}else{@(1,$Value)}
                try{$Trace.Add('argument');[Convert]::ToBase64String($values)}
                catch{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
            }
            function Get-OfflineFileStamp {
                [CmdletBinding()]param($Value,$Trace,[switch]$Handle)
                $anchor=[string]
                try{
                    $argument=$($Trace.Add('argument');if($Handle){$Value}else{Join-Path $Value "$anchor"})
                    $stamp=[IO.File]::GetLastWriteTimeUtc($argument)
                    [pscustomobject]@{stamp=$stamp.ToString('o')}
                }catch{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally{$Trace.Add('finally')}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeValueArgument", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(4,built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries,entry=>{
            Assert.True(entry.EmittedClrMethod);Assert.True(entry.UsesNativeFunctionBinding);Assert.False(entry.RetainedHostedSource);
        });
        var probe = """
            $ErrorActionPreference='Stop'
            Add-Type -TypeDefinition 'public sealed class OfflineStringValue { public System.Collections.Generic.List<string> Trace; public bool Fail; public override string ToString() { Trace.Add("stringify"); if (Fail) throw new System.InvalidOperationException("offline-string-failure"); return "converted"; } }'
            foreach($alternate in $false,$true){foreach($case in 'type','object','throw'){
                $trace=[Collections.Generic.List[string]]::new()
                $value=if($case -eq 'type'){[string]}else{$v=[OfflineStringValue]::new();$v.Trace=$trace;$v.Fail=$case -eq 'throw';$v}
                $records=@(ConvertTo-InterpolatedEncoding -Value $value -Trace $trace -Alternate:$alternate)
                [pscustomobject]@{case=$case;alternate=$alternate;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }}
            foreach($case in 'byte','numeric-string','type','object','throw','authored-type'){
                $trace=[Collections.Generic.List[string]]::new()
                $value=if($case -eq 'byte' -or $case -eq 'authored-type'){[byte]2}elseif($case -eq 'numeric-string'){'2'}elseif($case -eq 'type'){[string]}else{$v=[OfflineStringValue]::new();$v.Trace=$trace;$v.Fail=$case -eq 'throw';$v}
                $records=@(ConvertTo-OfflineByteEncoding -Value $value -Trace $trace -Bad:($case -eq 'authored-type'))
                [pscustomobject]@{case=('bytes-'+$case);records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }
            $file='OWNED_FILE';[IO.File]::WriteAllText($file,'owned');[IO.File]::SetLastWriteTimeUtc($file,[datetime]'2020-01-02T03:04:05Z')
            $stream=[IO.File]::Open($file,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
            try{foreach($handle in $false,$true){
                $trace=[Collections.Generic.List[string]]::new();$value=if($handle){$stream.SafeFileHandle}else{[IO.Path]::GetDirectoryName($file)}
                $records=@(Get-OfflineFileStamp -Value $value -Trace $trace -Handle:$handle)
                [pscustomobject]@{case='file-time';handle=$handle;records=$records;trace=@($trace.ToArray());closed=$stream.SafeFileHandle.IsClosed}|ConvertTo-Json -Depth 6 -Compress
            }}finally{$stream.Dispose()}
            function global:Export-Clixml {
                [CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject,[string]$LiteralPath,[int]$Depth)
                process{$global:InventoryConfiguration=$InputObject;$global:InventoryDirectory=[IO.Path]::GetDirectoryName($LiteralPath);$global:InventoryTrace.Add('export:'+ $WhatIfPreference)}
            }
            function global:Invoke-ADComputerInventoryChildProcess {throw 'Must never execute the authored inventory child'}
            function global:Start-Process {
                [CmdletBinding()]param($FilePath,$ArgumentList,$WindowStyle,$RedirectStandardOutput,$RedirectStandardError,[switch]$PassThru)
                $global:InventoryTrace.Add('start:'+ $WindowStyle)
                $command=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($ArgumentList[-1]))
                if($command -notmatch 'Invoke-ADComputerInventoryChildProcess'){throw 'Unexpected command encoding'}
                if($global:InventoryCase -eq 'start-failure'){throw 'offline-start-failure'}
                $config=$global:InventoryConfiguration
                if($global:InventoryCase -like 'success*'){[IO.File]::WriteAllText($config.SuccessPath,'done')}
                if($global:InventoryCase -eq 'success-data'){[IO.File]::WriteAllText($config.DataPath,"Name`nowned-one`nowned-two")}
                if($global:InventoryCase -eq 'error-file'){[IO.File]::WriteAllText($config.ErrorPath,'offline-error-file')}
                if($global:InventoryCase -eq 'stderr'){[IO.File]::WriteAllText($RedirectStandardError,'offline-stderr')}
                $p=[pscustomobject]@{HasExited=($global:InventoryCase -notlike 'timeout-*');ExitCode=17;Id=29;WaitCount=0}
                $p|Add-Member ScriptMethod WaitForExit {
                    param($milliseconds)
                    $global:InventoryTrace.Add('wait:'+ [string]$milliseconds)
                    if($this.HasExited){return $true}
                    $this.WaitCount++
                    if($global:InventoryCase -eq 'timeout-query'){[Threading.Thread]::Sleep(1350)}else{[Threading.Thread]::Sleep(1050)}
                    if($this.WaitCount -eq 1 -and $global:InventoryCase -ne 'timeout-initialization'){[IO.File]::WriteAllText($global:InventoryConfiguration.InitializationPath,'ready')}
                    if($this.WaitCount -eq 2 -and $global:InventoryCase -eq 'timeout-query'){
                        [IO.File]::WriteAllText($global:InventoryConfiguration.ReadyPath,'ready')
                        [IO.File]::WriteAllText($global:InventoryConfiguration.ProgressPath,'older-progress')
                        [IO.File]::SetLastWriteTimeUtc($global:InventoryConfiguration.ProgressPath,[datetime]'2020-01-02T03:04:05Z')
                    }
                    $false
                }
                $p|Add-Member ScriptMethod Kill {$global:InventoryTrace.Add('kill');$this.HasExited=$true}
                $p|Add-Member ScriptMethod Dispose {$global:InventoryTrace.Add('dispose')}
                $p
            }
            function global:ConvertFrom-ADComputerInventoryRow {[CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject) process{$InputObject}}
            function global:ConvertTo-PreparedComputer {
                [CmdletBinding()]param([Parameter(ValueFromPipeline)]$InputObject,$AzureInformationCache,$JamfInformationCache,[switch]$IncludeAzureAD,[switch]$IncludeIntune,[switch]$IncludeJamf,$Today)
                process{[pscustomobject]@{name=$InputObject.Name;azure=$IncludeAzureAD.IsPresent;intune=$IncludeIntune.IsPresent;jamf=$IncludeJamf.IsPresent}}
            }
            foreach($case in 'success-empty','success-data','error-file','stderr','missing-marker','start-failure','timeout-initialization','timeout-connection','timeout-query'){
                $global:InventoryCase=$case;$global:InventoryConfiguration=$null;$global:InventoryDirectory=$null
                $global:InventoryTrace=[Collections.Generic.List[string]]::new()
                $childPath=if($case -eq 'success-empty'){''}else{'owned-unused.ps1'}
                $result=Invoke-ADComputerInventoryAttempt -Server offline.invalid -QueryParameters @{Filter='offline';Properties=@('Name')} -IncludeAzureAD -IncludeJamf -ChildProcessFunctionPath $childPath -InitializationTimeoutSeconds 1 -ConnectionTimeoutSeconds 1 -IdleTimeoutSeconds 1
                [pscustomobject]@{case=$case;success=$result.Succeeded;computers=@($result.Computers);timedOut=$result.TimedOut;phase=$result.TimeoutPhase;error=$result.ErrorMessage;durationType=$result.Duration.GetType().FullName;directoryExists=(Test-Path -LiteralPath $global:InventoryDirectory);trace=@($global:InventoryTrace.ToArray())}|ConvertTo-Json -Depth 6 -Compress
            }
            """.Replace("OWNED_FILE",EscapeStatementErrorPath(Path.Combine(fixture.RootPath,"owned-stamp.txt")),StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath,probe,host);
        Assert.Equal(original,RunModuleProof(built.ArtifactPath!,probe,host));
        Assert.Equal(23,original.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Count(line=>line.StartsWith('{')));
        Assert.Contains("InvalidCastFromAnyTypeToString",original);
        Assert.Contains("stringify",original);
        Assert.Contains("\"directoryExists\":false",original);
        Assert.Contains("owned-two",original);
        Assert.Contains("offline-start-failure",original);
        Assert.Contains("\"phase\":\"Initialization\"",original);
        Assert.Contains("\"phase\":\"Connection\"",original);
        Assert.Contains("\"phase\":\"Query\"",original);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeValueArgument_KeepsMixedParameterScopedAndTransformedTypeOriginsGuarded(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Read-Closed {[CmdletBinding()]param([switch]$Flag) $type=[int];try{if($Flag){$text="one:$type"}else{$text="two:$type"};[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($text))}catch{$_.FullyQualifiedErrorId}}
            function Read-Mixed {[CmdletBinding()]param([switch]$Flag) $type=[int];try{if($Flag){$text=$type}else{$text="$type"};[object]::ReferenceEquals($text,$null)}catch{$_.FullyQualifiedErrorId}}
            function Read-Parameter {[CmdletBinding()]param($text,[switch]$Flag) $type=[int];try{if($Flag){$text="$type"};[object]::ReferenceEquals($text,$null)}catch{$_.FullyQualifiedErrorId}}
            function Read-Scoped {[CmdletBinding()]param() $type=[int];try{$script:text="$type";[object]::ReferenceEquals($script:text,$null)}catch{$_.FullyQualifiedErrorId}}
            function Read-Transformed {[CmdletBinding()]param() $type=[int];try{$text="$type";$transformed=$text.GetType();[Activator]::CreateInstance($transformed)}catch{$_.FullyQualifiedErrorId}}
            function Read-StatementArgument {[CmdletBinding()]param([switch]$Bad) $type=[int];try{[Convert]::ToBase64String($(if($Bad){@($type)}else{@(1,2)}))}catch{$_.InvocationInfo.OffsetInLine}}
            function Read-TypedCatch {[CmdletBinding()]param() $type=[int];$text="$type";try{[Convert]::ToBase64String(@($type))}catch [InvalidCastException]{$_.FullyQualifiedErrorId}}
            function Read-EncodingVariable {[CmdletBinding()]param() $type=[int];$text="$type";$encoder=[Text.Encoding]::Unicode;try{$encoder.GetBytes($text)}catch{$_.FullyQualifiedErrorId}}
            function Read-Iterator {[CmdletBinding()]param() $type=[int];$text="$type";foreach($text in @($type)){try{[object]::ReferenceEquals($text,$null)}catch{$_.FullyQualifiedErrorId}}}
            function Read-LocalAlias {[CmdletBinding()]param() $type=[int];$text="$type";$local:text=$type;try{[object]::ReferenceEquals($text,$null)}catch{$_.FullyQualifiedErrorId}}
            function Read-RefCell {[CmdletBinding()]param() $type=[int];$text="$type";$cell=[ref]$text;$cell.Value=$type;try{[object]::ReferenceEquals($text,$null)}catch{$_.FullyQualifiedErrorId}}
            function Read-LaterWrite {[CmdletBinding()]param() $type=[int];$text="$type";foreach($item in 1,2){try{[Activator]::CreateInstance($text)}catch{$_.FullyQualifiedErrorId};$text=[int]}}
            """,Path.Combine(Path.GetTempPath(),"interpolation-origin-boundaries.ps1"));
        var hybrid=new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(new[]{"Read_Closed","Read_StatementArgument"},hybrid.Emitted.Methods.Select(method=>method.GeneratedName).OrderBy(name=>name).ToArray());
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[]{source},framework,PowerShellCompilationCapabilities.TypedLibrary).Emitted.Methods);
    }
}

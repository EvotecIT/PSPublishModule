using PowerForge;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void LiteralPipelineValue_PreservesOfflineWhoIsAndConstructionEffects(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "WhoIs", "Get-WhoIs.ps1");
        Assert.Equal("5eaed156f790294c4c144f72cf00034d4cabf7ef3a2845f919c741ffcddbfe4f",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Get-PipelineLiteral {
                [CmdletBinding()]param($Items,[string]$Kind,[string]$Mode,$Trace)
                $seen='prior';$prior=$PSItem
                try {
                    if($Kind -eq 'Object') {
                        $value=[pscustomobject]@{
                            Before=$Trace.Add('before')
                            Result=$Items | ForEach-Object {$seen='changed';$Trace.Add('item');$_;if($Mode -eq 'Fail'){throw 'pipeline failed'}}
                            After=$Trace.Add('after')
                        }
                    } else {
                        $value=[ordered]@{
                            Before=$Trace.Add('before')
                            Result=$Items | ForEach-Object {$seen='changed';$Trace.Add('item');$_;if($Mode -eq 'Fail'){throw 'pipeline failed'}}
                            After=$Trace.Add('after')
                        }
                    }
                    [pscustomobject]@{result=$value.Result;isNull=$null -eq $value.Result;resultType=$(if($null -ne $value.Result){$value.Result.GetType().FullName});valueType=$value.GetType().FullName;seen=$seen;restored=$prior -eq $PSItem}
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;seen=$seen}
                } finally {$Trace.Add('finally')}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LiteralPipelineValues",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        Assert.Equal(2, built.Manifest!.CompiledMethods);
        Assert.All(built.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name != "<script>"), unit =>
        {
            Assert.True(unit.EmittedClrMethod, unit.Name + ": " + string.Join(" | ", unit.DiagnosticChain.Select(cause => cause.Message)));
            Assert.True(unit.UsesNativeFunctionBinding, unit.Name);
            Assert.False(unit.RetainedHostedSource, unit.Name);
        });
        const string probe = """
            Import-Module Microsoft.PowerShell.Utility
            Import-Module Microsoft.PowerShell.Management
            $PSModuleAutoLoadingPreference='None'
            $global:requests=[Collections.Generic.List[object]]::new()
            function global:Get-Date {[datetime]'2026-01-02T03:04:05'}
            function global:Invoke-RestMethod {
                [CmdletBinding()]param([string]$Uri,$Headers)
                $global:requests.Add([pscustomobject]@{uri=$Uri;accept=$Headers.Accept})
                if($global:whoisMode -eq 'Fail'){throw 'offline provider failure'}
                if($Uri -eq 'offline:organization'){return [pscustomobject]@{org=[pscustomobject]@{city='Example City'}}}
                if($global:whoisMode -eq 'NoNet'){return [pscustomobject]@{net=$null}}
                $blocks=@(for($i=0;$i -lt $global:blockCount;$i++){[pscustomobject]@{StartAddress="192.0.2.$i";cidrLength=24}})
                $organization=if($global:whoisMode -eq 'NoOrg'){$null}else{[pscustomobject]@{name='Example Org';'#text'='offline:organization'}}
                [pscustomobject]@{net=[pscustomobject]@{name='Example Network';orgRef=$organization;StartAddress='192.0.2.0';endAddress='192.0.2.255';netBlocks=[pscustomobject]@{netBlock=$blocks};updateDate='2026-01-02'}}
            }
            foreach($mode in 'Normal','NoOrg','NoNet','Fail') {
                foreach($count in 0,1,3) {
                    $global:whoisMode=$mode;$global:blockCount=$count;$global:requests.Clear()
                    $records=@(Get-WhoIs -IPAddress '192.0.2.1' -ErrorAction Stop)
                    [pscustomobject]@{kind='whois';mode=$mode;count=$count;records=$records;types=@($records|ForEach-Object{$_.PSTypeNames[0]});requests=@($global:requests.ToArray())}|ConvertTo-Json -Depth 12 -Compress
                }
            }
            foreach($kind in 'Object','Map') {
                foreach($mode in 'Normal','Fail') {
                    foreach($items in @(@{v=@()},@{v=$null},@{v=7},@{v=@(7,9)})) {
                        $trace=[Collections.Generic.List[string]]::new()
                        $records=@(Get-PipelineLiteral -Items $items.v -Kind $kind -Mode $mode -Trace $trace -ErrorAction Stop)
                        [pscustomobject]@{kind=$kind;mode=$mode;input=$items.v;records=$records;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 10 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "literal-pipeline-original");
        var generated = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(built.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "literal-pipeline-generated");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(generated.ExitCode == 0, generated.StandardOutput + generated.StandardError);
        var expected = original.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var actual = generated.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(28, expected.Length);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected[index]), JsonNode.Parse(actual[index])),
                "Original: " + expected[index] + Environment.NewLine + "Generated: " + actual[index]);
        Assert.Equal(original.StandardError, generated.StandardError);
        Assert.Contains("offline provider failure", generated.StandardError);
    }

    [Theory]
    [InlineData("$Items | ForEach-Object {$_}", true)]
    [InlineData("$Items | ForEach-Object {$_} 2>$null", false)]
    [InlineData("$Items | ForEach-Object {$_} &", false)]
    [InlineData("$Items | Out-Null", false)]
    [InlineData("[enum]::GetValues([DayOfWeek]) | ForEach-Object {$_}", false)]
    public void LiteralPipelineValue_RetainsExcludedPipelineOwners(string value, bool emits)
    {
        var document = PowerShellSourceParser.Parse("function Get-Literal {[CmdletBinding()]param($Items);[pscustomobject]@{Result=" + value + "}}",
            Path.Combine(Path.GetTempPath(), "literal-pipeline-owner.ps1"));
        var pipeline = new PowerShellSemanticCompilationPipeline();
        var hybrid = pipeline.Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.HybridModule);
        Assert.True(emits == hybrid.Emitted.Methods.Any(method => method.GeneratedName == "Get_Literal" && method.NativeFunctionBinding is not null),
            string.Join(Environment.NewLine, hybrid.Emitted.Methods.Select(method => method.Source)));
        Assert.Empty(pipeline.Compile(new[] { document }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable).Emitted.Methods);
    }
}

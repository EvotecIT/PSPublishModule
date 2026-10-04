using PowerForge;
using System.Management.Automation.Language;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ParserReferences_PreserveWritebackAndUnchangedSafeHashtableValues(string framework, string host)
    {
        var path=FindCompleteConversionWorkflow("PSScriptTools","Parser","hashtableTools.ps1");
        var ast=Parser.ParseFile(path,out _,out _);
        var authored=(FunctionDefinitionAst)ast.Find(node=>node is FunctionDefinitionAst function && function.Name=="Convert-HashtableString",true)!;
        using var fixture=ArtifactFixture.Create(authored.Extent.Text+"""

            function Test-ParserRefs {
                [CmdletBinding()]param($Text,$Trace)
                $tokens=$null;$errors=$null
                $null=[System.Management.Automation.Language.Parser]::ParseInput('0',[ref]$tokens,[ref]$errors)
                try {$ast=[System.Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$errors);$ast.GetType().FullName}
                catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally {$Trace.Add('finally')}
                [pscustomobject]@{tokenType=$tokens.GetType().FullName;tokens=@($tokens|ForEach-Object {$_.Kind.ToString()});errorType=$errors.GetType().FullName;errors=@($errors|ForEach-Object {$_.ErrorId})}
            }
            function Test-TypedParserRefs {
                [CmdletBinding()]param($Text,$Trace)
                [int]$tokens=17;$errors='error-prior'
                try {$null=[System.Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$errors)}
                catch {[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                finally {$Trace.Add('finally')}
                [pscustomobject]@{tokenType=$tokens.GetType().FullName;tokens=$tokens;errorType=$errors.GetType().FullName;errors=@($errors|ForEach-Object {$_.ErrorId})}
            }
            """, ".psm1");
        var built=new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.ParserReferences",PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        Assert.True(built.Manifest!.CompiledMethods==3,string.Join(Environment.NewLine,
            built.Manifest.UnitDispositionLedger!.Entries.SelectMany(unit=>unit.DiagnosticChain.Select(cause=>unit.Name+": "+cause.Message))));
        const string probe="""
            Add-Type @'
            public sealed class ParserReferenceInput {
                public static readonly System.Collections.Generic.List<string> Trace = new System.Collections.Generic.List<string>();
                public bool Fail;
                public override string ToString() {
                    Trace.Add("convert");
                    if (Fail) throw new System.InvalidOperationException("parser input conversion failed");
                    return "@{Converted=1}";
                }
            }
            '@
            $global:UnsafeHashtableCalls=0
            function global:Invoke-OfflineDanger {$global:UnsafeHashtableCalls++;throw 'must never execute'}
            $samples=@('@{Name="value";Count=7}','@{Nested=@{X=1};Items=@(1,2,3)}','[ordered]@{B=2;A=1}','@{}','42','@{Name=','@{Name=(Invoke-OfflineDanger)}')
            foreach($sample in $samples) {
                foreach($pipeline in $false,$true) {
                    $records=[Collections.Generic.List[object]]::new();$failure=$null
                    try {
                        if($pipeline){@($sample,$sample)|Convert-HashtableString -ErrorAction Stop|ForEach-Object {$records.Add($_)}}
                        else {Convert-HashtableString -Text $sample -ErrorAction Stop|ForEach-Object {$records.Add($_)}}
                    } catch {$failure=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine}}
                    [pscustomobject]@{sample=$sample;pipeline=$pipeline;values=@($records.ToArray());failure=$failure;unsafeCalls=$global:UnsafeHashtableCalls}|ConvertTo-Json -Depth 10 -Compress
                }
            }
            foreach($function in 'Test-ParserRefs','Test-TypedParserRefs') {
                foreach($sample in '42','@{X=1}','@{X=') {
                    $trace=[Collections.Generic.List[string]]::new()
                    $result=@(& $function -Text $sample -Trace $trace -ErrorAction Stop)
                    [pscustomobject]@{function=$function;sample=$sample;result=$result;trace=@($trace.ToArray())}|ConvertTo-Json -Depth 10 -Compress
                }
            }
            foreach($function in 'Test-ParserRefs','Test-TypedParserRefs') {
                foreach($fail in $false,$true) {
                    [ParserReferenceInput]::Trace.Clear()
                    $sample=[ParserReferenceInput]::new();$sample.Fail=$fail
                    $trace=[Collections.Generic.List[string]]::new()
                    $result=@(& $function -Text $sample -Trace $trace -ErrorAction Stop)
                    [pscustomobject]@{function=$function;conversionFails=$fail;result=$result;trace=@($trace.ToArray());conversionTrace=@([ParserReferenceInput]::Trace.ToArray())}|ConvertTo-Json -Depth 10 -Compress
                }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host).Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host).Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(24,original.Length);Assert.Equal(original.Length,generated.Length);
        for(var index=0;index<original.Length;index++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[index]),JsonNode.Parse(generated[index])),"Original: "+original[index]+Environment.NewLine+"Generated: "+generated[index]);
        Assert.All(generated.Take(14),line=>Assert.Contains("\"unsafeCalls\":0",line));
        var successfulConversion = JsonNode.Parse(generated[20])!;
        var failedConversion = JsonNode.Parse(generated[21])!;
        Assert.NotEmpty(successfulConversion["conversionTrace"]!.AsArray());
        Assert.Equal("System.Management.Automation.Language.ScriptBlockAst", successfulConversion["result"]![0]!.GetValue<string>());
        Assert.NotEmpty(failedConversion["conversionTrace"]!.AsArray());
        Assert.False(string.IsNullOrEmpty(failedConversion["result"]![0]!["id"]!.GetValue<string>()));
        Assert.Equal("Number", failedConversion["result"]![1]!["tokens"]![0]!.GetValue<string>());
        Assert.Empty(failedConversion["result"]![1]!["errors"]!.AsArray());
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void ParserReferences_AliasedScopedAndNonTrailingSlotsRemainHosted(string framework)
    {
        const string source="""
            function Test-Alias {param([string]$Text);$tokens=$null;[Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$local:tokens)}
            function Test-Scope {param([string]$Text);$tokens=$null;$script:errors=$null;[Management.Automation.Language.Parser]::ParseInput($Text,[ref]$tokens,[ref]$script:errors)}
            function Test-Early {param([string]$Text);$tokens=$null;$errors=$null;[Management.Automation.Language.Parser]::ParseInput([ref]$tokens,$Text,[ref]$errors)}
            """;
        using var fixture=ArtifactFixture.Create(source);
        var plan=new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fixture.ScriptPath,
            PowerShellCompilationMode.Hybrid,targetFramework:framework,capabilities:PowerShellCompilationCapabilities.HybridModule));
        Assert.All(plan.Files.SelectMany(file=>file.Units),unit=>Assert.False(unit.IsCompilable));
    }
}

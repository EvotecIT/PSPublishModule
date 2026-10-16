using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeReferenceArguments_PreserveFactoryCastCatchRecordsRethrowAndFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-ClosedDocument {
                [CmdletBinding()] param([string]$Content,[switch]$Rethrow)
                $settings=[System.Xml.XmlReaderSettings]::new()
                $settings.DtdProcessing=[System.Xml.DtdProcessing]::Prohibit
                $settings.XmlResolver=$null
                $stringReader=[IO.StringReader]::new($Content)
                $reader=$null
                try {
                    $reader=[System.Xml.XmlReader]::Create($stringReader,$settings)
                    $document=[System.Xml.XmlDocument]::new()
                    $document.XmlResolver=$null
                    $document.Load($reader)
                    'xml:'+ $document.DocumentElement.Name
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;category=[string]$_.CategoryInfo.Category;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;same=[object]::ReferenceEquals($_,$PSItem)}
                    if($Rethrow){throw}
                } finally {
                    if($null -ne $reader){$reader.Dispose()}
                    $stringReader.Dispose()
                    'finally:xml'
                }
            }
            function Join-ClosedReference {
                [CmdletBinding()] param([string]$Base,[string]$Content,[switch]$Rethrow)
                $match=[regex]::Match($Content,'href=["'']([^"'']+)["'']',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
                $href=[string]$match.Groups[1].Value
                try { ([Uri]::new([Uri]$Base,$href)).AbsoluteUri }
                catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;category=[string]$_.CategoryInfo.Category;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;same=[object]::ReferenceEquals($_,$PSItem)}
                    if($Rethrow){throw}
                } finally { 'finally:uri' }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,fixture.OutputPath,"Generated.NativeReferenceArguments",
            PowerShellCompilationArtifactKind.BinaryModule,PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution:true){TargetFramework=framework});
        Assert.True(built.Succeeded,built.Error+Environment.NewLine+built.BuildOutput);
        foreach(var name in new[]{"Read-ClosedDocument","Join-ClosedReference"})
        {
            var unit=Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,unit=>unit.Name==name);
            Assert.True(unit.EmittedClrMethod,System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        const string probe="""
            foreach($rethrow in $false,$true){
                foreach($content in '<root />','<broken>','<!DOCTYPE root [<!ENTITY x "blocked">]><root>&x;</root>'){
                    $Error.Clear();$outer=$null;$records=@()
                    try{$records=@(Read-ClosedDocument -Content $content -Rethrow:$rethrow)}catch{$outer=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
                    [pscustomobject]@{kind='xml';input=$content;rethrow=$rethrow;records=$records;outer=$outer;errors=@($Error|ForEach-Object{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}})}|ConvertTo-Json -Compress -Depth 8
                }
                foreach($case in @([ordered]@{base='https://example.invalid/root/';content='<link href="child">'},[ordered]@{base='http://[bad';content='<link href="child">'},[ordered]@{base='https://example.invalid/';content='<link href="http://[bad">'})){
                    $Error.Clear();$outer=$null;$records=@()
                    try{$records=@(Join-ClosedReference -Base $case.base -Content $case.content -Rethrow:$rethrow)}catch{$outer=[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}}
                    [pscustomobject]@{kind='uri';input=$case;rethrow=$rethrow;records=$records;outer=$outer;errors=@($Error|ForEach-Object{[pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message}})}|ConvertTo-Json -Compress -Depth 8
                }
            }
            """;
        var original=RunModuleProof(fixture.ScriptPath,probe,host);
        var generated=RunModuleProof(built.ArtifactPath!,probe,host);
        Assert.Equal(original,generated);
        Assert.Equal(12,generated.Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("finally:xml",generated);
        Assert.Contains("finally:uri",generated);
        Assert.Contains("\"same\":true",generated);
        Assert.Contains("MethodInvocationException",generated);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void NativeReferenceArguments_KeepTypedBranchReferenceAndUnknownOriginsHosted(string framework)
    {
        var document=PowerShellSourceParser.Parse("""
            function Read-Typed {[CmdletBinding()]param([string]$Content) $reader=[Xml.XmlReader]::Create([IO.StringReader]::new($Content));$document=[Xml.XmlDocument]::new();try{$document.Load($reader)}catch [Xml.XmlException]{$_.FullyQualifiedErrorId}}
            function Read-Branch {[CmdletBinding()]param([switch]$Flag) $value=[Uri]::new('https://example.invalid/');if($Flag){$value=[Uri]};try{[Uri]::new($value,'child')}catch{$_.FullyQualifiedErrorId}}
            function Read-Reference {[CmdletBinding()]param() $type=[Uri];$value="$type";$cell=[ref]$value;$cell.Value=$type;try{[Uri]::new([Uri]'https://example.invalid/',$value)}catch{$_.FullyQualifiedErrorId}}
            function Read-Unknown {[CmdletBinding()]param([string]$Base) $value=[Activator]::CreateInstance([object]);try{[Uri]::new([Uri]$Base,$value)}catch{$_.FullyQualifiedErrorId}}
            function Read-Transformed {[CmdletBinding()]param([string]$Content) $value=[Xml.XmlReader]::Create([IO.StringReader]::new($Content)).GetType();try{[Uri]::new([Uri]'https://example.invalid/',$value)}catch{$_.FullyQualifiedErrorId}}
            """,Path.Combine(Path.GetTempPath(),"closed-reference-catch-negatives.ps1"));
        var compiled=new PowerShellSemanticCompilationPipeline().Compile(new[]{document},framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Empty(compiled.Emitted.Methods);
        Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[]{document},framework,
            PowerShellCompilationCapabilities.TypedLibrary).Emitted.Methods);
    }
}

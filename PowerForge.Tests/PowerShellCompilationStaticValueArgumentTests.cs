using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StaticValueArgument_PreservesEncodingFilesCultureAndErrors(string framework, string host)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "Convert-BinaryToString.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source) + """

            function Read-StaticValues {
                [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1,[string]$Path,[string]$Text,[object]$Number)
                $marker='before'
                [int[]]$limits=@([int]::MaxValue); [string[]]$empty=@([string]::Empty)
                try {
                    [IO.File]::WriteAllText($Path,$Text,[Text.Encoding]::UTF8)
                    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Text))
                    $formatted=[string]::Format([Globalization.CultureInfo]::InvariantCulture,'{0:N2}',[object[]]@($Number))
                    [pscustomobject]@{encoded=$encoded;formatted=$formatted;thread=[object]::ReferenceEquals([Threading.Thread]::CurrentThread,[Threading.Thread]::CurrentThread);marker=$marker;limits=$limits;empty=$empty}
                } catch {
                    [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;line=$_.InvocationInfo.ScriptLineNumber;column=$_.InvocationInfo.OffsetInLine;marker=$marker;limits=$limits;empty=$empty}
                } finally { 'finally' }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StaticValueArguments",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == "Read-StaticValues");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.True(unit.UsesNativeFunctionBinding);
        Assert.False(unit.RetainedHostedSource);
        Assert.True(Assert.Single(built.Manifest.UnitDispositionLedger.Entries, entry => entry.Name == "Convert-BinaryToString").EmittedClrMethod);
        var path = EscapeStatementErrorPath(Path.Combine(fixture.RootPath, "owned-text.txt"));
        var probe = """
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=$culture
                foreach($text in $null,'','plain','żółć',([string][char]0xd800)) {
                    $path='OWNED_PATH'
                    $records=@(Read-StaticValues -Path $path -Text $text -Number ([double]1234.5))
                    [pscustomobject]@{kind='file';culture=$culture;text=$text;records=$records;bytes=[Convert]::ToBase64String([IO.File]::ReadAllBytes($path))}|ConvertTo-Json -Compress -Depth 8
                }
                foreach($bad in 'missing-parent','empty-path') {
                    $path=if($bad -eq 'empty-path'){''}else{'OWNED_PATH/missing/file.txt'}
                    [pscustomobject]@{kind='failure';culture=$culture;mode=$bad;records=@(Read-StaticValues -Path $path -Text 'text' -Number 12)}|ConvertTo-Json -Compress -Depth 8
                }
            }
            foreach($bytes in $null,([byte[]]@()),([byte[]]@(65,0)),([byte[]]@(65)),([Text.Encoding]::Unicode.GetBytes('żółć'))) {
                [pscustomobject]@{kind='binary';input=$bytes;values=@(Convert-BinaryToString -Binary $bytes)}|ConvertTo-Json -Compress -Depth 8
            }
            """.Replace("OWNED_PATH", path, StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(19, original.Length);
        Assert.Equal(original.Length, generated.Length);
        for (var i = 0; i < original.Length; i++)
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original[i]), JsonNode.Parse(generated[i])), "Original: " + original[i] + Environment.NewLine + "Generated: " + generated[i]);
        Assert.True(generated[2].Contains("1,234.50", StringComparison.Ordinal), generated[2]);
        Assert.Contains("finally", generated[10]);
    }

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void StaticValueArgument_KeepsTypeProducingAndUnknownTransformsHosted(string framework)
    {
        var source = PowerShellSourceParser.Parse("""
            function Read-Value { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1) try { [object]::ReferenceEquals([Text.Encoding]::UTF8,[Text.Encoding]::UTF8) } catch { $_.FullyQualifiedErrorId } }
            function Read-TypeTransform { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1) try { [object]::ReferenceEquals([Text.Encoding]::UTF8.GetType(),$null) } catch { $_.FullyQualifiedErrorId } }
            function Read-ScalarGetter { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1) try { [Math]::Abs([Console]::WindowWidth) } catch { $_.FullyQualifiedErrorId } }
            function Read-TypeArray { [CmdletBinding()]param([ValidateRange(1,9)][int]$Seed=1) try { [object]::ReferenceEquals([Type]::EmptyTypes,$null) } catch { $_.FullyQualifiedErrorId } }
            """, Path.Combine(Path.GetTempPath(), "static-value-arguments.ps1"));
        var hybrid = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(hybrid.Emitted.Methods, method => method.GeneratedName == "Read_Value" && method.NativeFunctionBinding is not null);
        Assert.DoesNotContain(hybrid.Emitted.Methods, method => method.GeneratedName is "Read_TypeTransform" or "Read_TypeArray" or "Read_ScalarGetter");
        var strict = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);
    }
}

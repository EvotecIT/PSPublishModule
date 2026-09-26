using System.Management.Automation.Language;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("[guid]::NewGuid().ToString('N')", false)]
    [InlineData("[IO.Path]::GetRandomFileName()", false)]
    [InlineData("[System.Math]::Abs(-1)", false)]
    [InlineData("[object]::ReferenceEquals([int],[string])", true)]
    [InlineData("[Activator]::CreateInstance([Text.StringBuilder])", true)]
    [InlineData("[Type]::GetType('System.Int32')", true)]
    [InlineData("[guid]::NewGuid().GetType()", true)]
    [InlineData("([guid]::NewGuid()).GetType()", true)]
    [InlineData("[type]([IO.Path]::GetFileName('System.IO.FileInfo'))", true)]
    [InlineData("([guid]::NewGuid())[0].GetType()", true)]
    [InlineData("@([guid]::NewGuid())[0].GetType()", true)]
    [InlineData("$([guid]::NewGuid()).GetType()", true)]
    [InlineData("'prefix'+[guid]::NewGuid().ToString('N')+'suffix'", false)]
    [InlineData("\"prefix $([guid]::NewGuid().GetType()) suffix\"", false)]
    [InlineData("[Text.StringBuilder]::new([int])", true)]
    [InlineData("[Text.StringBuilder]::new().Length", true)]
    [InlineData("[IO.FileAccess]::Read", false)]
    [InlineData("[IO.FileAccess]::Read.GetType()", true)]
    [InlineData("[int]", true)]
    public void NativeScalarReceiver_SeparatesReceiverSyntaxFromAuthoredTypeValues(string source, bool expected)
    {
        var ast = Parser.ParseInput(source, out _, out var errors);
        Assert.Empty(errors);
        Assert.Equal(expected, PowerShellNativeTypeArgumentPolicy.ContainsTypeValue(Assert.Single(ast.EndBlock!.Statements)));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeScalarReceiver_PreservesCaughtAliasesAndOfflineHtmlMerge(string framework, string host)
    {
        var merge = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Merge-ADComputerHTMLReportData.ps1");
        var find = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Find-JavaScriptArrayAssignment.ps1");
        using var fixture = ArtifactFixture.Create(File.ReadAllText(merge) + Environment.NewLine + File.ReadAllText(find) + Environment.NewLine + """
            function Read-ScalarAlias {
                [CmdletBinding()] param([string]$Value)
                $name=[System.IO.Path]::GetFileName($Value)
                $alias=$name
                $map=@{}
                try { $map.Add($alias,'one'); $map.Add($alias,'two') }
                catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message;value=$map[$alias]} }
                finally { 'finally' }
                'after'
            }
            function New-ParenthesizedType {
                [CmdletBinding()] param([string]$Value)
                try { ([Activator]::CreateInstance(([guid]::NewGuid()).GetType())).GetType().FullName }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            function New-CastComputedType {
                [CmdletBinding()] param([string]$Value)
                try { [Activator]::CreateInstance([type]([System.IO.Path]::GetFileName($Value))) }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            function New-IndexedType {
                [CmdletBinding()] param([string]$Value)
                try { ([Activator]::CreateInstance(([guid]::NewGuid())[0].GetType())).GetType().FullName }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            function New-CollectedType {
                [CmdletBinding()] param([string]$Value)
                try { ([Activator]::CreateInstance(@([guid]::NewGuid())[0].GetType())).GetType().FullName }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            function New-SubexpressionType {
                [CmdletBinding()] param([string]$Value)
                try { ([Activator]::CreateInstance($([guid]::NewGuid()).GetType())).GetType().FullName }
                catch { $_.FullyQualifiedErrorId; $_.Exception.GetType().FullName }
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeScalarReceiver", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Read-ScalarAlias", "Merge-ADComputerHTMLReportData", "Find-JavaScriptArrayAssignment" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        }
        foreach (var name in new[] { "New-ParenthesizedType", "New-CastComputedType", "New-IndexedType", "New-CollectedType", "New-SubexpressionType" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.False(unit.EmittedClrMethod);
            Assert.True(unit.RetainedHostedSource);
            Assert.Contains(unit.DiagnosticChain, cause => cause.Message.Contains("caught-error identity", StringComparison.Ordinal));
        }
        var probe = "$root='" + EscapeStatementErrorPath(fixture.RootPath) + "'; " + """
            $root=Join-Path $root 'offline-merge-proof'
            if(Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse }
            $null=New-Item -ItemType Directory -Path $root
            foreach($value in 'first.txt','second.txt','first.txt') { @(Read-ScalarAlias $value) | ConvertTo-Json -Depth 5 -Compress }
            New-ParenthesizedType -Value first
            New-CastComputedType -Value 'System.IO.FileInfo'
            New-IndexedType -Value first
            New-CollectedType -Value first
            New-SubexpressionType -Value first
            New-CastComputedType -Value 'System.IO.FileInfo'
            $html=Join-Path $root 'staging.html'; $data=Join-Path $root 'data.json'; $output=Join-Path $root 'report.html'
            $encoding=[Text.UTF8Encoding]::new($false)
            [IO.File]::WriteAllText($html,'<html><script>var inventory = ["old"]; var other = [];</script></html>',$encoding)
            foreach($content in '[]','[{"name":"one"}]','[{"name":"é😀"}]',('['+('1,'*40000)+'2]')) {
                [IO.File]::WriteAllText($data,$content,$encoding)
                Merge-ADComputerHTMLReportData -StagingHtmlPath $html -DataFilePath $data -OutputPath $output -DataStoreID inventory
                $expected='<html><script>var inventory = '+$content+'; var other = [];</script></html>'
                [pscustomobject]@{same=([IO.File]::ReadAllText($output) -ceq $expected);length=([IO.File]::ReadAllBytes($output).Length);temps=@(Get-ChildItem $root -Filter 'report.html.*.tmp').Count} | ConvertTo-Json -Compress
            }
            Remove-Item -LiteralPath $data
            $before=[IO.File]::ReadAllText($output)
            try { Merge-ADComputerHTMLReportData -StagingHtmlPath $html -DataFilePath $data -OutputPath $output -DataStoreID inventory -ErrorAction Stop }
            catch { [pscustomobject]@{id=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;inner=$_.Exception.InnerException.GetType().FullName;preserved=([IO.File]::ReadAllText($output) -ceq $before);temps=@(Get-ChildItem $root -Filter 'report.html.*.tmp').Count} | ConvertTo-Json -Compress }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"same\":true", generated);
        Assert.Contains("\"preserved\":true", generated);
    }
}

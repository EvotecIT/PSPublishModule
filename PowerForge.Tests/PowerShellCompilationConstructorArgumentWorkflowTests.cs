using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void ConstructorArguments_DoNotAdmitNestedTypeValuesOrRuntimeFreeReport(string framework)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-TypeCollection {
                param()
                $items=[Collections.Generic.List[object]]::new()
                try { $items.Add([Collections.Generic.List[object]]::new(@([IO.FileInfo]))) }
                catch { $_.FullyQualifiedErrorId }
            }
            """, ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "Generated.ConstructorTypeValues", "Methods", framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(result.Methods, method => method.SourceName == "Read-TypeCollection");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("caught-error identity", StringComparison.Ordinal));
        var report = FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Export-ADComputerReportData.ps1");
        var strict = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { report }, "Generated.RuntimeFreeReport", "Methods", framework,
            PowerShellCompilationCapabilities.TypedExecutable);
        Assert.DoesNotContain(strict.Methods, method => method.SourceName == "Export-ADComputerReportData");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ConstructorArguments_PreservePinnedOfflineReportAndFailureCleanup(string framework, string host)
    {
        var sources = new[]
        {
            FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Export-ADComputerReportData.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertTo-PrettyObject.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", "ConvertTo-InvariantJoinedString.ps1")
        };
        Assert.Equal("9e028c386f932ea512099c1f947a3b3df62c02bb7f597804f8fac6580119dbdf",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sources[0]))).ToLowerInvariant());
        Assert.Equal("d1d5aa6d1c394cdce1a3c261bbd884aa814df70772e320bccf010766d7643a77",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sources[1]))).ToLowerInvariant());
        Assert.Equal("cd4d417b24d8e69bb44e7688e0930ba0f90b866f80bc73d82dfe0f6765e9c364",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sources[2]))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)) + """

            function Read-ConstructedArgument {
                param([int]$Capacity)
                $items=[Collections.Generic.List[object]]::new()
                try { $items.Add([Text.StringBuilder]::new($Capacity)); $items[0].Capacity }
                catch [ArgumentException] { $_.Exception.GetType().FullName; $_.FullyQualifiedErrorId }
                catch { $_.Exception.GetType().FullName; $_.FullyQualifiedErrorId }
                finally { 'finally' }
            }
            function New-TaggedObject {
                [CmdletBinding()] param([object]$TypeName)
                [pscustomobject]@{Before='before';pstypename=$TypeName;After='after'}
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ConstructorArguments",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Export-ADComputerReportData", "Read-ConstructedArgument", "New-TaggedObject" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
        }
        const string probe = """
            $proofRoot='__PROOF_ROOT__'
            $rows=@(
                [pscustomobject]@{Name='one<&>';Enabled=$true;Number=1;Items=@('a','b');When=[datetime]'2020-01-02';TimeOnPendingList=9},
                [pscustomobject]@{Name="two`nline";Enabled=$false;Number=2;Items=@('c');When=[datetime]'2020-02-03';TimeOnPendingList=8},
                [pscustomobject]@{Name='zażółć 東京';Enabled=$true;Number=3;Items=@();When=[datetime]'2020-03-04';TimeOnPendingList=7})
            foreach($count in 0,1,3) {
                foreach($chunk in 1,2,4) {
                    $path=Join-Path $proofRoot 'report.json'
                    $inputRows=@($rows | Select-Object -First $count)
                    $result=Export-ADComputerReportData -Computers $inputRows -FilePath $path -ChunkSize $chunk -DateTimeFormat 'yyyy-MM-dd'
                    $bytes=[IO.File]::ReadAllBytes($path)
                    $json=[IO.File]::ReadAllText($path)
                    [pscustomobject]@{count=$count;chunk=$chunk;returnedCount=$result.Count;properties=$result.PropertyNames;
                        sample=$result.Sample;json=$json;bytes=[Convert]::ToBase64String($bytes)} | ConvertTo-Json -Depth 8 -Compress
                    [IO.File]::Delete($path)
                }
            }
            $missing=Join-Path $proofRoot 'missing/report.json'
            try { Export-ADComputerReportData -Computers $rows -FilePath $missing }
            catch { [pscustomobject]@{missingError=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;exists=(Test-Path $missing)} | ConvertTo-Json -Compress }
            & (Get-Command Export-ADComputerReportData).Module {
                param($path,$rows)
                function ConvertTo-PrettyObject { throw 'offline serializer failure' }
                try { Export-ADComputerReportData -Computers $rows -FilePath $path -ChunkSize 1 }
                catch { [pscustomobject]@{rollback=$_.Exception.Message;exists=(Test-Path $path)} | ConvertTo-Json -Compress }
            } (Join-Path $proofRoot 'rollback.json') $rows
            foreach($capacity in 0,4,-1,2) {
                [pscustomobject]@{capacity=$capacity;records=@(Read-ConstructedArgument -Capacity $capacity)} | ConvertTo-Json -Compress
            }
            foreach($name in @('Owned.Type','',$null,42,@('one','two'))) {
                try {
                    $tagged=New-TaggedObject -TypeName $name
                    [pscustomobject]@{name=$name;typeNames=@($tagged.PSObject.TypeNames);properties=@($tagged.PSObject.Properties.Name);
                        value=$tagged;type=$tagged.GetType().FullName} | ConvertTo-Json -Depth 6 -Compress
                } catch { [pscustomobject]@{name=$name;error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName} | ConvertTo-Json -Depth 5 -Compress }
            }
            """;
        var command = probe.Replace("__PROOF_ROOT__", EscapeStatementErrorPath(fixture.RootPath), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, command, host);
        var generated = RunModuleProof(built.ArtifactPath!, command, host);
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("offline serializer failure", generated);
        Assert.Contains("\"exists\":false", generated);
        Assert.Contains("\"returnedCount\":3", generated);
    }
}

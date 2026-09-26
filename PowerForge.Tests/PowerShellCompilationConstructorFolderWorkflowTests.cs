using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ConstructorArguments_PreservePinnedFolderInspectionOnNet10()
    {
        var source = FindCompleteConversionWorkflow("PSScriptTools", "FolderInspection", "Test-EmptyFolder.ps1");
        Assert.Equal("59a427193e26c82f3c550c7c3219886379489e714976bec251b874a70a777e3a",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ConstructorFolder",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = "net10.0" });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries).EmittedClrMethod);
        var unavailable = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { source }, "Generated.ConstructorFolderLegacy", "Methods", "net472",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.DoesNotContain(unavailable.Methods, method => method.SourceName == "Test-EmptyFolder");
        Assert.Contains(unavailable.Diagnostics, diagnostic => diagnostic.Message.Contains("System.IO.EnumerationOptions", StringComparison.Ordinal));
        var empty = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "empty")).FullName;
        var populated = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "populated")).FullName;
        File.WriteAllText(Path.Combine(populated, "one.txt"), "owned input");
        var nested = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "nested")).FullName;
        var child = Directory.CreateDirectory(Path.Combine(nested, "zażółć 東京")).FullName;
        File.WriteAllText(Path.Combine(child, "child.txt"), "owned child");
        const string probe = """
            $paths=@('__EMPTY__','__POPULATED__','__NESTED__')
            foreach($passThru in $false,$true) {
                $records=@(Test-EmptyFolder -Path $paths -PassThru:$passThru)
                [pscustomobject]@{binding='array';passThru=$passThru;records=$records;typeNames=@($records | ForEach-Object { $_.PSObject.TypeNames -join '|' })} | ConvertTo-Json -Depth 5 -Compress
                [pscustomobject]@{binding='pipeline';passThru=$passThru;records=@($paths | Test-EmptyFolder -PassThru:$passThru)} | ConvertTo-Json -Depth 5 -Compress
                [pscustomobject]@{binding='property';passThru=$passThru;records=@($paths | ForEach-Object { [pscustomobject]@{PSPath=$_} } | Test-EmptyFolder -PassThru:$passThru)} | ConvertTo-Json -Depth 5 -Compress
            }
            try { Test-EmptyFolder -Path '__MISSING__' -ErrorAction Stop }
            catch { [pscustomobject]@{error=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName;message=$_.Exception.Message} | ConvertTo-Json -Compress }
            """;
        var command = probe.Replace("__EMPTY__", EscapeStatementErrorPath(empty), StringComparison.Ordinal)
            .Replace("__POPULATED__", EscapeStatementErrorPath(populated), StringComparison.Ordinal)
            .Replace("__NESTED__", EscapeStatementErrorPath(nested), StringComparison.Ordinal)
            .Replace("__MISSING__", EscapeStatementErrorPath(Path.Combine(fixture.RootPath, "missing")), StringComparison.Ordinal);
        var original = RunModuleProof(fixture.ScriptPath, command, "pwsh");
        var generated = RunModuleProof(result.ArtifactPath!, command, "pwsh");
        Assert.True(original == generated, "Original: " + original + Environment.NewLine + "Generated: " + generated);
        Assert.Contains("\"records\":[true,false,false]", generated);
        Assert.Contains("\"IsEmpty\":false", generated);
    }
}

using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativeFunctionFileContextHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(explicitExports => configuration.Concat(new object[] { explicitExports }).ToArray()));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativeFunctionFileContextHosts))]
    public void NativeFunctionFileContext_PreservesRootAndSupportFileDefaults(string framework, string host, bool explicitExports)
    {
        const string function = """
            function Read-NativeFile {
                [CmdletBinding()] param([ValidateNotNullOrEmpty()][string]$Root = $PSScriptRoot,
                    [string]$File = $PSCommandPath)
                return "$Root|$File|$PSScriptRoot|$PSCommandPath"
            }
            """;
        using var fixture = ArtifactFixture.Create(function + "\n. \"$PSScriptRoot/Private/Read-SupportFile.ps1\"\n" +
            (explicitExports ? "Export-ModuleMember -Function Read-NativeFile,Read-SupportFile" : string.Empty), ".psm1");
        var sourceRoot = Path.GetDirectoryName(fixture.ScriptPath)!;
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Private"));
        File.WriteAllText(Path.Combine(sourceRoot, "Private", "Read-SupportFile.ps1"),
            function.Replace("Read-NativeFile", "Read-SupportFile", StringComparison.Ordinal));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeFileContext", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        // Contained support scripts currently remain native module dependencies.
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach($command in 'Read-NativeFile','Read-SupportFile') {
                $info=Get-Command $command
                $file=$info.ScriptBlock.File
                $root=Split-Path -Parent $file
                $expected="$root|$file|$root|$file"
                $actual=& $command
                if(!$file -or $actual -ne $expected) { throw "Context mismatch for ${command}: actual=$actual expected=$expected" }
                if($command -eq 'Read-SupportFile' -and (Split-Path -Leaf $file) -ne 'Read-SupportFile.ps1') {
                    throw "Support context incorrectly uses $file"
                }
                $command+':verified'
            }
            """;
        foreach (var modulePath in new[] { fixture.ScriptPath, result.ArtifactPath! })
        {
            var observed = RunStatementErrorProbe(host,
                "Import-Module '" + EscapeStatementErrorPath(modulePath) + "'; " + probe,
                fixture.RootPath, Path.GetFileName(modulePath) + "-native-file-context");
            Assert.True(observed.ExitCode == 0 && string.IsNullOrWhiteSpace(observed.StandardError),
                observed.StandardOutput + observed.StandardError);
            Assert.Contains("Read-NativeFile:verified", observed.StandardOutput);
            Assert.Contains("Read-SupportFile:verified", observed.StandardOutput);
        }
    }
}

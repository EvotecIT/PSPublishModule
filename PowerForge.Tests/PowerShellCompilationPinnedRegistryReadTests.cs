using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedRegistryReads_PreserveEnumArgumentsAndIndexedConditionalValues(string framework, string host)
    {
        var source = new[] { "Get-PSSubRegistry.ps1", "Get-PSSubRegistryComplete.ps1" }
            .Select(name => FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Private", name));
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, source.Select(File.ReadAllText)), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PinnedRegistryReads",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        // Only a new GUID-named child of HKCU\Software is written. The original
        // workload reads that child; finally removes the exact task-owned key.
        const string probe = """
            $ownedKeyPath='Software\PowerForgeCompilerFixture-'+[guid]::NewGuid().ToString('N')
            $env:PFC_TEST_VALUE='compiler-temp'
            $created=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($ownedKeyPath)
            try {
                $created.SetValue('', 'default-value', [Microsoft.Win32.RegistryValueKind]::String)
                $created.SetValue('Plain', 'plain-value', [Microsoft.Win32.RegistryValueKind]::String)
                $created.SetValue('Expand', '%PFC_TEST_VALUE%\folder', [Microsoft.Win32.RegistryValueKind]::ExpandString)
                $created.SetValue('Number', 17, [Microsoft.Win32.RegistryValueKind]::DWord)
                $created.Close()
                $registry=@{HiveKey=[Microsoft.Win32.RegistryHive]::CurrentUser;SubKeyName=$ownedKeyPath;Registry='HKCU\CompilerFixture';Key='Expand'}
                foreach($expand in $false,$true) {
                    @(Get-PSSubRegistry -Registry $registry -ComputerName local -ExpandEnvironmentNames:$expand)|ConvertTo-Json -Compress -Depth 8
                    foreach($advanced in $false,$true) {
                        @(Get-PSSubRegistryComplete -Registry $registry -ComputerName local -Advanced:$advanced -ExpandEnvironmentNames:$expand)|ConvertTo-Json -Compress -Depth 8
                    }
                }
                $registry.Key='missing-value'
                @(Get-PSSubRegistry -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                $registry.SubKeyName=$ownedKeyPath+'\missing-subkey'
                @(Get-PSSubRegistry -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                @(Get-PSSubRegistryComplete -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                $registry.HiveKey='invalid-hive'
                @(Get-PSSubRegistry -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                @(Get-PSSubRegistryComplete -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                $registry.Error=$true; $registry.ErrorMessage='authored-source-failure'
                @(Get-PSSubRegistry -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                @(Get-PSSubRegistryComplete -Registry $registry -ComputerName local)|ConvertTo-Json -Compress -Depth 8
                $registry.ComputerName='other'
                'mismatch='+@(Get-PSSubRegistry -Registry $registry -ComputerName local).Count
            } finally {
                $created.Dispose()
                [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($ownedKeyPath, $false)
            }
            $check=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($ownedKeyPath)
            try { 'cleaned='+($null -eq $check) } finally { if($check){$check.Dispose()} }
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("compiler-temp", generated);
        Assert.Contains("authored-source-failure", generated);
        Assert.Contains("cleaned=True", generated);
    }
}

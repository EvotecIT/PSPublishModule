using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ModuleMergeComposerImportCleanupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExportSynchronizationPreservesImportCleanup(bool appendGeneratedScript, bool failImport)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-export-cleanup-" + Guid.NewGuid().ToString("N")));
        try
        {
            var modulePath = Path.Combine(root.FullName, "Demo.psm1");
            var manifest = Path.Combine(root.FullName, "Demo.psd1");
            File.WriteAllText(manifest, "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-New') }");
            var source = """
try {
    function Get-New { 'new' }
# PowerForge exports begin
$FunctionsToExport = @('Get-Old')
$AliasesToExport = @()
$CmdletsToExport = @()
Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport
# PowerForge exports end
    if ($global:FailImportForProof) { throw 'late-import-failure' }
} catch {
    $global:ImportCleanupForProof = $true
    throw
}
""";
            if (appendGeneratedScript)
            {
                File.WriteAllText(modulePath, source);
                var script = Path.Combine(root.FullName, "Generated.ps1");
                File.WriteAllText(script, "function Get-New { 'generated' }");
                ModuleMergeComposer.SyncMergedPsm1WithGeneratedScripts(manifest, root.FullName, "Demo", new[] { script });
                // The pipeline synchronizes exports again after adding delivery commands.
                File.WriteAllText(modulePath, ModuleMergeComposer.ReplaceExportBlock(File.ReadAllText(modulePath), """
$FunctionsToExport = @('Get-New')
$AliasesToExport = @()
$CmdletsToExport = @()
Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport
"""));
            }
            else
            {
                var exportBlock = """
$FunctionsToExport = @('Get-New')
$AliasesToExport = @()
$CmdletsToExport = @()
Export-ModuleMember -Function $FunctionsToExport -Alias $AliasesToExport -Cmdlet $CmdletsToExport
""";
                File.WriteAllText(modulePath, ModuleMergeComposer.ReplaceExportBlock(source, exportBlock));
            }

            var initialState = System.Management.Automation.Runspaces.InitialSessionState.CreateDefault2();
            initialState.ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy.Bypass;
            using var host = PowerShell.Create(initialState);
            host.AddScript("""
param($path, $fail)
$global:FailImportForProof = $fail
$global:ImportCleanupForProof = $false
try {
    Import-Module $path -ErrorAction Stop
    if ($fail) { throw 'Import unexpectedly succeeded.' }
    Get-New
    Remove-Module Demo
} catch {
    if (-not $fail -or $_.Exception.Message -notlike '*late-import-failure*') { throw }
    $global:ImportCleanupForProof
}
""").AddArgument(modulePath).AddArgument(failImport);
            var output = host.Invoke();
            Assert.Empty(host.Streams.Error);
            if (failImport) Assert.Equal(true, Assert.Single(output).BaseObject);
            else
            {
                Assert.False(host.HadErrors);
                Assert.Equal(appendGeneratedScript ? "generated" : "new", Assert.Single(output).BaseObject);
            }
        }
        finally { root.Delete(recursive: true); }
    }
}

using System.Management.Automation;
using System.Text;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ExportDetectorTests
{
    [Fact]
    public void DetectBinaryCmdlets_finds_CmdletAttribute()
    {
        var path = typeof(GetExampleCommand).Assembly.Location;
        var cmdlets = BinaryExportDetector.DetectBinaryCmdlets(new[] { path });

        Assert.Contains("Get-Example", cmdlets);
    }

    [Fact]
    public void DetectBinaryAliases_finds_AliasAttribute()
    {
        var path = typeof(GetExampleCommand).Assembly.Location;
        var aliases = BinaryExportDetector.DetectBinaryAliases(new[] { path });

        Assert.Contains("gex", aliases);
        Assert.Contains("Get-ExampleAlias", aliases);
    }

    [Fact]
    public void DetectBinaryCmdlets_finds_PSPublishModule_plugin_and_bundle_cmdlets()
    {
        var path = typeof(InvokePowerForgePluginExportCommand).Assembly.Location;
        var cmdlets = BinaryExportDetector.DetectBinaryCmdlets(new[] { path });

        Assert.Contains("Invoke-PowerForgeBundlePostProcess", cmdlets);
        Assert.Contains("Invoke-PowerForgePluginExport", cmdlets);
        Assert.Contains("Invoke-PowerForgePluginPack", cmdlets);
    }

    [Fact]
    public void GeneratedModuleFiles_export_PSPublishModule_plugin_and_bundle_cmdlets()
    {
        var repoRoot = RepoRootLocator.Find();
        var manifestPath = Path.Combine(repoRoot, "Module", "PSPublishModule.psd1");
        var bootstrapperPath = Path.Combine(repoRoot, "Module", "PSPublishModule.psm1");

        var exports = ModuleManifestExportReader.ReadExports(manifestPath);
        var bootstrapper = File.ReadAllText(bootstrapperPath);

        Assert.Contains("Invoke-PowerForgeBundlePostProcess", exports.Cmdlets);
        Assert.Contains("Invoke-PowerForgePluginExport", exports.Cmdlets);
        Assert.Contains("Invoke-PowerForgePluginPack", exports.Cmdlets);
        Assert.Contains("Invoke-PowerForgeBundlePostProcess", bootstrapper, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgePluginExport", bootstrapper, StringComparison.Ordinal);
        Assert.Contains("Invoke-PowerForgePluginPack", bootstrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void DetectScriptFunctions_ignores_nested_helper_functions()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var scriptPath = Path.Combine(root.FullName, "Install-Example.ps1");
            File.WriteAllText(scriptPath, """
function Install-Example {
    function Write-DeliveryError {
        param([string] $Message)
        Write-Error $Message
    }

    Write-DeliveryError -Message 'boom'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var functions = new PowerShellScriptFunctionExportDetector().DetectScriptFunctions(new[] { scriptPath });

            Assert.Contains("Install-Example", functions);
            Assert.DoesNotContain("Write-DeliveryError", functions);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Cmdlet(VerbsCommon.Get, "Example")]
    [Alias("gex", "Get-ExampleAlias")]
    private sealed class GetExampleCommand : PSCmdlet
    {
    }

}

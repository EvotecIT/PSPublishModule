using System.Management.Automation;
using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellAssemblyMetadataServiceTests
{
    [Fact]
    public void Analyze_returns_cmdlets_and_aliases_for_binary_module_assembly()
    {
        var service = new PowerShellAssemblyMetadataService();
        var result = service.Analyze(typeof(GetWidgetCommand).Assembly.Location);

        Assert.Contains("Get-Widget", result.CmdletsToExport);
        Assert.Contains("gwid", result.AliasesToExport);
        Assert.Contains("Get-WidgetAlias", result.AliasesToExport);
    }

    [Fact]
    public void Analyze_throws_for_missing_assembly()
    {
        var service = new PowerShellAssemblyMetadataService();

        Assert.Throws<FileNotFoundException>(() => service.Analyze(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll")));
    }

    [Cmdlet(VerbsCommon.Get, "Widget")]
    [Alias("gwid", "Get-WidgetAlias")]
    private sealed class GetWidgetCommand : PSCmdlet
    {
    }
}

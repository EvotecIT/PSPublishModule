using System.Management.Automation;

namespace PowerForge.Tests;

public sealed class ExportDetectorTests
{
    [Fact]
    public void DetectBinaryCmdlets_finds_CmdletAttribute()
    {
        var path = typeof(GetExampleCommand).Assembly.Location;
        var cmdlets = ExportDetector.DetectBinaryCmdlets(new[] { path });

        Assert.Contains("Get-Example", cmdlets);
    }

    [Fact]
    public void DetectBinaryAliases_finds_AliasAttribute()
    {
        var path = typeof(GetExampleCommand).Assembly.Location;
        var aliases = ExportDetector.DetectBinaryAliases(new[] { path });

        Assert.Contains("gex", aliases);
        Assert.Contains("Get-ExampleAlias", aliases);
    }

    [Cmdlet(VerbsCommon.Get, "Example")]
    [Alias("gex", "Get-ExampleAlias")]
    private sealed class GetExampleCommand : PSCmdlet
    {
    }
}

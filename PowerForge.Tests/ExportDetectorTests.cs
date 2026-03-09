using System.Management.Automation;
using System.Text;

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

            var functions = ExportDetector.DetectScriptFunctions(new[] { scriptPath });

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

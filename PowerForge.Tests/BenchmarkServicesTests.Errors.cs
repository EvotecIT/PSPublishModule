using System.Management.Automation;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void DslRuntime_RejectsNonterminatingConfigurationErrors()
    {
        var script = ScriptBlock.Create("Write-Error 'invalid benchmark configuration'");
        var exception = Assert.ThrowsAny<Exception>(() => EvaluateBenchmarkDslWithoutImportedCommands(script));
        Assert.Contains("invalid benchmark configuration", exception.ToString(), StringComparison.Ordinal);
    }
}

using System.Reflection;
using PowerForge;

namespace PowerForge.Tests;

public sealed class PssaFormatterIsolationTests
{
    [Fact]
    public void EmbeddedFormatter_UsesFilesystemDiscoveryWithoutRemovingExplicitModuleResolutionRoots()
    {
        var buildScript = typeof(PssaFormatter).GetMethod("BuildScript", BindingFlags.NonPublic | BindingFlags.Static);
        var script = Assert.IsType<string>(buildScript?.Invoke(null, null));

        var discoveryIndex = script.IndexOf("[System.IO.Directory]::EnumerateDirectories($PssaRoot)", StringComparison.Ordinal);
        var importIndex = script.IndexOf("Import-Module -Name $PssaModulePath", StringComparison.Ordinal);

        Assert.True(discoveryIndex >= 0);
        Assert.True(importIndex > discoveryIndex);
        Assert.DoesNotContain("Get-Module -ListAvailable", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$env:PSModulePath =", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedFormatter_OnlyToleratesKnownPowerShellClassMemberMetadataFailure()
    {
        var buildScript = typeof(PssaFormatter).GetMethod("BuildScript", BindingFlags.NonPublic | BindingFlags.Static);
        var script = Assert.IsType<string>(buildScript?.Invoke(null, null));

        Assert.Contains("-ErrorVariable +formatterErrors", script, StringComparison.Ordinal);
        Assert.Contains("*PowerShellCustomFunctionAttribute*", script, StringComparison.Ordinal);
        Assert.Contains("*FunctionMemberAst*", script, StringComparison.Ordinal);
        Assert.Contains("*FunctionDefinitionAst*", script, StringComparison.Ordinal);
        Assert.Contains("$unexpectedErrors.Count -gt 0 -or $null -eq $formatted", script, StringComparison.Ordinal);
        Assert.Contains("WARNING::", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatFiles_LogsCompatibilityWarningWithoutTurningItIntoAnError()
    {
        var logger = new CollectingLogger();
        var runner = new StubRunner(new PowerShellRunResult(
            0,
            "WARNING::module.psm1::Class-member casing was skipped.\r\nUNCHANGED::module.psm1\r\n",
            string.Empty,
            "pwsh"));
        var formatter = new PssaFormatter(runner, logger);

        var result = Assert.Single(formatter.FormatFiles(new[] { "module.psm1" }));

        Assert.False(result.Changed);
        Assert.Equal("Unchanged", result.Message);
        Assert.Contains(logger.Warnings, warning => warning.Contains("module.psm1", StringComparison.Ordinal));
    }

    private sealed class StubRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubRunner(PowerShellRunResult result) => _result = result;

        public PowerShellRunResult Run(PowerShellRunRequest request) => _result;
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();
        public bool IsVerbose => false;
        public void Info(string message) { }
        public void Success(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message) { }
        public void Verbose(string message) { }
    }
}

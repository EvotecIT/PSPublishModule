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
        Assert.Contains("*PowerShellCustomFunctionAttribute*' -and", script, StringComparison.Ordinal);
        Assert.Contains("*FunctionMemberAst*' -and", script, StringComparison.Ordinal);
        Assert.Contains("$unexpectedErrors.Count -gt 0 -or $null -eq $formatted", script, StringComparison.Ordinal);
        Assert.Contains("WARNING::", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PowerShellCustomFunctionAttribute metadata is unavailable")]
    [InlineData("FunctionMemberAst metadata is unavailable")]
    [InlineData("FunctionDefinitionAst metadata is unavailable")]
    public void EmbeddedFormatter_ToleratesEachKnownClassMemberMetadataFailure(string errorMessage)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string moduleRoot = Path.Combine(root, "modules", "PSScriptAnalyzer", "999.0.0");
            Directory.CreateDirectory(moduleRoot);
            WriteTestModule(
                moduleRoot,
                "999.0.0",
                $$"""
                function Invoke-Formatter {
                    [CmdletBinding()]
                    param([string] $ScriptDefinition, [hashtable] $Settings)
                    Write-Error '{{errorMessage}}'
                    return $ScriptDefinition
                }
                Export-ModuleMember -Function Invoke-Formatter
                """);

            string inputPath = Path.Combine(root, "module.ps1");
            File.WriteAllText(inputPath, "Get-Date");
            var logger = new CollectingLogger();
            var formatter = new PssaFormatter(
                new EnvironmentPowerShellRunner(new Dictionary<string, string?>
                {
                    ["PSModulePath"] = Path.Combine(root, "modules")
                }),
                logger);

            FormatterResult result = Assert.Single(formatter.FormatFiles(new[] { inputPath }));

            Assert.False(result.Changed);
            Assert.Equal("Unchanged", result.Message);
            Assert.Single(logger.Warnings);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
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

    [Fact]
    public void EmbeddedFormatter_TriesDiscoveredCandidatesNewestFirstUntilOneImports()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var moduleRoot = Path.Combine(root, "modules", "PSScriptAnalyzer");
            var newestRoot = Path.Combine(moduleRoot, "999.0.0");
            var olderRoot = Path.Combine(moduleRoot, "998.0.0");
            Directory.CreateDirectory(newestRoot);
            Directory.CreateDirectory(olderRoot);

            WriteTestModule(
                newestRoot,
                "999.0.0",
                """
                [System.IO.File]::AppendAllText($env:PSSA_IMPORT_LOG, "999.0.0`n")
                throw 'The newest test module is incompatible with this host.'
                """);
            WriteTestModule(
                olderRoot,
                "998.0.0",
                """
                [System.IO.File]::AppendAllText($env:PSSA_IMPORT_LOG, "998.0.0`n")
                function Invoke-Formatter {
                    param([string] $ScriptDefinition, [hashtable] $Settings)
                    return $ScriptDefinition
                }
                Export-ModuleMember -Function Invoke-Formatter
                """);

            var importLogPath = Path.Combine(root, "imports.log");
            var inputPath = Path.Combine(root, "module.ps1");
            File.WriteAllText(inputPath, "Get-Date");
            var runner = new EnvironmentPowerShellRunner(new Dictionary<string, string?>
            {
                ["PSModulePath"] = Path.Combine(root, "modules"),
                ["PSSA_IMPORT_LOG"] = importLogPath
            });
            var formatter = new PssaFormatter(runner, new CollectingLogger());

            FormatterResult result = Assert.Single(formatter.FormatFiles(new[] { inputPath }));

            Assert.Equal(new[] { "999.0.0", "998.0.0" }, File.ReadAllLines(importLogPath));
            Assert.False(result.Changed);
            Assert.Equal("Unchanged", result.Message);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test-owned temporary files.
            }
        }

    }

    [Fact]
    public void EmbeddedFormatter_RanksDirectInstallByManifestVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string moduleRoot = Path.Combine(root, "modules", "PSScriptAnalyzer");
            string versionedRoot = Path.Combine(moduleRoot, "999.0.0");
            Directory.CreateDirectory(moduleRoot);
            Directory.CreateDirectory(versionedRoot);

            WriteTestModule(
                moduleRoot,
                "1000.0.0",
                """
                [System.IO.File]::AppendAllText($env:PSSA_IMPORT_LOG, "direct`n")
                function Invoke-Formatter {
                    param([string] $ScriptDefinition, [hashtable] $Settings)
                    return $ScriptDefinition
                }
                Export-ModuleMember -Function Invoke-Formatter
                """);
            WriteTestModule(
                versionedRoot,
                "999.0.0",
                """
                [System.IO.File]::AppendAllText($env:PSSA_IMPORT_LOG, "versioned`n")
                function Invoke-Formatter {
                    param([string] $ScriptDefinition, [hashtable] $Settings)
                    return $ScriptDefinition
                }
                Export-ModuleMember -Function Invoke-Formatter
                """);

            string importLogPath = Path.Combine(root, "imports.log");
            string inputPath = Path.Combine(root, "module.ps1");
            File.WriteAllText(inputPath, "Get-Date");
            var formatter = new PssaFormatter(
                new EnvironmentPowerShellRunner(new Dictionary<string, string?>
                {
                    ["PSModulePath"] = Path.Combine(root, "modules"),
                    ["PSSA_IMPORT_LOG"] = importLogPath
                }),
                new CollectingLogger());

            FormatterResult result = Assert.Single(formatter.FormatFiles(new[] { inputPath }));

            Assert.Equal(new[] { "direct" }, File.ReadAllLines(importLogPath));
            Assert.False(result.Changed);
            Assert.Equal("Unchanged", result.Message);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void WriteTestModule(string versionRoot, string version, string moduleScript)
    {
        File.WriteAllText(
            Path.Combine(versionRoot, "PSScriptAnalyzer.psd1"),
            $$"""
            @{
                RootModule = 'PSScriptAnalyzer.psm1'
                ModuleVersion = '{{version}}'
                FunctionsToExport = @('Invoke-Formatter')
            }
            """);
        File.WriteAllText(Path.Combine(versionRoot, "PSScriptAnalyzer.psm1"), moduleScript);
    }

    private sealed class StubRunner : IPowerShellRunner
    {
        private readonly PowerShellRunResult _result;

        public StubRunner(PowerShellRunResult result) => _result = result;

        public PowerShellRunResult Run(PowerShellRunRequest request) => _result;
    }

    private sealed class EnvironmentPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string?> _environmentVariables;
        private readonly PowerShellRunner _inner = new();

        public EnvironmentPowerShellRunner(IReadOnlyDictionary<string, string?> environmentVariables)
            => _environmentVariables = environmentVariables;

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _inner.Run(new PowerShellRunRequest(
                request.ScriptPath!,
                request.Arguments,
                request.Timeout,
                request.PreferPwsh,
                request.WorkingDirectory,
                _environmentVariables,
                request.ExecutableOverride,
                request.CaptureOutput,
                request.CaptureError,
                request.OutputLineReceived,
                request.ErrorLineReceived));
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

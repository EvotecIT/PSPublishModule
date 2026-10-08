using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class BenchmarkServicesTests
{
    [Fact]
    public void PlacementPolicy_CompiledAndFallbackDslRetainSettingsWithoutApplyingThem()
    {
        const string spec = "New-BenchmarkSuite 'placement' { Set-BenchmarkPolicy -ProcessorAffinityMask 3 -ProcessPriority BelowNormal; Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { 1 } } }";
        var suite = Assert.Single(EvaluateBenchmarkDsl(ScriptBlock.Create(spec)));
        Assert.Equal(3UL, suite.ProcessorAffinityMask);
        Assert.Equal(ProcessPriorityClass.BelowNormal, suite.ProcessPriority);

        var previous = Runspace.DefaultRunspace;
        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        try
        {
            Runspace.DefaultRunspace = runspace;
            var fallback = Assert.Single(PowerShellBenchmarkDslRuntime.Evaluate(ScriptBlock.Create(spec)));
            Assert.Equal(suite.ProcessorAffinityMask, fallback.ProcessorAffinityMask);
            Assert.Equal(suite.ProcessPriority, fallback.ProcessPriority);
            // Planning remains portable and does not apply Windows-only execution settings.
            Assert.Single(new PowerShellBenchmarkRunner().Plan(fallback));
        }
        finally { Runspace.DefaultRunspace = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlacementRunner_CoversSetupMeasurementsValidationAndRestoresAfterFailure(bool failOperation)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Throws<PlatformNotSupportedException>(() => PowerShellBenchmarkProcessPlacement.Enter(
                new PowerShellBenchmarkSuite { ProcessorAffinityMask = 1 }));
            return;
        }
        using var process = Process.GetCurrentProcess();
        var originalAffinity = process.ProcessorAffinity;
        var originalPriority = process.PriorityClass;
        var mask = unchecked((ulong)originalAffinity.ToInt64());
        var selected = mask & unchecked(0UL - mask);
        var root = CreateTempRoot();
        try
        {
            var check = $"$p = [System.Diagnostics.Process]::GetCurrentProcess(); try {{ if ([ulong]$p.ProcessorAffinity.ToInt64() -ne {selected} -or $p.PriorityClass -ne 'BelowNormal') {{ throw 'Placement did not cover this stage.' }} }} finally {{ $p.Dispose() }};";
            var suite = Assert.Single(EvaluateBenchmarkDsl(ScriptBlock.Create($$"""
New-BenchmarkSuite 'placement' -OutputRoot '{{root.Replace("'", "''")}}' {
    Set-BenchmarkPolicy -Warmup 1 -Iteration 1 -ProcessorAffinityMask {{selected}} -ProcessPriority BelowNormal
    Set-BenchmarkSetup { {{check}} }
    Add-BenchmarkEngine Managed { Add-BenchmarkOperation Run { {{check}} {{(failOperation ? "throw 'Observed operation failure'" : "1")}} } }
    Add-BenchmarkValidation { {{check}} $true }
    Set-BenchmarkArtifacts None
}
""")));
            var result = new PowerShellBenchmarkRunner().Run(suite);
            Assert.Equal("0x" + selected.ToString("X"), result.Metadata["processAffinityMask"]);
            Assert.Equal("BelowNormal", result.Metadata["processPriority"]);
            Assert.Contains(result.Samples, sample => sample.Status == (failOperation ? BenchmarkSampleStatus.Failed : BenchmarkSampleStatus.Succeeded));
            process.Refresh();
            Assert.Equal(originalAffinity, process.ProcessorAffinity);
            Assert.Equal(originalPriority, process.PriorityClass);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void PlacementScope_RejectsInvalidMasksAndConcurrentControllersWithoutLeakingState()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var process = Process.GetCurrentProcess();
        var original = process.ProcessorAffinity;
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerShellBenchmarkProcessPlacement.Enter(
            new PowerShellBenchmarkSuite { ProcessorAffinityMask = 0 }));
        var suite = new PowerShellBenchmarkSuite { ProcessorAffinityMask = unchecked((ulong)original.ToInt64()) };
        using (var first = PowerShellBenchmarkProcessPlacement.Enter(suite))
            Assert.Throws<InvalidOperationException>(() => PowerShellBenchmarkProcessPlacement.Enter(suite));
        using var next = PowerShellBenchmarkProcessPlacement.Enter(suite);
        process.Refresh();
        Assert.Equal(original, process.ProcessorAffinity);
    }

    [Fact]
    public void PlacementHostRequest_PreservesOverridesAndMergedHostEvidence()
    {
        var child = PowerShellBenchmarkHostExecutor.CreateChildRequest(new PowerShellBenchmarkHostRunRequest
        {
            ProcessorAffinityMask = 0x8000000000000001UL,
            ProcessPriority = ProcessPriorityClass.BelowNormal,
            MemorySamplingIntervalMilliseconds = 5
        }, "Current", "pwsh", "result.json", "readme-paths.txt", DateTimeOffset.UtcNow);
        Assert.Equal("8000000000000001", child.ProcessorAffinityMask);
        Assert.Equal("BelowNormal", child.ProcessPriority);
        Assert.Equal(5, child.MemorySamplingIntervalMilliseconds);
        var suite = new PowerShellBenchmarkSuite { Name = "placement" };
        var run = new BenchmarkRunResult { Metadata = new Dictionary<string, string> { ["processAffinityMask"] = "0xFFFF", ["processPriority"] = "BelowNormal" } };
        var merged = PowerShellBenchmarkResultMerger.Merge(suite, new[] { run }, DateTimeOffset.UtcNow,
            PowerShellBenchmarkEnvironmentMetadata.CaptureSourceProvenance(suite));
        Assert.Contains(merged.Metadata, pair => pair.Key.StartsWith("host.", StringComparison.Ordinal) && pair.Key.EndsWith(".processAffinityMask", StringComparison.Ordinal) && pair.Value == "0xFFFF");
    }

    [Fact]
    public void PlacementMerge_PreservesDistinctExecutablesWithTheSameRuntimeAndRejectsAmbiguity()
    {
        var suite = new PowerShellBenchmarkSuite { Name = "placement" };
        var children = new[] { "x86", "x64" }.Select(architecture => new BenchmarkRunResult
        {
            Samples = new[] { new BenchmarkSample { Host = "Desktop-5.1" } },
            Metadata = new Dictionary<string, string>
            {
                ["hostExecutablePath"] = "/hosts/" + architecture + "/powershell.exe",
                ["originalProcessAffinityMask"] = architecture == "x86" ? "0xFFFFFFFF" : "0xFFFFFFFFFFFFFFFF"
            }
        }).ToArray();
        var provenance = PowerShellBenchmarkEnvironmentMetadata.CaptureSourceProvenance(suite);
        var result = PowerShellBenchmarkResultMerger.Merge(suite, children, DateTimeOffset.UtcNow, provenance);
        Assert.Equal("0xFFFFFFFF", result.Metadata["host./hosts/x86/powershell.exe.originalProcessAffinityMask"]);
        Assert.Equal("0xFFFFFFFFFFFFFFFF", result.Metadata["host./hosts/x64/powershell.exe.originalProcessAffinityMask"]);
        foreach (var child in children) child.Metadata.Remove("hostExecutablePath");
        Assert.Throws<InvalidOperationException>(() => PowerShellBenchmarkResultMerger.Merge(suite, children, DateTimeOffset.UtcNow, provenance));
    }
}

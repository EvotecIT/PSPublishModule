using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    public void StrictPipelineStagesPreserveTheirRecordOwnersOnBothPinnedHosts()
    {
        var reduction = FilterAggregate("1, 2, -1", "$_ -gt 0")
            .Replace("$values |", "if ($true) { return 1, 2, -1 |", StringComparison.Ordinal) + "; }";
        CompareStrictStreamingCases(
            ("NestedReturnReduction", reduction),
            ("CaptureAsInput", """
                [int[]] $selected = @(1,2,3 | Where-Object { $_ -gt 1 })
                $selected | Where-Object { $_ -lt 3 }
                """),
            ("SwitchStageItem", """
                switch (1) {
                    1 { 1,2 | ForEach-Object { $_ }; break }
                }
                """),
            ("BeginCommandOutput", """
                function Get-Record {
                    [CmdletBinding()]
                    [OutputType([int])]
                    param([Parameter(ValueFromPipeline)][int] $Record)
                    begin { Write-Output 7 }
                    process {}
                    end { 8 }
                }
                1,2 | Get-Record
                """));
    }

    [PinnedSemanticHostFact]
    public async Task StrictReadHostKeepsAuthoredCarriageReturnLinesAndRejectsAnEmptyPrompt()
    {
        using var fixture = OracleFixture.Create("""
            try {
                $answer = Read-Host 'Input'
            } catch {
                "caught on line $($_.InvocationInfo.ScriptLineNumber)"
                exit 4
            }
            exit 0
            """.ReplaceLineEndings("\r"));
        using var invalid = OracleFixture.Create("$answer = Read-Host -Prompt ''; exit 0");
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictReadHostCr", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var expected = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-File", fixture.ScriptPath }, "\brest\n");
            Assert.Equal(4, expected.Code);
            Assert.Contains("caught on line 2", expected.Output, StringComparison.Ordinal);
            Assert.Equal(expected, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, Array.Empty<string>(), "\brest\n"));
            var emptyPrompt = BuildStrictStreamingFixture(invalid, "StrictReadHostEmptyPrompt", pin.ProfileId);
            Assert.False(emptyPrompt.Succeeded);
            Assert.Contains("Read-Host", emptyPrompt.Error, StringComparison.OrdinalIgnoreCase);
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictCleanupRequiresFallibleOperationsToHandleTheirErrorsLocally()
    {
        const string source = """
            function Get-Record {
                [CmdletBinding()]
                [OutputType([int])]
                param([Parameter(ValueFromPipeline)][int] $Record)
                begin {}
                process { $Record }
                end { 99 }
                clean { [IO.File]::AppendAllText('missing-parent/trace.txt', 'cleanup') }
            }
            try { return 1,2 | Get-Record }
            catch { 'outer catch'; exit 7 }
            """;
        const string operation = "[IO.File]::AppendAllText('missing-parent/trace.txt', 'cleanup')";
        const string trace = "[IO.File]::AppendAllText('trace.txt', 'caught locally')";
        var profile = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
        var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Pins.Single(candidate => candidate.ProfileId == profile);
        var host = StrictStreamingHostPath(pin)!;
        using var open = OracleFixture.Create(source);
        var nativeOpen = await ObserveExitedConsole(host, open.RootPath,
            new[] { "-NoProfile", "-NonInteractive", "-File", open.ScriptPath }, string.Empty);
        Assert.Equal(0, nativeOpen.Code);
        Assert.Equal(string.Join(Environment.NewLine, new[] { "1", "2", "99", "" }), nativeOpen.Output);
        Assert.Contains("missing-parent", nativeOpen.Error, StringComparison.Ordinal);
        var rejected = BuildStrictStreamingFixture(open, "UnboundedStrictCleanup", profile);
        Assert.False(rejected.Succeeded);
        Assert.True(rejected.Error?.Contains("exhaustive local catch", StringComparison.Ordinal) == true, rejected.Error);
        using var handled = OracleFixture.Create(source.Replace(operation,
            "try { " + operation + " } catch { try { " + trace + " } catch {} }", StringComparison.Ordinal));
        var build = BuildStrictStreamingFixture(handled, "LocallyHandledStrictCleanup", profile);
        Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
        var native = await ObserveExitedConsole(host, handled.RootPath,
            new[] { "-NoProfile", "-NonInteractive", "-File", handled.ScriptPath }, string.Empty);
        Assert.Equal((0, nativeOpen.Output, string.Empty), native);
        var tracePath = Path.Combine(handled.RootPath, "trace.txt");
        Assert.Equal("caught locally", File.ReadAllText(tracePath));
        File.Delete(tracePath);
        Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, handled.RootPath, Array.Empty<string>(), string.Empty));
        Assert.Equal("caught locally", File.ReadAllText(tracePath));
    }

    [PinnedSemanticHostFact]
    public async Task StrictCsvRetainsAnAuthoredWhitespaceHeaderOnBothPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Path)
            try {
                $rows = Import-Csv -LiteralPath $Path -Header ' ',Author,Notes
                foreach ($row in $rows) { "title=[$($row.' ')]" }
            } catch { exit 3 }
            """);
        var rows = Path.Combine(fixture.RootPath, "data.csv");
        File.WriteAllText(rows, "first,Ada,one\nsecond,Doe,two\n");
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var build = BuildStrictStreamingFixture(fixture, "StrictCsvWhitespaceHeader", pin.ProfileId);
            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, rows }, string.Empty);
            Assert.Equal(0, native.Code);
            Assert.Equal("title=[first]" + Environment.NewLine + "title=[second]" + Environment.NewLine, native.Output);
            Assert.Equal(native, await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, new[] { rows }, string.Empty));
        }
    }

    [PinnedSemanticHostFact]
    public async Task StrictRecursiveReaderContinuesAfterAnEnumeratedDirectoryDisappearsOnBothPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Tree)
            Get-ChildItem -LiteralPath $Tree -Recurse | ForEach-Object {
                $_.Name
                if ($_.Name -eq 'disappearing') { [IO.Directory]::Delete($_.FullName) }
            }
            exit 0
            """);
        var tree = Path.Combine(fixture.RootPath, "tree");
        var disappearing = Path.Combine(tree, "disappearing");
        var readable = Directory.CreateDirectory(Path.Combine(tree, "readable"));
        File.WriteAllText(Path.Combine(readable.FullName, "survives.txt"), "fixture");
        var expected = new[] { "disappearing", "readable", "survives.txt" };
        foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
        {
            var host = StrictStreamingHostPath(pin) ??
                Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            Directory.CreateDirectory(disappearing);
            var native = await ObserveExitedConsole(host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, tree }, string.Empty);
            Assert.Equal((0, string.Join(Environment.NewLine, expected) + Environment.NewLine, string.Empty), native);
            Assert.False(Directory.Exists(disappearing));
        }
        Directory.CreateDirectory(disappearing);
        using var reader = PowerForge.Generated.Runtime.PowerShellStrictFileSystemReader.ReadItems(tree).GetEnumerator();
        Assert.True(reader.MoveNext());
        Assert.Equal(expected[0], reader.Current.Name);
        Directory.Delete(disappearing);
        Assert.True(reader.MoveNext());
        Assert.Equal(expected[1], reader.Current.Name);
        Assert.True(reader.MoveNext());
        Assert.Equal(expected[2], reader.Current.Name);
        Assert.False(reader.MoveNext());
    }

    [PinnedSemanticHostFact]
    [SupportedOSPlatform("windows")]
    public async Task StrictRecursivePipelineReportsDeniedDirectoriesAndContinuesOnBothPinnedHosts()
    {
        using var fixture = OracleFixture.Create("""
            param([string] $Path)
            Get-ChildItem -Path $Path -Recurse -Attributes !Directory | Select-Object -ExpandProperty Name
            exit 0
            """);
        var tree = Path.Combine(fixture.RootPath, "tree");
        var inaccessible = Directory.CreateDirectory(Path.Combine(tree, "blocked"));
        var allowed = Directory.CreateDirectory(Path.Combine(tree, "readable"));
        File.WriteAllText(Path.Combine(inaccessible.FullName, "hidden-by-access.txt"), "fixture");
        File.WriteAllText(Path.Combine(allowed.FullName, "readable.txt"), "fixture");
        using var identity = WindowsIdentity.GetCurrent();
        var originalAcl = inaccessible.GetAccessControl();
        var denied = inaccessible.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, AccessControlType.Deny));
        inaccessible.SetAccessControl(denied);
        try
        {
            foreach (var pin in PowerShellCompilationSemanticHostArtifactPinCatalog.Pins)
            {
                var build = BuildStrictStreamingFixture(fixture, "StrictFilesystemPermission", pin.ProfileId);
                Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
                var host = StrictStreamingHostPath(pin) ??
                    Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var path = "FileSystem::" + tree;
                var expected = await ObserveExitedConsole(host, fixture.RootPath,
                    new[] { "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, path }, string.Empty);
                Assert.Equal(0, expected.Code);
                Assert.Equal("readable.txt" + Environment.NewLine, expected.Output);
                Assert.Contains(inaccessible.FullName, expected.Error.Replace(Environment.NewLine, string.Empty), StringComparison.OrdinalIgnoreCase);
                var actual = await ObserveExitedConsole(build.ArtifactPath!, fixture.RootPath, new[] { path }, string.Empty);
                Assert.Equal(expected.Code, actual.Code);
                Assert.Equal(expected.Output, actual.Output);
                Assert.Contains(inaccessible.FullName, actual.Error, StringComparison.OrdinalIgnoreCase);
                var observed = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(pin.ProfileId, build, new[] { path });
                Assert.Equal(0, observed.ExitCode);
                Assert.False(Assert.Single(observed.ErrorRecords).IsTerminating);
            }
        }
        finally
        {
            inaccessible.SetAccessControl(originalAcl);
        }
    }
}

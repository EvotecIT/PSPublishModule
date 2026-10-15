using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_NativeScriptClosureLoadsAtAuthoredPointWithArgumentsAndWorkingDirectory()
    {
        using var fixture = ArtifactFixture.Create("""
            param([Parameter(Mandatory)][string] $Expected)
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [System.Text.Encoding]([System.Text.UTF8Encoding]::new($false))
            $before = Get-Command -Name Read-OwnedValue -ErrorAction SilentlyContinue
            if ($before) { throw 'Helper was registered before its authored include' }
            Write-Output 'before:missing'
            $helperPath = Join-Path $PSScriptRoot './Helper.ps1'
            . $helperPath
            $value = Read-OwnedValue -Expected $Expected
            Write-Output "after:$value"
            Write-Output (Get-OwnedLength -Value $value)
            """, compactPath: true);
        File.WriteAllText(Path.Combine(fixture.RootPath, "Helper.ps1"), """
            function Read-OwnedValue {
                param([string] $Expected)
                $value = [System.IO.File]::ReadAllText('input.txt')
                if ($value -cne $Expected) { throw 'Owned working-directory input differs' }
                return $value
            }
            function Get-OwnedLength { param([string] $Value) return $Value.Length }
            """);
        const string expected = "Zażółć O'Brien \"quoted\"; $HOME &";
        const string workingDirectory = "owned data O'Brien";
        var cwd = Path.Combine(fixture.RootPath, workingDirectory);
        Directory.CreateDirectory(cwd);
        File.WriteAllText(Path.Combine(cwd, "input.txt"), expected);
        File.WriteAllText(fixture.ScriptPath, File.ReadAllText(fixture.ScriptPath), System.Text.Encoding.Unicode);
        var authoredBytes = File.ReadAllBytes(fixture.ScriptPath);
        var projectPath = Path.Combine(fixture.RootPath, "closure.json");
        var manifestService = new PowerShellCompilationProjectManifestService();
        var project = manifestService.Create(projectPath, fixture.ScriptPath, "NativeClosure", CreateNativeClosureTarget());
        project.Sources = new[] { "input.ps1", "Helper.ps1" };
        project.EntryPoint = "input.ps1";
        var artifact = Assert.Single(project.Artifacts);
        artifact.EmitSource = true;
        artifact.TestArguments = new[] { "--Expected=" + expected };
        artifact.TestWorkingDirectory = workingDirectory;
        manifestService.Save(projectPath, project);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(projectPath));
        AssertProjectSuccess(workflow.Restore(projectPath));
        var build = workflow.Build(projectPath);
        AssertProjectSuccess(build);
        var buildReceipt = JsonSerializer.Deserialize<PowerShellCompilationBuildResult>(
            File.ReadAllText(Path.Combine(fixture.RootPath, ".powerforge", "build", artifact.Name + ".json")),
            PowerShellCompilationProjectManifestService.JsonOptions)!;
        AssertNativeClosureBodies(buildReceipt, "Read-OwnedValue", "Get-OwnedLength");
        Assert.Equal(authoredBytes, File.ReadAllBytes(fixture.ScriptPath));
        Assert.Equal(authoredBytes, File.ReadAllBytes(Path.Combine(buildReceipt.GeneratedSourcePath!, "Source.ps1")));
        var original = RunNativeClosureProcess("pwsh", cwd,
            "-NoLogo", "-NoProfile", "-NonInteractive", "-File", fixture.ScriptPath, "-Expected", expected);
        var builtPath = Assert.Single(build.Targets).Path!;
        var built = RunNativeClosureProcess(builtPath, cwd, artifact.TestArguments);
        Assert.Equal((0, original.StdOut, string.Empty), (built.ExitCode, built.StdOut, built.StdErr));
        var missing = RunNativeClosureProcess(builtPath, cwd);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains("Expected", missing.StdErr, StringComparison.Ordinal);
        Assert.DoesNotContain("before:missing", missing.StdOut, StringComparison.Ordinal);
        Assert.Equal(new[] { "before:missing", "after:" + expected, expected.Length.ToString() },
            built.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static line => line.TrimEnd('\r')).ToArray());
        AssertProjectSuccess(workflow.Test(projectPath));
        AssertProjectSuccess(workflow.Pack(projectPath));
        var installed = workflow.Install(projectPath);
        AssertProjectSuccess(installed);
        var relativePrimary = Path.GetRelativePath(Path.Combine(fixture.RootPath, artifact.OutputDirectory), builtPath);
        var installedPath = Path.Combine(Assert.Single(installed.Targets).Path!, "artifact", relativePrimary);
        var installedRun = RunNativeClosureProcess(installedPath, cwd, artifact.TestArguments);
        Assert.Equal((built.ExitCode, built.StdOut, built.StdErr),
            (installedRun.ExitCode, installedRun.StdOut, installedRun.StdErr));
        Assert.Equal(authoredBytes, File.ReadAllBytes(fixture.ScriptPath));
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeScriptClosureRejectsLiveJoinPathReplacementToExternalOrWrongEmbeddedFile()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string] $Mode = 'Expected', [string] $ExternalPath)
            $ErrorActionPreference = 'Stop'
            $global:OwnedJoinCalls = 0
            function Join-Path {
                param([string] $Path, [string] $ChildPath)
                $global:OwnedJoinCalls++
                if ($Mode -eq 'External') { return $ExternalPath }
                if ($Mode -eq 'Other') { return [System.IO.Path]::Combine($Path, 'Other.ps1') }
                return [System.IO.Path]::Combine($Path, $ChildPath)
            }
            $helperPath = Join-Path $PSScriptRoot 'Helper.ps1'
            . $helperPath
            Write-Output 'selected-source-loaded'
            Read-OwnedValue
            . "$PSScriptRoot/Other.ps1"
            Get-OtherValue
            Write-Output "joins:$global:OwnedJoinCalls"
            """, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        var other = Path.Combine(fixture.RootPath, "Other.ps1");
        File.WriteAllText(helper, "function Read-OwnedValue { return 'expected-helper' }");
        File.WriteAllText(other, "function Get-OtherValue { return 'other-helper' }");
        var external = Path.Combine(fixture.RootPath, "external.ps1");
        var marker = Path.Combine(fixture.RootPath, "external-executed.txt");
        File.WriteAllText(external, "[System.IO.File]::WriteAllText('" + marker.Replace("'", "''") + "', 'executed')");
        var result = BuildNativeClosure(fixture, helper, other);
        AssertNativeClosureBodies(result, "Join-Path", "Read-OwnedValue", "Get-OtherValue");
        var accepted = RunNativeClosureProcess(result.ArtifactPath!, fixture.RootPath);
        Assert.Equal(0, accepted.ExitCode);
        Assert.Empty(accepted.StdErr);
        Assert.Equal(new[] { "selected-source-loaded", "expected-helper", "other-helper", "joins:1" },
            accepted.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(static line => line.TrimEnd('\r')).ToArray());
        foreach (var mode in new[] { "External", "Other" })
        {
            var refused = RunNativeClosureProcess(result.ArtifactPath!, fixture.RootPath,
                "--Mode=" + mode, "--ExternalPath=" + external);
            Assert.NotEqual(0, refused.ExitCode);
            AssertNativeClosureDiagnostic("does not match its selected staged source file", refused.StdErr);
            Assert.DoesNotContain("selected-source-loaded", refused.StdOut, StringComparison.Ordinal);
            Assert.False(File.Exists(marker), "The external source must not execute before path refusal.");
        }
    }

    [WindowsX64Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Build_NativeScriptClosureRejectsHelperMutationAtAuthoredLoadPoint()
    {
        using var fixture = ArtifactFixture.Create("""
            $ErrorActionPreference = 'Stop'
            $helperPath = Join-Path $PSScriptRoot 'Helper.ps1'
            [System.IO.File]::AppendAllText($helperPath, "`n# changed after extraction")
            Write-Output 'mutation-written'
            . $helperPath
            Write-Output 'mutated-source-loaded'
            Read-OwnedValue
            """, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "Helper.ps1");
        File.WriteAllText(helper, "function Read-OwnedValue { return 'original-helper' }");
        var originalHelper = File.ReadAllBytes(helper);
        var result = BuildNativeClosure(fixture, helper);
        AssertNativeClosureBodies(result, "Read-OwnedValue");
        var refused = RunNativeClosureProcess(result.ArtifactPath!, fixture.RootPath);
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("mutation-written", refused.StdOut, StringComparison.Ordinal);
        AssertNativeClosureDiagnostic("changed after payload extraction", refused.StdErr);
        Assert.DoesNotContain("mutated-source-loaded", refused.StdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("original-helper", refused.StdOut, StringComparison.Ordinal);
        Assert.Equal(originalHelper, File.ReadAllBytes(helper));
    }

    private static void AssertNativeClosureDiagnostic(
        string expected, string stderr, string? scenario = null, bool expectedPresent = true)
    {
        // PowerShell may continue its formatted error on a new gutter-prefixed line.
        // Keep every diagnostic word and its order while allowing that presentation.
        const string separator = @"(?:[ \t]+|\r?\n[ \t]*\|[ \t]*)";
        var pattern = string.Join(separator, expected.Split(' ')
            .Select(System.Text.RegularExpressions.Regex.Escape));
        var matched = System.Text.RegularExpressions.Regex.IsMatch(stderr, pattern);
        var presence = expectedPresent ? "present" : "absent";
        Assert.True(matched == expectedPresent,
            $"Case={scenario ?? "native-closure"}; expected diagnostic {presence}: {expected}{Environment.NewLine}stderr={stderr}");
    }

    private static PowerShellCompilationTargetContract CreateNativeClosureTarget()
        => PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);

    private static PowerShellCompilationBuildResult BuildNativeClosure(ArtifactFixture fixture, params string[] helpers)
    {
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath,
            "PowerForge.NativeSourceClosure", PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Hybrid)
        {
            CompilationSourcePaths = new[] { fixture.ScriptPath }.Concat(helpers).ToArray(),
            EmitSource = true,
            TargetContract = CreateNativeClosureTarget()
        };
        spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
        var result = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(File.ReadAllBytes(fixture.ScriptPath), File.ReadAllBytes(Path.Combine(result.GeneratedSourcePath!, "Source.ps1")));
        return result;
    }

    private static void AssertNativeClosureBodies(PowerShellCompilationBuildResult result, params string[] functions)
    {
        var entries = result.Manifest!.UnitDispositionLedger!.Entries;
        var root = Assert.Single(entries, static unit => unit.Name == "<script>");
        Assert.Equal(PowerShellCompilationUnitKind.Script, root.Kind);
        foreach (var name in new[] { "<script>" }.Concat(functions))
        {
            var unit = Assert.Single(entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, name);
            Assert.False(unit.RetainedHostedSource, name);
            Assert.False(unit.ShapingFallback, name);
            Assert.False(unit.Omitted, name);
            Assert.False(unit.Rejected, name);
        }
    }

    private static ProcessRunResult RunNativeClosureProcess(string executable, string cwd, params string[] arguments)
        => new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(executable, cwd, arguments,
            TimeSpan.FromSeconds(60), new Dictionary<string, string?> { ["NO_COLOR"] = "1" }))
            .GetAwaiter().GetResult();
}

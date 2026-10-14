namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_PackagedDerivedSourceChecksActualOperandAndPayloadAfterAuthoredPathCalls()
    {
        using var fixture = ArtifactFixture.Create("""
            param([string]$Case = 'clean', [string]$OutsidePath, [string]$Marker)
            $ErrorActionPreference = 'Stop'
            if ($Case -eq 'continue-shadow') { $ErrorActionPreference = 'Continue'; $Case = 'join-shadow' }
            $script:trace = [System.Collections.Generic.List[string]]::new()
            function Join-Path {
                param([string]$Path, [string]$ChildPath)
                $script:trace.Add('join')
                if ($Case -eq 'join-throw') { throw 'AUTHORED-JOIN-ERROR' }
                if ($Case -eq 'join-shadow') { return $OutsidePath }
                Microsoft.PowerShell.Management\Join-Path $Path $ChildPath
            }
            function Invoke-OtherPath {
                param([string]$Path, [string]$ChildPath)
                $script:trace.Add('alias')
                return $OutsidePath
            }
            function Test-Path {
                param([string]$LiteralPath, [string]$PathType)
                $script:trace.Add('test')
                if ($Case -eq 'test-throw') { throw 'AUTHORED-TEST-ERROR' }
                if ($Case -eq 'unchanged-path') { $script:helperPath = $LiteralPath }
                if ($Case -eq 'indirect-write') { $script:helperPath = $OutsidePath }
                if ($Case -eq 'unknown-value') { $script:helperPath = @($LiteralPath, $OutsidePath) }
                if ($Case -eq 'other-embedded') { $script:helperPath = [IO.Path]::Combine([IO.Path]::GetDirectoryName($LiteralPath), 'other.ps1') }
                if ($Case -eq 'hash-change') { [IO.File]::WriteAllText($LiteralPath, "'UNREVIEWED-HASH'") }
                if ($Case -eq 'link-change') {
                    [IO.File]::Delete($LiteralPath)
                    [void][IO.File]::CreateSymbolicLink($LiteralPath, $OutsidePath)
                }
                Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath -PathType $PathType
            }
            if ($Case -eq 'alias-shadow') { Set-Alias -Name Join-Path -Value Invoke-OtherPath }
            $helperPath = Join-Path $PSScriptRoot 'helper.ps1'
            if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) { throw 'missing helper' }
            . $helperPath -Value 'Ada' -Marker $Marker
            if ($Case -eq 'not-selected') { . "$PSScriptRoot/other.ps1" }
            "$([string]::Join(',', $script:trace))|$(Get-Value)"
            """, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "helper.ps1");
        File.WriteAllText(helper,
            "param([string]$Value, [string]$Marker); [IO.File]::WriteAllText($Marker, 'selected'); " +
            "$script:trace.Add('helper'); function Get-Value { $Value }");
        File.WriteAllText(Path.Combine(fixture.RootPath, "other.ps1"), "'UNREVIEWED-OTHER'");
        var outside = Path.Combine(fixture.RootPath, "unselected.ps1");
        // Matching bytes must not give an external path or link runtime source authority.
        File.Copy(helper, outside);
        var marker = Path.Combine(fixture.RootPath, "executed.txt");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "PowerForge.GuardedSourcePaths", resolved.Kind, resolved.Mode,
            allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            SingleFile = false
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        foreach (var scenario in new[] { "clean", "unchanged-path" })
        {
            var clean = RunProcess(result.ArtifactPath!, "--Case=" + scenario, "--OutsidePath=" + outside, "--Marker=" + marker);
            Assert.Equal((0, "join,test,helper|Ada", string.Empty),
                (clean.ExitCode, clean.StandardOutput.Trim(), clean.StandardError.Trim()));
            Assert.Equal("selected", File.ReadAllText(marker));
        }
        var cases = new List<string> { "join-shadow", "alias-shadow", "indirect-write", "unknown-value", "other-embedded", "hash-change" };
        if (!OperatingSystem.IsWindows()) cases.Add("link-change");
        foreach (var scenario in cases)
        {
            File.Delete(marker);
            var run = RunProcess(result.ArtifactPath!, "--Case=" + scenario, "--OutsidePath=" + outside, "--Marker=" + marker);
            Assert.True(run.ExitCode != 0,
                $"Case={scenario}; exit={run.ExitCode}; stdout={run.StandardOutput}; stderr={run.StandardError}");
            Assert.Contains("Packaged dot-source", run.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("UNREVIEWED", run.StandardOutput, StringComparison.Ordinal);
            Assert.False(File.Exists(marker), scenario + " executed source before its guard passed.");
        }
        File.Delete(marker);
        var continuing = RunProcess(result.ArtifactPath!, "--Case=continue-shadow", "--OutsidePath=" + outside, "--Marker=" + marker);
        Assert.Equal(0, continuing.ExitCode);
        Assert.Contains("Packaged dot-source", continuing.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("UNREVIEWED", continuing.StandardOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(marker), "Continue must not turn a rejected operand into an executed source load.");
        foreach (var scenario in new[] { "join-throw", "test-throw" })
        {
            File.Delete(marker);
            var run = RunProcess(result.ArtifactPath!, "--Case=" + scenario, "--OutsidePath=" + outside, "--Marker=" + marker);
            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains(scenario == "join-throw" ? "AUTHORED-JOIN-ERROR" : "AUTHORED-TEST-ERROR", run.StandardError, StringComparison.Ordinal);
            Assert.DoesNotContain("Packaged dot-source", run.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(marker));
        }
    }

    [Fact]
    public void Build_PackagedTransitiveDerivedSourcePreservesDependencyRootParametersAndDotScope()
    {
        using var fixture = ArtifactFixture.Create(
            "$helperPath = Join-Path $PSScriptRoot 'Private/helper.ps1'; . $helperPath -Name 'Ada'; " +
            "\"$callerValue|$(Get-LeafValue)|$([IO.Path]::GetFileName($helperRoot))\"", compactPath: true);
        var directory = Directory.CreateDirectory(Path.Combine(fixture.RootPath, "Private")).FullName;
        File.WriteAllText(Path.Combine(directory, "helper.ps1"),
            "param([string]$Name); $callerValue = $Name; $helperRoot = $PSScriptRoot; " +
            "$leafPath = Join-Path $PSScriptRoot 'leaf.ps1'; . $leafPath");
        File.WriteAllText(Path.Combine(directory, "leaf.ps1"), "function Get-LeafValue { 9 }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.ScriptPath);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            resolved.SourcePath, fixture.OutputPath, "PowerForge.TransitiveSourcePaths", resolved.Kind, resolved.Mode,
            allowUnreviewedDependencyResolution: true)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            SingleFile = false
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var run = RunProcess(result.ArtifactPath!);

        Assert.Equal((0, "Ada|9|Private", string.Empty),
            (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }
}

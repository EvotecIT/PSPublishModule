using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("return Get-Array")]
    public void Transpile_RetainsUnqualifiedEnumerationAfterImplicitOutput(string ending)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Array { [CmdletBinding()] param() return 1,2 }; function Get-Records { [CmdletBinding()] param() 'start'; " + ending + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ImplicitOutput", "net10.0");
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Array");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-Records");
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_RuntimeFreeLibraryRequiresAnOutputAbiForSequentialRecords()
    {
        using var fixture = ArtifactFixture.Create("function Get-Records { [CmdletBinding()] param() 'first'; return 'last' }");
        var typed = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_ImplicitScalarOutputPreservesContinuationAndLocalCallStreams(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Write-Output { [CmdletBinding()] param([string]$Value) return 'shadowed' }
            function Get-ConditionalRecord { [CmdletBinding()] param([bool]$Enabled) if ($Enabled) { 'record' } }
            function Get-Sequence {
                [CmdletBinding()] param([bool]$Enabled, [string]$Text, [int[]]$Items)
                'start'
                if ($Enabled) { $Text; 1 }
                foreach ($Value in $Items) { $Value }
                Get-ConditionalRecord -Enabled $Enabled
                Get-ConditionalRecord -Enabled $Enabled
                try { 'try'; throw [System.InvalidOperationException]::new('expected') } catch { 'catch' } finally { 'finally' }
                return 'end'
            }
            function Get-EarlyRecord {
                [CmdletBinding()] param([bool]$Early)
                'before'
                try { if ($Early) { return 'early' }; 'later' } finally { 'cleanup' }
                return 'end'
            }
            function Get-StopRecords {
                [CmdletBinding()] param([bool]$Enabled, [System.Text.StringBuilder]$Trace)
                try { if ($Enabled) { 'first'; 'second' } } finally { [void]$Trace.Append('cleanup') }
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ImplicitOutput", framework);
        Assert.Equal(5, typed.Methods.Length);
        Assert.All(typed.Methods, method => Assert.False(method.RequiresPowerShellCommandRegions));
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.ImplicitOutput",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.CompiledMethods);
        const string probe = """
            foreach ($enabled in $false,$true) {
                foreach ($text in $null,'','text') {
                    $records = @(Get-Sequence -Enabled $enabled -Text $text -Items (2,3))
                    '{0}:{1}' -f $records.Count,(($records | ForEach-Object { $_.GetType().Name + ':' + $_.ToString() }) -join '|')
                }
                @(Get-EarlyRecord -Early $enabled) -join '|'
            }
            $trace = [System.Text.StringBuilder]::new()
            $stopped = @(Get-StopRecords -Enabled $true -Trace $trace | Select-Object -First 1)
            'stopped:{0}:trace:{1}' -f ($stopped -join '|'),$trace.ToString()
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("11:String:start|String:text|Int32:1|Int32:2|Int32:3|String:record|String:record|String:try|String:catch|String:finally|String:end", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("before|early|cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stopped:first:trace:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.True((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()) ==
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()),
            "ORIGINAL:\n" + original.StandardOutput + "\nGENERATED:\n" + compiled.StandardOutput + "\nERROR:\n" + compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Value = Get-Records; return $Value")]
    [InlineData("$null = Get-Records; return 9")]
    [InlineData("return (Get-Records) + 1")]
    [InlineData("[void](Get-Records); return 9")]
    [InlineData("Get-Records | Out-Null; return 9")]
    public void Transpile_RetainsConsumedSuccessStreams(string body)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Records { [CmdletBinding()] param() 1; return 2 }; function Get-Consumer { [CmdletBinding()] param() " + body + " }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ImplicitOutput", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Records");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-Consumer");
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Transpile_RetainsConsumedTransitiveSuccessStreams()
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Records { [CmdletBinding()] param() 1; return 2 }
            function Get-Forwarded { [CmdletBinding()] param() return Get-Records }
            function Get-Consumer { [CmdletBinding()] param() $null = Get-Forwarded; return 9 }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "ImplicitOutput", "net10.0",
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Records");
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Forwarded");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-Consumer");
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_RetainedFinallyWarningsPreservePreferencesAndCaptureAfterStop(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            function Get-Number { [CmdletBinding()] param() return 1 }
            function Invoke-Cleanup { [CmdletBinding()] param() Write-Warning 'cleanup' }
            function Get-DirectRecords {
                [CmdletBinding()] param([bool]$Enabled)
                try { if ($Enabled) { 'first'; 'second' } } finally { Write-Warning 'cleanup' }
            }
            function Get-NestedRecords {
                [CmdletBinding()] param([bool]$Enabled)
                try { if ($Enabled) { 'first'; 'second' } } finally { Invoke-Cleanup }
            }
            """, ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "StoppedFinally", framework,
            PowerShellCompilationCapabilities.BinaryModule);
        Assert.Contains(typed.Methods, method => method.SourceName == "Get-Number");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-DirectRecords");
        Assert.DoesNotContain(typed.Methods, method => method.SourceName == "Get-NestedRecords");
        Assert.NotEmpty(typed.Diagnostics);
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "PowerForge.StoppedFinally",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        const string probe = """
            foreach ($command in 'Get-DirectRecords','Get-NestedRecords') {
                $warnings = @()
                $records = @(& $command -Enabled $true -WarningAction SilentlyContinue -WarningVariable warnings | Select-Object -First 1)
                '{0}:{1}:warnings:{2}' -f $command,($records -join '|'),($warnings -join '|')
            }
            """;
        var original = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        var compiled = RunProcess(host, "-NoProfile", "-NonInteractive", "-Command",
            "Import-Module '" + result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal) + "'; " + probe);
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("Get-DirectRecords:first:warnings:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Get-NestedRecords:first:warnings:cleanup", original.StandardOutput, StringComparison.Ordinal);
        Assert.True((original.ExitCode, original.StandardOutput.Trim(), original.StandardError.Trim()) ==
            (compiled.ExitCode, compiled.StandardOutput.Trim(), compiled.StandardError.Trim()),
            "ORIGINAL:\n" + original.StandardOutput + "\nGENERATED:\n" + compiled.StandardOutput + "\nERROR:\n" + compiled.StandardError);
    }
}

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ClrByReference_DirectObjectLocalPreservesGenericTryParseWriteback(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Convert-RefValues {
                [CmdletBinding()] param([string]$Text)
                $address = $null
                $version = $null
                $addressOk = [System.Net.IPAddress]::TryParse($Text, [ref]$address)
                $versionOk = [version]::TryParse($Text, [ref]$version)
                '{0}|{1}|{2}|{3}' -f $addressOk,$address,$versionOk,$version
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ClrByReferenceObjects",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.False(unit.RetainedHostedSource);

        const string probe = "Convert-RefValues -Text '127.0.0.1'; Convert-RefValues -Text '1.2'; Convert-RefValues -Text 'bad'";
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "clr-byref-objects-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; Import-Module $modulePath; " + probe,
            fixture.RootPath, "clr-byref-objects-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ClrByReference_UnsupportedStorageAndAliasingRemainHosted(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Test-ScopedReference {
                $script:number = 7
                [int]::TryParse('42', [ref]$script:number)
            }
            function Test-MemberReference {
                $item = [pscustomobject]@{ Value = 7 }
                [int]::TryParse('42', [ref]$item.Value)
            }
            function Test-MultipleReferences {
                $first = 7
                $second = 8
                [object]::ReferenceEquals([ref]$first, [ref]$second)
            }
            function Test-AliasedReference {
                [int]$value = 1
                $null = [object]::ReferenceEquals([ref]$value, $value++)
                $value
            }
            function Test-EarlyReference {
                [int]$value = 1
                $null = [System.Threading.Interlocked]::Exchange([ref]$value, $value++)
                $value
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ClrByReferenceFallback",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(5, result.Manifest!.UnitDispositionLedger!.Entries.Count);
        Assert.All(result.Manifest.UnitDispositionLedger.Entries, static unit =>
        {
            Assert.False(unit.EmittedClrMethod);
            Assert.True(unit.RetainedHostedSource);
        });
        const string probe = "Test-AliasedReference; Test-EarlyReference";
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "clr-byref-fallback-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "clr-byref-fallback-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ClrByReference_DirectIntLocalPreservesTryParseWriteback(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Convert-RefInt {
                [CmdletBinding()] param([string]$Text)
                $number = 7
                try {
                    $success = [int]::TryParse($Text, [ref]$number)
                    "$success|$number"
                } catch {
                    '{0}|{1}' -f $_.FullyQualifiedErrorId,$number
                }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ClrByReference",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.False(unit.RetainedHostedSource);

        const string probe = "Convert-RefInt -Text '42'; Convert-RefInt -Text 'bad'; Convert-RefInt -Text ''; Remove-Module $module.Name; $module=Import-Module $modulePath -PassThru; Convert-RefInt -Text '23'";
        var original = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "clr-byref-original");
        var compiled = RunStatementErrorProbe(host,
            "$modulePath='" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $module=Import-Module $modulePath -PassThru; " + probe,
            fixture.RootPath, "clr-byref-compiled");
        Assert.Equal(0, original.ExitCode);
        Assert.Contains("True|42", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("False|0", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal((original.ExitCode, original.StandardOutput, original.StandardError),
            (compiled.ExitCode, compiled.StandardOutput, compiled.StandardError));
    }
}

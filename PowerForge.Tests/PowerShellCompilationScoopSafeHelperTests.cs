using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(StatementErrorHosts))]
    public void ScoopSafeHelpers_PreserveOfflineOriginalBehavior(string framework, string host)
    {
        var fixtureHashes = new Dictionary<string, string>
        {
            ["Write-InstallInfo"] = "CE545F1E47D665F75FEEAE53648234B31DCCEFCACE45569565F06A3D946A1663",
            ["Test-CommandAvailable"] = "BD661F4566B7C2EA5E842A868B78210D2864CCB280AB25F8018C6FD70FDE3998",
            ["Write-DebugInfo"] = "EA7C2A7DF051487868710C578B7359D525CA6D80812C6F98D42780B4CA89935A",
            ["Test-ShouldRunInstall"] = "4EEC4EDCAABA84CE414FD8E0E8695CDA0F6B202190358A6D7C4AB6E5B259E43A"
        };
        var exitPath = FindCompleteConversionWorkflow("ScoopInstaller", "SafeHelpers", "Exit-Install.ps1");
        Assert.Equal("0B45D56C5BE70A8DA27BC44F1B0619A0532E34C8DAA729189E4DDD30F5F9C239",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exitPath))));
        var source = string.Join(Environment.NewLine,
            fixtureHashes.Select(entry =>
            {
                var path = FindCompleteConversionWorkflow("ScoopInstaller", "SafeHelpers", entry.Key + ".ps1");
                Assert.Equal(entry.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                return File.ReadAllText(path);
            }).Append(File.ReadAllText(exitPath)));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScoopSafeHelpers",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in fixtureHashes.Keys)
        {
            var entry = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(entry.EmittedClrMethod, name + ": " +
                string.Join("; ", entry.DiagnosticChain.Select(cause => cause.Message)));
        }
        var retainedExit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries,
            unit => unit.Name == "Exit-Install");
        Assert.False(retainedExit.EmittedClrMethod);
        Assert.True(retainedExit.RetainedHostedSource);

        const string probe = """
            $ErrorActionPreference = 'Stop'
            $env:CI = $null
            $env:SCOOP_NOINSTALL = $null
            $direct = Test-ShouldRunInstall 'install.ps1'
            $dotNoCi = Test-ShouldRunInstall '.'
            $env:CI = 'true'
            $dotCi = Test-ShouldRunInstall '.'
            $env:SCOOP_NOINSTALL = 'yes'
            $dotNoInstall = Test-ShouldRunInstall '.'
            $env:SCOOP_NOINSTALL = '0'
            $dotZero = Test-ShouldRunInstall '.'
            function global:CompilerSafeHelperCommand { 'owned' }
            $available = Test-CommandAvailable CompilerSafeHelperCommand
            $absent = Test-CommandAvailable CompilerMissingSafeHelperCommand
            $before = [string]$Host.UI.RawUI.ForegroundColor
            $info = @(Write-InstallInfo 'offline-only' -ForegroundColor DarkGreen)
            $after = [string]$Host.UI.RawUI.ForegroundColor
            $VerbosePreference = 'Continue'
            $verbose = @(Write-DebugInfo -BoundArgs ([ordered]@{A=1}) 4>&1 | ForEach-Object { $_.Message })
            [pscustomobject]@{
                direct = $direct; dotNoCi = $dotNoCi; dotCi = $dotCi
                dotNoInstall = $dotNoInstall; dotZero = $dotZero
                available = $available; absent = $absent
                info = $info; before = $before; after = $after
                verbose = $verbose
            } | ConvertTo-Json -Compress -Depth 5
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(result.ArtifactPath!, probe, host);
        Assert.True(original == generated,
            "Original: " + original + Environment.NewLine + "Generated: " + generated);
        using var observation = System.Text.Json.JsonDocument.Parse(original);
        var record = observation.RootElement;
        Assert.True(record.GetProperty("direct").GetBoolean());
        Assert.False(record.GetProperty("dotNoCi").GetBoolean());
        Assert.True(record.GetProperty("dotCi").GetBoolean());
        Assert.False(record.GetProperty("dotNoInstall").GetBoolean());
        Assert.True(record.GetProperty("dotZero").GetBoolean());
        Assert.True(record.GetProperty("available").GetBoolean());
        Assert.False(record.GetProperty("absent").GetBoolean());
        Assert.Equal("offline-only", Assert.Single(record.GetProperty("info").EnumerateArray()).GetString());
        Assert.Equal(record.GetProperty("before").GetString(), record.GetProperty("after").GetString());
        Assert.Contains(record.GetProperty("verbose").EnumerateArray(),
            line => line.GetString() == "-------- PSBoundParameters --------");
        Assert.Contains(record.GetProperty("verbose").EnumerateArray(),
            line => line.GetString() == "-------- Environment Variables --------");
    }
}

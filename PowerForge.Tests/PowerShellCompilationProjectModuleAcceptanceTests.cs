using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [MemberData(nameof(ModuleAcceptanceTargets))]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ModuleAcceptanceSourceEncodingMatchesNativeFileOnBothHosts(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Read-EncodingValue { 'owned' }", compactPath: true);
        var script = Path.Combine(fixture.RootPath, "encoding.ps1");
        var receipt = Path.Combine(fixture.RootPath, "encoding.json");
        const string source = "param([string] $ArtifactPath, [string] $Receipt)\n[IO.File]::WriteAllText($Receipt, 'café', [Text.Encoding]::UTF8)\n";
        var project = Path.Combine(fixture.RootPath, "encoding-project.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, framework, null, false, false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = service.Create(project, fixture.ScriptPath, "Encoding", target);
        var artifact = Assert.Single(manifest.Artifacts);
        foreach (var encoding in new System.Text.Encoding[] { new System.Text.UTF8Encoding(false),
                     new System.Text.UnicodeEncoding(false, true), System.Text.Encoding.Latin1 })
        {
            File.WriteAllBytes(script, encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray());
            artifact.ModuleTest = new PowerShellCompilationProjectModuleTest
            {
                Script = Path.GetFileName(script), Sha256 = PowerShellCompilationProjectManifestService.ComputeSha256(script),
                Parameters = new Dictionary<string, string> { ["Receipt"] = receipt }
            };
            service.Save(project, manifest);
            var captured = PowerShellCompilationProjectWorkflowService.CaptureModuleAcceptance(
                PowerShellCompilationProjectManifestService.Open(project), artifact, fixture.ScriptPath);
            var invocation = new PowerShellCompilationProjectWorkflowService.ModuleAcceptanceInvocation(
                framework == "net472" ? captured.Host : Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH") ?? captured.Host,
                captured.WorkingDirectory, captured.Payload);
            var native = await new ProcessRunner().RunAsync(new ProcessRunRequest(invocation.Host, fixture.RootPath,
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                    "-ArtifactPath", fixture.ScriptPath, "-Receipt", receipt }, TimeSpan.FromMinutes(1)));
            Assert.True(native.Succeeded, native.StdOut + native.StdErr);
            var expected = File.ReadAllBytes(receipt);
            File.Delete(receipt);
            File.WriteAllText(script, "throw 'Changed source must not execute'");
            PowerShellCompilationProjectWorkflowService.RunModuleAcceptance(invocation);
            Assert.Equal(expected, File.ReadAllBytes(receipt));
        }
    }

    [Theory]
    [MemberData(nameof(ModuleAcceptanceTargets))]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ModuleAcceptanceExecutesVerifiedSnapshotWithNativePathsAndIsolatedAutoload(string framework)
    {
        using var fixture = ArtifactFixture.Create("function Get-SnapshotValue { 'compiled' }", compactPath: true);
        var scriptPath = Path.Combine(fixture.RootPath, "snapshot O'Brien.ps1");
        var receipt = Path.Combine(fixture.RootPath, "snapshot.json");
        const string source = """
            param([string] $ArtifactPath, [string] $Receipt)
            $ErrorActionPreference = 'Stop'
            if ($env:PSModulePath -cne [IO.Path]::Combine($PSHOME, 'Modules')) { throw 'Acceptance module path is not isolated to this host.' }
            [ordered]@{ source='reviewed'; script=$PSCommandPath; root=$PSScriptRoot; artifact=$ArtifactPath } |
                ConvertTo-Json | Set-Content -LiteralPath $Receipt -Encoding UTF8
            """;
        File.WriteAllText(scriptPath, source);
        var project = Path.Combine(fixture.RootPath, "snapshot-project.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, framework, null, false, false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = service.Create(project, fixture.ScriptPath, "Snapshot", target);
        var artifact = Assert.Single(manifest.Artifacts);
        artifact.ModuleTest = new PowerShellCompilationProjectModuleTest
        {
            Script = Path.GetFileName(scriptPath), Sha256 = PowerShellCompilationProjectManifestService.ComputeSha256(scriptPath),
            Parameters = new Dictionary<string, string> { ["Receipt"] = receipt }
        };
        service.Save(project, manifest);
        Assert.Equal(2, service.Load(project).SchemaVersion);
        var invocation = PowerShellCompilationProjectWorkflowService.CaptureModuleAcceptance(
            PowerShellCompilationProjectManifestService.Open(project), artifact, fixture.ScriptPath);
        File.WriteAllText(scriptPath, "throw 'Unreviewed replacement executed'");
        PowerShellCompilationProjectWorkflowService.RunModuleAcceptance(invocation);
        var result = JsonNode.Parse(File.ReadAllText(receipt))!;
        Assert.Equal("reviewed", result["source"]!.GetValue<string>());
        Assert.Equal(scriptPath, result["script"]!.GetValue<string>());
        Assert.Equal(fixture.RootPath, result["root"]!.GetValue<string>());
        Assert.Equal(fixture.ScriptPath, result["artifact"]!.GetValue<string>());
    }

    public static IEnumerable<object[]> ModuleAcceptanceTargets()
    {
        yield return new object[] { "net10.0" };
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            yield return new object[] { "net472" };
    }

    [Theory]
    [MemberData(nameof(ModuleAcceptanceTargets))]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_ModuleAcceptanceScriptQualifiesBuiltAndInstalledArtifactsAndRejectsChangedBytes(string framework)
    {
        using var fixture = ArtifactFixture.Create("""
            if ($env:POWERFORGE_ACCEPTANCE_PREPARED -ne 'owned') { throw 'Acceptance setup is required before import.' }
            function Read-AcceptanceValue { param([string] $Value) "qualified:$Value" }
            Export-ModuleMember -Function Read-AcceptanceValue
            """, compactPath: true);
        const string expected = "Zażółć O'Brien \"quoted\"; $HOME &";
        const string directory = "data O'Brien";
        Directory.CreateDirectory(Path.Combine(fixture.RootPath, directory));
        File.WriteAllText(Path.Combine(fixture.RootPath, directory, "expected.txt"), expected);
        var scriptPath = Path.Combine(fixture.RootPath, "accept O'Brien.ps1");
        File.WriteAllText(scriptPath, """
            param([Parameter(Mandatory)][string] $ArtifactPath, [Parameter(Mandatory)][string] $Expected)
            $ErrorActionPreference = 'Stop'
            if ([IO.File]::ReadAllText((Join-Path (Get-Location) 'expected.txt')) -cne $Expected) { throw 'Working directory or argument differs.' }
            $env:POWERFORGE_ACCEPTANCE_PREPARED = 'owned'
            Import-Module -Name $ArtifactPath -Force -ErrorAction Stop
            if ((Read-AcceptanceValue -Value $Expected) -cne "qualified:$Expected") { throw 'Compiled command differs.' }
            [IO.File]::WriteAllText((Join-Path (Get-Location) 'observed.txt'), [IO.Path]::GetFullPath($ArtifactPath))
            """);
        var project = Path.Combine(fixture.RootPath, "p.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid, framework,
            runtimeIdentifier: null, selfContained: false, singleFile: false,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var manifest = service.Create(project, fixture.ScriptPath, "ModuleAcceptance", target);
        var artifact = Assert.Single(manifest.Artifacts);
        service.Save(project, manifest);
        // Use the public JSON contract so the negative control also detects a
        // reader that silently ignores the acceptance declaration.
        var json = JsonNode.Parse(File.ReadAllText(project))!;
        json["schemaVersion"] = 2;
        json["artifacts"]![0]!["moduleTest"] = new JsonObject
        {
            ["script"] = "accept O'Brien.ps1",
            ["sha256"] = PowerShellCompilationProjectManifestService.ComputeSha256(scriptPath),
            ["parameters"] = new JsonObject { ["Expected"] = expected },
            ["workingDirectory"] = directory
        };
        File.WriteAllText(project, json.ToJsonString());
        json["schemaVersion"] = 1;
        File.WriteAllText(project, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => service.Load(project));
        json["schemaVersion"] = 2;
        File.WriteAllText(project, json.ToJsonString());
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var built = Assert.Single(build.Targets).Path!;
        var observed = Path.Combine(fixture.RootPath, directory, "observed.txt");
        Assert.False(File.Exists(observed));
        AssertProjectSuccess(workflow.Test(project));
        Assert.Equal(Path.GetFullPath(built), File.ReadAllText(observed));
        var testEvidence = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.RootPath, ".powerforge", "test", artifact.Name + ".json")))!;
        Assert.Equal(2, testEvidence["schemaVersion"]!.GetValue<int>());
        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(scriptPath), testEvidence["moduleTestSha256"]!.GetValue<string>());
        AssertProjectSuccess(workflow.Pack(project));
        var install = workflow.Install(project);
        AssertProjectSuccess(install);
        var installation = Assert.Single(install.Targets).Path!;
        var installed = File.ReadAllText(observed);
        Assert.StartsWith(Path.GetFullPath(installation) + Path.DirectorySeparatorChar, installed, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Path.GetFullPath(built), installed);
        var receiptPath = Path.Combine(fixture.RootPath, ".powerforge", "build", artifact.Name + ".json");
        var before = File.ReadAllBytes(receiptPath);
        File.AppendAllText(scriptPath, "\nthrow 'Changed acceptance must not execute'\n");
        var changed = workflow.Test(project);
        Assert.False(changed.Succeeded);
        Assert.Contains("acceptance script", Assert.Single(changed.Targets).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(installed, File.ReadAllText(observed));
        Assert.Equal(before, File.ReadAllBytes(receiptPath));
    }
}

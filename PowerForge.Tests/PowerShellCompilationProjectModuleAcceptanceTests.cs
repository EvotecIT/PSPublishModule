using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
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
        json["artifacts"]![0]!["moduleTest"] = new JsonObject
        {
            ["script"] = "accept O'Brien.ps1",
            ["sha256"] = PowerShellCompilationProjectManifestService.ComputeSha256(scriptPath),
            ["arguments"] = new JsonArray("-Expected", expected),
            ["workingDirectory"] = directory
        };
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

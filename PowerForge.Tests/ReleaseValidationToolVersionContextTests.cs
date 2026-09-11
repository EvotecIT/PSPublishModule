using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationToolVersionContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolVersionContext.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationToolVersionContextTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Staged_tool_versions_reach_script_context_environment_and_command_variables(bool dotNetPublish, bool ambiguous)
    {
        var versions = ambiguous ? new[] { "1.2.3-preview1", "2.0.0" } : new[] { "1.2.3-preview1" };
        var expected = ambiguous ? "" : "1.2.3-preview1";
        var script = Payload("context.ps1", """
            $context = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT -Raw | ConvertFrom-Json
            @{ ContextVersion = [string]$context.ResolvedVersion; EnvironmentVersion = [string]$env:POWERFORGE_RELEASE_VERSION } | ConvertTo-Json -Compress
            """);
        var commandProbe = Payload("command.ps1", "param([AllowEmptyString()][string] $Version) [Console]::Out.Write('version=' + $Version)");
        var validationConfig = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Commands = [new() { Name = "Tool version command", FileName = "pwsh", TimeoutSeconds = 20,
                Arguments = ["-NoProfile", "-NonInteractive", "-File", commandProbe, "-Version", "{Version}"],
                ExpectedOutput = "version=" + expected }]
        }));
        var buildCalls = 0;
        var publishSpec = new DotNetPublishSpec { DotNet = new() { ProjectRoot = _root } };
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No package lane"),
            planTools: (_, _, _) => new() { ProjectRoot = _root, Targets = versions.Select((version, index) =>
                new PowerForgeToolReleaseTargetPlan { Name = "app" + index, Version = version }).ToArray() },
            runTools: plan => {
                buildCalls++;
                return new() { Success = true, Artefacts = plan.Targets.Select(target => new PowerForgeToolReleaseArtifactResult {
                    Target = target.Name, Version = target.Version, Runtime = "win-x64", Framework = "net10.0",
                    Flavor = PowerForgeToolReleaseFlavor.Portable, ZipPath = Payload(target.Name + ".zip", "built payload")
                }).ToArray() };
            },
            loadDotNetToolsSpec: (_, _) => (publishSpec, Path.Combine(_root, "release.json")),
            planDotNetTools: (_, _, _, _) => new() { ProjectRoot = _root, Targets = versions.Select((version, index) =>
                new DotNetPublishTargetPlan { Name = "app" + index, Version = version }).ToArray() },
            runDotNetTools: plan => {
                buildCalls++;
                return new() { Succeeded = true, Artefacts = plan.Targets.Select(target => new DotNetPublishArtefactResult {
                    Target = target.Name, Runtime = "win-x64", Framework = "net10.0", Style = DotNetPublishStyle.Portable,
                    ZipPath = Payload(target.Name + ".zip", "built payload")
                }).ToArray() };
            },
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"));
        var spec = new PowerForgeReleaseSpec {
            Tools = dotNetPublish ? new() { DotNetPublish = publishSpec } : new() { ProjectRoot = _root,
                Targets = versions.Select((_, index) => new PowerForgeToolReleaseTarget { Name = "app" + index }).ToArray() },
            Validation = new() { AfterStaging = [new() { Name = "Tool context", FilePath = script, TimeoutSeconds = 20 },
                new() { Name = "Tool commands", ConfigPath = validationConfig, TimeoutSeconds = 25 }] }
        };

        var result = service.Execute(spec, new() { ConfigPath = Payload("release.json", "{}"), ToolsOnly = true,
            StageRoot = Path.Combine(_root, "stage") });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1, buildCalls);
        var assets = result.ReleaseAssetEntries.Where(entry => entry.Category == PowerForgeReleaseAssetCategory.Tool).ToArray();
        Assert.Equal(versions, assets.Select(entry => entry.Version));
        Assert.All(assets, entry => Assert.True(File.Exists(entry.StagedPath)));
        Assert.True(File.Exists(result.ReleaseManifestPath));
        Assert.Equal(2, result.ReleaseValidations.Length);
        var contextResult = Assert.Single(result.ReleaseValidations, validation => validation.Name == "Tool context");
        Assert.True(contextResult.Succeeded, contextResult.StdErr);
        using var observed = JsonDocument.Parse(contextResult.StdOut);
        Assert.Equal(expected, observed.RootElement.GetProperty("ContextVersion").GetString());
        Assert.Equal(expected, observed.RootElement.GetProperty("EnvironmentVersion").GetString());
        var commandResult = Assert.Single(result.ReleaseValidations, validation => validation.Name == "Tool commands");
        Assert.True(commandResult.Succeeded, commandResult.StdErr);
        Assert.Equal("Tool version command", commandResult.StdOut);
    }

    private string Payload(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

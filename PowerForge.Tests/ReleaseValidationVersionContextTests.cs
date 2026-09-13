using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationVersionContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ValidationVersion", Guid.NewGuid().ToString("N"));
    public ReleaseValidationVersionContextTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("preview1", "preview1", true)]
    [InlineData("preview1", "preview2", false)]
    [InlineData("preview1", null, false)]
    public void Staged_module_validation_receives_full_release_identity(string? plannedLabel, string? artifactLabel, bool succeeds)
    {
        var moduleRoot = Directory.CreateDirectory(Path.Combine(_root, "module")).FullName;
        File.WriteAllText(Path.Combine(moduleRoot, "Example.psd1"),
            "@{ ModuleVersion = '1.2.3'; PrivateData = @{ PSData = @{ " +
            (artifactLabel is null ? "" : "Prerelease = '" + artifactLabel + "'") + " } } }");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Modules = [new() { Path = moduleRoot, Manifest = "Example.psd1" }]
        }));
        var script = Payload("context.ps1", "$context = Get-Content -LiteralPath $env:POWERFORGE_CONTEXT -Raw | ConvertFrom-Json; " +
            "@{ ContextVersion = $context.ResolvedVersion; EnvironmentVersion = $env:POWERFORGE_RELEASE_VERSION } | ConvertTo-Json -Compress");
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No package lane"),
            planTools: (_, _, _) => throw new InvalidOperationException("No tool lane"),
            runTools: _ => throw new InvalidOperationException("No tool lane"),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("No tool lane"),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("No tool lane"),
            runDotNetTools: _ => throw new InvalidOperationException("No tool lane"),
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"),
            executeModuleBuild: (_, _) => new() { ExitCode = 0 });

        var result = service.Execute(new() {
            Module = new() { RepositoryRoot = _root, ScriptPath = Payload("build.ps1", "# External build boundary"),
                ModuleVersion = "1.2.3", PreReleaseTag = plannedLabel },
            Validation = new() { AfterStaging = [new() { FilePath = script }, new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = Payload("release.json", "{}"), ModuleOnly = true,
            ModuleRunMode = ConfigurationGateMode.Build, StageRoot = Path.Combine(_root, "stage") });

        Assert.Equal(succeeds, result.Success);
        Assert.Equal(2, result.ReleaseValidations.Length);
        Assert.True(result.ReleaseValidations[0].Succeeded, result.ReleaseValidations[0].StdErr);
        using var context = JsonDocument.Parse(result.ReleaseValidations[0].StdOut);
        var expected = plannedLabel is null ? "1.2.3" : "1.2.3-" + plannedLabel;
        Assert.Equal(expected, context.RootElement.GetProperty("ContextVersion").GetString());
        Assert.Equal(expected, context.RootElement.GetProperty("EnvironmentVersion").GetString());
        Assert.Equal(succeeds, result.ReleaseValidations[1].Succeeded);
        if (succeeds) Assert.Equal("Module Example.psd1", result.ReleaseValidations[1].StdOut);
        else Assert.Contains("does not match", result.ReleaseValidations[1].StdErr);
    }

    private string Payload(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

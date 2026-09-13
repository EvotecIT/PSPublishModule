using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationOutputSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.OutputValidation.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationOutputSelectionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("Portable", "probe")]
    [InlineData("Installer", "probe")]
    [InlineData("Store", "probe")]
    [InlineData("Portable", "failing-probe")]
    [InlineData("Portable", "contract-only")]
    [InlineData("Portable", "missing-selected-tool")]
    [InlineData("Portable", "skip-tool")]
    public void Selected_packaged_outputs_do_not_require_unselected_base_tool_artifacts(string output, string mode)
    {
        var selected = Enum.Parse<PowerForgeReleaseToolOutputKind>(output);
        var config = Payload("release.json", "{}");
        var project = Payload("App.csproj", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var artifact = Payload("payload." + output.ToLowerInvariant(), "built artifact");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            CliArtifacts = new() { Target = "app", PublishConfigPath = config },
            Commands = mode == "contract-only" ? [] : [new() { Name = "Product probe", FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows() ? ["/d", "/c", "echo selected-output-{Version}"] : ["-c", "printf selected-output-{Version}"],
                ExpectedOutput = mode == "failing-probe" ? "wrong" : "selected-output-1.2.3" }]
        }));
        var plan = new DotNetPublishPlan { ProjectRoot = _root, ConfigurationInputPaths = [config], Targets = [new() {
            Name = "app", ProjectPath = project, Version = "1.2.3", Combinations = [new() {
                Runtime = "win-x64", Framework = "net10.0", Style = DotNetPublishStyle.Portable
            }]
        }] };
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No package lane"),
            planTools: (_, _, _) => throw new InvalidOperationException("No legacy lane"),
            runTools: _ => throw new InvalidOperationException("No legacy lane"),
            loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec(), config),
            planDotNetTools: (_, _, _, _) => plan,
            runDotNetTools: _ => new() {
                Succeeded = true,
                Artefacts = selected == PowerForgeReleaseToolOutputKind.Portable ? [new() {
                    Category = DotNetPublishArtefactCategory.Bundle, Target = "app", Framework = "net10.0",
                    Runtime = "win-x64", Style = DotNetPublishStyle.Portable, ZipPath = artifact
                }] : [],
                MsiBuilds = selected == PowerForgeReleaseToolOutputKind.Installer ? [new() {
                    Target = "app", Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.Portable,
                    Version = "1.2.3", OutputFiles = [artifact]
                }] : [],
                StorePackages = selected == PowerForgeReleaseToolOutputKind.Store ? [new() {
                    Target = "app", Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.Portable,
                    OutputFiles = [artifact]
                }] : []
            },
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"));

        var result = service.Execute(new() { Tools = new() { DotNetPublish = new() },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = config, ToolsOnly = true,
            ToolOutputs = mode is "missing-selected-tool" or "skip-tool" ? [selected, PowerForgeReleaseToolOutputKind.Tool] : [selected],
            SkipToolOutputs = mode == "skip-tool" ? [PowerForgeReleaseToolOutputKind.Tool] : [], StageRoot = Path.Combine(_root, "stage") });

        var shouldPass = mode is not ("failing-probe" or "missing-selected-tool");
        Assert.Equal(shouldPass, result.Success);
        var validation = Assert.Single(result.ReleaseValidations);
        Assert.Equal(shouldPass, validation.Succeeded);
        if (mode == "failing-probe") {
            Assert.Contains("Product probe", validation.StdErr);
        } else if (mode == "missing-selected-tool") {
            Assert.Contains("matrix", validation.StdErr);
        } else if (mode == "contract-only") {
            Assert.Contains("Skipped", validation.StdOut);
        } else {
            Assert.Contains("Product probe", validation.StdOut);
        }
        Assert.DoesNotContain(result.ReleaseAssetEntries, item => item.Category == PowerForgeReleaseAssetCategory.Tool);
        var payload = Assert.Single(result.ReleaseAssetEntries, item => item.Category.ToString() == output);
        Assert.Equal("built artifact", File.ReadAllText(payload.StagedPath!));
        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ReleaseManifestPath!));
        Assert.NotEmpty(manifest.RootElement.GetProperty("assetEntries").EnumerateArray());
    }

    private string Payload(string name, string text)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text);
        return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

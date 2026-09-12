using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationPostStagingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.PostStaging.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationPostStagingTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("default", true)]
    [InlineData("relative", true)]
    [InlineData("absolute", true)]
    [InlineData("literal-braces", true)]
    [InlineData("outside", false)]
    [InlineData("missing", false)]
    [InlineData("unlisted", false)]
    public void Generated_winget_assets_participate_in_the_staged_cli_contract(string mode, bool expectedSuccess)
    {
        var config = Payload("release.json", "{}");
        var project = Payload("App.csproj", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var artifact = Payload("app.zip", "built CLI artifact");
        var stage = Path.Combine(_root, mode == "literal-braces" ? "{build_id}" : "stage");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            CliArtifacts = new() { Target = "app", Runtimes = ["win-x64"], Frameworks = ["net10.0"], Styles = ["Portable"] }
        }));
        var plan = new DotNetPublishPlan { ProjectRoot = _root, ConfigurationInputPaths = [config], Targets = [new() {
            Name = "app", ProjectPath = project, Version = "1.2.3", Combinations = [new() {
                Runtime = "win-x64", Framework = "net10.0", Style = DotNetPublishStyle.Portable
            }]
        }] };
        PowerForgeReleaseValidationContext? observed = null;
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No package lane"),
            planTools: (_, _, _) => throw new InvalidOperationException("No legacy lane"),
            runTools: _ => throw new InvalidOperationException("No legacy lane"),
            loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec(), config),
            planDotNetTools: (_, _, _, _) => plan,
            runDotNetTools: _ => new() { Succeeded = true, Artefacts = [new() {
                Category = DotNetPublishArtefactCategory.Publish, Target = "app", Framework = "net10.0",
                Runtime = "win-x64", Style = DotNetPublishStyle.Portable, ZipPath = artifact
            }] },
            runReleaseValidation: (action, context, directory, token) => {
                observed = context;
                var generated = Assert.Single(context.AssetEntries, entry => entry.Source == "Winget");
                if (mode == "missing") { File.Delete(generated.Path); }
                if (mode == "unlisted") { context.StagedAssets = context.StagedAssets.Where(path => path != generated.Path).ToArray(); }
                return new PowerForgeReleaseValidationService(new NullLogger()).Run(action, context, directory, token);
            },
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"),
            submitWinget: _ => throw new InvalidOperationException("No Winget submission"));

        var result = service.Execute(new() {
            Tools = new() { DotNetPublish = new() },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] },
            Winget = new() {
                Enabled = true, Submit = false,
                OutputPath = mode switch {
                    "relative" => "manifests",
                    "absolute" => Path.Combine(stage, "manifests"),
                    "outside" => Path.Combine(_root, "outside"),
                    _ => null
                },
                InstallerUrlTemplate = "https://example.invalid/releases/{PackageVersion}/{FileName}",
                Packages = [new() {
                    PackageIdentifier = "Fixture.App", PackageVersion = "1.2.3", Publisher = "Fixture", PackageName = "App",
                    License = "MIT", ShortDescription = "CLI fixture", Installers = [new() {
                        Category = PowerForgeReleaseAssetCategory.Tool, Target = "app", Runtime = "win-x64",
                        InstallerType = "zip", NestedInstallerType = "portable", RelativeFilePath = "app.exe"
                    }]
                }]
            }
        }, new() { ConfigPath = config, ToolsOnly = true, StageRoot = stage });

        Assert.True(result.Success == expectedSuccess, result.ErrorMessage);
        Assert.NotNull(observed);
        var validation = Assert.Single(result.ReleaseValidations);
        Assert.Equal(expectedSuccess, validation.Succeeded);
        var winget = Assert.Single(result.ReleaseAssetEntries, entry => entry.Source == "Winget");
        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ReleaseManifestPath!));
        Assert.Contains(manifest.RootElement.GetProperty("assetEntries").EnumerateArray(), entry => entry.GetProperty("Path").GetString() == winget.Path);
        if (expectedSuccess) {
            Assert.Contains(winget.Path, observed.StagedAssets);
            Assert.Equal(result.ReleaseAssetEntries.Length, observed.StagedAssets.Length);
            Assert.Contains("CLI app: 1 artifacts", validation.StdOut);
            Assert.Contains("PackageIdentifier: Fixture.App", File.ReadAllText(winget.Path));
        } else if (mode == "unlisted") {
            Assert.Contains("asset set does not match", validation.StdErr);
        } else if (mode == "missing") {
            Assert.Contains("missing", validation.StdErr);
        } else {
            Assert.Contains("outside", validation.StdErr);
        }
    }

    private string Payload(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

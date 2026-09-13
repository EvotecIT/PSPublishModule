using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationTargetSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.TargetSelection.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationTargetSelectionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("other", false, false)]
    [InlineData("other", true, false)]
    [InlineData("other", true, true)]
    [InlineData("APP", false, false)]
    [InlineData("", false, false)]
    public void Targeted_release_skips_only_the_unselected_CLI_contract_and_keeps_product_probes(string selection, bool command, bool explicitMatrix)
    {
        var releasePath = Path.Combine(_root, "release.json");
        var validationPath = Path.Combine(_root, "validation.json");
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var spec = new PowerForgeReleaseSpec { Tools = new() { DotNetPublish = new() {
            DotNet = new() { ProjectRoot = _root, Restore = false, Build = false },
            Targets = new[] { "app", "other" }.Select(name => new DotNetPublishTarget {
                Name = name, ProjectPath = "App.csproj", Publish = new() {
                    Frameworks = ["net10.0"], Runtimes = ["win-x64"], Styles = [DotNetPublishStyle.Portable], UseStaging = false
                }
            }).ToArray()
        } }, Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] } };
        File.WriteAllText(releasePath, JsonSerializer.Serialize(spec));
        File.WriteAllText(validationPath, ReleaseValidationService.Serialize(new() {
            CliArtifacts = new() { Target = "app", PublishConfigPath = explicitMatrix ? null : releasePath,
                Runtimes = explicitMatrix ? ["win-x64"] : [], Frameworks = explicitMatrix ? ["net10.0"] : [], Styles = explicitMatrix ? ["Portable"] : [] },
            Commands = command ? [new() { FileName = "probe", Name = "Retained product probe", Arguments = ["{Version}"] }] : []
        }));
        var request = new PowerForgeReleaseRequest { ConfigPath = releasePath, ToolsOnly = true, PlanOnly = true,
            StageRoot = Path.Combine(_root, "stage"), Targets = selection.Length == 0 ? [] : [selection] };
        var planned = new PowerForgeReleaseService(new NullLogger()).Execute(spec, request);
        Assert.True(planned.Success, planned.ErrorMessage);
        var plan = Assert.IsType<DotNetPublishPlan>(planned.DotNetToolPlan);
        var runner = new Runner();
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No package build"),
            planTools: (_, _, _) => throw new InvalidOperationException("No legacy plan"),
            runTools: _ => throw new InvalidOperationException("No legacy build"),
            loadDotNetToolsSpec: (_, _) => (DotNetPublishConfiguration.Load(releasePath), releasePath),
            planDotNetTools: (_, _, _, _) => plan,
            runDotNetTools: effective => new() { Succeeded = true, Artefacts = effective.Targets.Select(target => {
                var zip = Path.Combine(_root, target.Name + ".zip");
                File.WriteAllText(zip, "built " + target.Name);
                return new DotNetPublishArtefactResult { Category = DotNetPublishArtefactCategory.Publish,
                    Target = target.Name, Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.Portable, ZipPath = zip };
            }).ToArray() },
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"),
            runReleaseValidation: (action, context, directory, token) => new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(action, context, directory, token));
        request.PlanOnly = false;
        var result = service.Execute(spec, request);
        Assert.True(result.Success, result.ErrorMessage);
        var validation = Assert.Single(result.ReleaseValidations);
        Assert.True(validation.Succeeded, validation.StdErr);
        if (selection == "other") {
            Assert.Contains("skip", validation.StdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("app", validation.StdOut);
            Assert.DoesNotContain(result.ReleaseAssetEntries, entry => entry.Category == PowerForgeReleaseAssetCategory.Tool && entry.Target == "app");
        } else {
            Assert.Contains("CLI app: 1 artifacts", validation.StdOut);
        }
        if (command) {
            Assert.Equal("1.2.3", Assert.Single(Assert.Single(runner.Requests).Arguments));
            Assert.Contains("Retained product probe", validation.StdOut);
        } else { Assert.Empty(runner.Requests); }
    }

    [Fact]
    public void Missing_target_without_explicit_release_selection_is_not_silently_skipped()
    {
        var path = Path.Combine(_root, "validation.json");
        File.WriteAllText(path, ReleaseValidationService.Serialize(new() { CliArtifacts = new() { Target = "typo", ManifestPath = "missing.json" } }));
        var runner = new Runner();
        var result = new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(new() { ConfigPath = path },
            new() { PublishPlan = new() { Targets = [new() { Name = "other" }] } }, _root, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.StdErr);
        Assert.Empty(runner.Requests);
    }

    private sealed class Runner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

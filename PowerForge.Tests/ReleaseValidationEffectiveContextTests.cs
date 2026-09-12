using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationEffectiveContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.EffectiveValidation.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationEffectiveContextTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("runtime")]
    [InlineData("style")]
    [InlineData("framework")]
    [InlineData("all")]
    public void Staged_validation_uses_the_real_release_planners_selected_matrix(string selection)
    {
        var (releasePath, plan) = Plan(selection);
        var entries = WriteArtifacts(plan);
        var manifest = Manifest(entries);
        var contract = new ReleaseValidationSpec { CliArtifacts = new() {
            ManifestPath = manifest, PublishConfigPath = releasePath, Target = "app"
        } };

        var result = Run(contract, new() {
            ProjectRoot = _root, PublishPlan = plan, ModuleSelected = false, PackagesSelected = false,
            StagingRoot = _root, StagedAssets = entries.Select(item => item.StagedPath!).ToArray()
        });

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal($"CLI app: {plan.Targets[0].Combinations.Length} artifacts", result.StdOut);
        Assert.True(plan.Targets[0].Combinations.Length < 8);
        Assert.Contains(releasePath, plan.ConfigurationInputPaths);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("wrong-runtime")]
    [InlineData("wrong-framework")]
    [InlineData("wrong-style")]
    [InlineData("other-config")]
    [InlineData("explicit-matrix")]
    public void Effective_plan_does_not_weaken_matrix_or_configuration_expectations(string variation)
    {
        var (releasePath, plan) = Plan("all");
        var entries = WriteArtifacts(plan).ToList();
        if (variation == "missing") entries.Clear();
        if (variation == "duplicate") entries.Add(entries[0]);
        if (variation == "wrong-runtime") entries[0].Runtime = "linux-x64";
        if (variation == "wrong-framework") entries[0].Framework = "net8.0";
        if (variation == "wrong-style") entries[0].Style = "FrameworkDependent";
        if (variation == "other-config") releasePath = Payload("other.json", File.ReadAllText(releasePath));
        var contract = new ReleaseValidationSpec { CliArtifacts = new() {
            ManifestPath = Manifest(entries),
            PublishConfigPath = releasePath, Target = "app"
        } };
        if (variation == "explicit-matrix")
        {
            contract.CliArtifacts.PublishConfigPath = null;
            contract.CliArtifacts.Runtimes = ["linux-x64"];
            contract.CliArtifacts.Frameworks = ["net10.0"];
            contract.CliArtifacts.Styles = ["Portable"];
        }

        var result = Run(contract, new() { ProjectRoot = _root, PublishPlan = plan,
            ModuleSelected = false, PackagesSelected = false, StagingRoot = _root });

        Assert.False(result.Succeeded);
        Assert.Contains("matrix", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Standalone_validation_still_requires_the_full_configured_matrix()
    {
        var (releasePath, plan) = Plan("all");
        var entries = WriteArtifacts(plan);
        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = Manifest(entries),
            PublishConfigPath = releasePath, Target = "app"
        } }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains("matrix", Assert.Single(report.Errors));
    }

    [Fact]
    public void Json_action_keeps_its_own_root_for_real_probe_execution()
    {
        var contractRoot = Directory.CreateDirectory(Path.Combine(_root, "contract-root")).FullName;
        var otherRoot = Directory.CreateDirectory(Path.Combine(_root, "other-lane")).FullName;
        File.WriteAllText(Path.Combine(contractRoot, "probe.ps1"), "(Get-Location).Path");
        var result = Run(new() { ProjectRoot = "contract-root", Commands = [new() {
            FileName = "pwsh", Arguments = ["-NoProfile", "-NonInteractive", "-File", "probe.ps1"],
            Name = "Root probe", ExpectedOutput = "{ProjectRoot}"
        }] }, new() { ProjectRoot = otherRoot });

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal(contractRoot, result.WorkingDirectory);
        Assert.Equal("Root probe", result.StdOut);
    }

    [Theory]
    [InlineData("packages")]
    [InlineData("tools")]
    [InlineData("target")]
    public void Release_context_uses_executed_lanes_and_their_resolved_roots(string selection)
    {
        var laneRoot = Directory.CreateDirectory(Path.Combine(_root, "selected-root")).FullName;
        var releasePath = Payload("release.json", "{}");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Modules = [new() { Path = "not-staged", Manifest = "Unused.psd1" }]
        }));
        PowerForgeReleaseValidationContext? observed = null;
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => new() { Success = true, RootPath = laneRoot },
            planTools: (_, _, _) => new() { ProjectRoot = laneRoot, Targets = [new() { Name = "app", Version = "1.2.3" }] },
            runTools: _ => new() { Success = true },
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("Unexpected DotNet load"),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("Unexpected DotNet plan"),
            runDotNetTools: _ => throw new InvalidOperationException("Unexpected DotNet build"),
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"),
            runReleaseValidation: (action, context, directory, token) => {
                observed = context;
                return new PowerForgeReleaseValidationService(new NullLogger()).Run(action, context, directory, token);
            });
        var result = service.Execute(new() {
            Module = new() { RepositoryRoot = Path.Combine(_root, "unselected-module") },
            Packages = new() { RootPath = Path.Combine(_root, "configured-package-root") },
            Tools = new() { ProjectRoot = Path.Combine(_root, "configured-tool-root"), Targets = [new() { Name = "app" }] },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = releasePath, StageRoot = Path.Combine(_root, "stage"),
            PackagesOnly = selection == "packages", ToolsOnly = selection == "tools",
            Targets = selection == "target" ? ["app"] : [] });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(observed);
        Assert.False(observed.ModuleSelected);
        Assert.Equal(selection == "packages", observed.PackagesSelected);
        Assert.Equal(selection != "packages", observed.ToolsSelected);
        Assert.Equal(laneRoot, observed.ProjectRoot);
        Assert.Contains("Skipped", Assert.Single(result.ReleaseValidations).StdOut);
    }

    [Fact]
    public void Effective_plan_survives_full_staging_and_validation_dispatch()
    {
        var (releasePath, plan) = Plan("all");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Modules = [new() { Path = "unselected-module", Manifest = "Unused.psd1" }],
            CliArtifacts = new() { Target = "app", PublishConfigPath = releasePath, ManifestPath = "{ReleaseManifestPath}" }
        }));
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Unselected packages"),
            planTools: (_, _, _) => throw new InvalidOperationException("Unexpected legacy plan"),
            runTools: _ => throw new InvalidOperationException("Unexpected legacy build"),
            loadDotNetToolsSpec: (_, _) => (DotNetPublishConfiguration.Load(releasePath), releasePath),
            planDotNetTools: (_, _, _, _) => plan,
            runDotNetTools: effective => new() {
                Succeeded = true,
                Artefacts = effective.Targets[0].Combinations.Select((item, index) => new DotNetPublishArtefactResult {
                    Target = "app", Runtime = item.Runtime, Framework = item.Framework, Style = item.Style,
                    ZipPath = Payload($"build-{index}.zip", "built payload")
                }).ToArray()
            },
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"));

        var result = service.Execute(new() {
            Module = new() { RepositoryRoot = Path.Combine(_root, "unselected-module") },
            Tools = new() { DotNetPublish = DotNetPublishConfiguration.Load(releasePath) },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = releasePath, Targets = ["app"], StageRoot = Path.Combine(_root, "stage") });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Same(plan, result.DotNetToolPlan);
        Assert.Null(result.ModulePlan);
        Assert.Null(result.Packages);
        var validation = Assert.Single(result.ReleaseValidations);
        Assert.True(validation.Succeeded, validation.StdErr);
        Assert.Equal("CLI app: 1 artifacts", validation.StdOut);
        Assert.True(File.Exists(result.ReleaseManifestPath));
        var asset = Assert.Single(result.ReleaseAssetEntries, item => item.Category == PowerForgeReleaseAssetCategory.Tool);
        Assert.True(File.Exists(asset.StagedPath));
        Assert.Equal("win-x64", asset.Runtime);
        Assert.Equal("net10.0", asset.Framework);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_owned_package_validation_follows_the_effective_build_selection(bool moduleOnly)
    {
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Packages = new() { Path = "not-produced", Items = [new() { Id = "Example" }] },
            Consumers = [new() { SourceDirectory = "not-produced-consumer" }],
            Tools = [new() { PackageRoot = "not-produced", PackageId = "Example.Tool", CommandName = "example" }]
        }));
        var builds = new List<bool>();
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No separate package lane"),
            planTools: (_, _, _) => throw new InvalidOperationException("No tool lane"),
            runTools: _ => throw new InvalidOperationException("No tool lane"),
            loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("No tool lane"),
            planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("No tool lane"),
            runDotNetTools: _ => throw new InvalidOperationException("No tool lane"),
            publishGitHubRelease: _ => throw new InvalidOperationException("No publication"),
            executeModuleBuild: (request, _) => {
                builds.Add(request.IncludeProjectPackages);
                return new() { ExitCode = 0 };
            });

        var result = service.Execute(new() {
            Module = new() { RepositoryRoot = _root, ScriptPath = Payload("build.ps1", "# build boundary"),
                IncludesPackages = true, ModuleVersion = "1.2.3" },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = Payload("release.json", "{}"), ModuleOnly = moduleOnly,
            ModuleRunMode = ConfigurationGateMode.Build, StageRoot = Path.Combine(_root, "stage") });

        Assert.Equal(!moduleOnly, Assert.Single(builds));
        Assert.Equal(!moduleOnly, result.ModulePlan!.IncludesProjectPackages);
        Assert.Equal(moduleOnly, result.Success);
        var validation = Assert.Single(result.ReleaseValidations);
        if (moduleOnly) Assert.Contains("Skipped", validation.StdOut);
        else Assert.Contains("not-produced", validation.StdErr);
    }

    private (string Path, DotNetPublishPlan Plan) Plan(string selection)
    {
        Payload("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var spec = new PowerForgeReleaseSpec { Tools = new() { DotNetPublish = new() {
            DotNet = new() { ProjectRoot = _root, Restore = false, Build = false },
            Targets = [new() { Name = "app", ProjectPath = "App.csproj", Publish = new() {
                Frameworks = ["net8.0", "net10.0"], Runtimes = ["win-x64", "linux-x64"],
                Styles = [DotNetPublishStyle.Portable, DotNetPublishStyle.FrameworkDependent], UseStaging = false
            } }]
        } } };
        var path = Payload("release.json", JsonSerializer.Serialize(spec));
        var result = new PowerForgeReleaseService(new NullLogger()).Execute(spec, new() {
            ConfigPath = path, PlanOnly = true, ToolsOnly = true, Targets = ["app"],
            Runtimes = selection is "runtime" or "all" ? ["win-x64"] : [],
            Styles = selection is "style" or "all" ? [DotNetPublishStyle.Portable] : [],
            Frameworks = selection is "framework" or "all" ? ["net10.0"] : []
        });
        Assert.True(result.Success, result.ErrorMessage);
        return (path, Assert.IsType<DotNetPublishPlan>(result.DotNetToolPlan));
    }

    private PowerForgeReleaseAssetEntry[] WriteArtifacts(DotNetPublishPlan plan)
        => plan.Targets[0].Combinations.Select((item, index) => new PowerForgeReleaseAssetEntry {
            Category = PowerForgeReleaseAssetCategory.Tool, Target = "app", Runtime = item.Runtime,
            Framework = item.Framework, Style = item.Style.ToString(), Version = "1.2.3", StagedPath = Payload($"artifact-{index}.zip", "payload")
        }).ToArray();

    private string Manifest(IEnumerable<PowerForgeReleaseAssetEntry> entries)
        => Payload("manifest.json", JsonSerializer.Serialize(new { assetEntries = entries.Select(item => new {
            category = item.Category.ToString(), item.Target, item.Runtime, item.Framework, item.Style, item.Version, item.StagedPath
        }) }));

    private PowerForgeReleaseValidationResult Run(ReleaseValidationSpec spec, PowerForgeReleaseValidationContext context)
        => new PowerForgeReleaseValidationService(new NullLogger()).Run(new() {
            ConfigPath = Payload("validation.json", ReleaseValidationService.Serialize(spec))
        }, context, _root, CancellationToken.None);

    private string Payload(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

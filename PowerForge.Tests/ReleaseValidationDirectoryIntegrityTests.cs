namespace PowerForge.Tests;

public sealed class ReleaseValidationDirectoryIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.DirectoryIntegrity.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationDirectoryIntegrityTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("change")]
    [InlineData("unchanged")]
    [InlineData("cancel")]
    public void Execute_Validation_preserves_module_directory_before_next_action_and_publication(string mutation)
    {
        var config = Payload("release.json", "{}");
        var moduleConfig = Payload("module.json", """
            { "SchemaVersion": 1, "Build": { "Name": "Fixture", "SourcePath": ".", "Version": "1.2.3" }, "Segments": [] }
            """);
        Payload("Fixture.psd1", "@{ ModuleVersion = '1.2.3' }");
        var project = Payload("App.csproj", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var artifact = Payload("app.zip", "original");
        var tokenFile = Payload("github-token.txt", "fixture-token");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Commands = [new() { Name = "Probe", FileName = "fixture-probe" }]
        }));
        using var cancellation = new CancellationTokenSource();
        var runner = new ProbeRunner();
        var published = new List<GitHubReleasePublishRequest>();
        var modulePublishCalls = 0;
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No packages"),
            planTools: (_, _, _) => throw new InvalidOperationException("No legacy plan"),
            runTools: _ => throw new InvalidOperationException("No legacy run"),
            loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec(), config),
            planDotNetTools: (_, _, _, _) => new() { ProjectRoot = _root, ConfigurationInputPaths = [config], Targets = [new() {
                Name = "app", ProjectPath = project, Version = "1.2.3", Combinations = [new() {
                    Runtime = "win-x64", Framework = "net10.0", Style = DotNetPublishStyle.Portable
                }]
            }] },
            runDotNetTools: _ => new() { Succeeded = true, Artefacts = [new() {
                Category = DotNetPublishArtefactCategory.Publish, Target = "app", Framework = "net10.0",
                Runtime = "win-x64", Style = DotNetPublishStyle.Portable, ZipPath = artifact
            }] },
            executeModuleBuild: (request, _) => {
                if (request.RunMode == ConfigurationGateMode.Publish) {
                    modulePublishCalls++;
                } else {
                    var nested = Directory.CreateDirectory(Path.Combine(request.StagingPath!, "nested")).FullName;
                    File.WriteAllText(Path.Combine(nested, "payload.txt"), "original");
                }
                return new() { ExitCode = 0 };
            },
            runReleaseValidation: (action, context, directory, token) => {
                runner.Probe = () => {
                    Assert.False(string.IsNullOrWhiteSpace(context.ModuleStagingPath));
                    var nested = Path.Combine(context.ModuleStagingPath!, "nested");
                    var file = Path.Combine(nested, "payload.txt");
                    if (mutation == "add") { File.WriteAllText(Path.Combine(nested, "new.txt"), "added"); }
                    else if (mutation == "delete") { File.Delete(file); }
                    else if (mutation == "change") { File.WriteAllText(file, "modified"); }
                    else if (mutation == "cancel") { cancellation.Cancel(); }
                };
                return new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(action, context, directory, token);
            },
            publishGitHubRelease: request => { published.Add(request); return new() { Succeeded = true }; });

        var spec = new PowerForgeReleaseSpec {
            Module = new() { RepositoryRoot = _root, ConfigPath = moduleConfig, ManifestPath = "Fixture.psd1" },
            Tools = new() { DotNetPublish = new() },
            GitHub = new() { Publish = true, Owner = "fixture", Repository = "fixture", TokenFilePath = tokenFile },
            Validation = new() { AfterStaging = [
                new() { Name = "First", ConfigPath = validationPath },
                new() { Name = "Second", ConfigPath = validationPath }
            ] }
        };
        var request = new PowerForgeReleaseRequest {
            ConfigPath = config, ModuleRunMode = ConfigurationGateMode.Publish,
            StageRoot = Path.Combine(_root, "stage"), CancellationToken = cancellation.Token
        };

        if (mutation == "cancel") {
            Assert.ThrowsAny<OperationCanceledException>(() => service.Execute(spec, request));
            Assert.Equal(1, runner.Calls);
            Assert.Equal(0, modulePublishCalls);
            Assert.Empty(published);
            return;
        }

        var result = service.Execute(spec, request);
        var valid = mutation == "unchanged";
        Assert.True(result.Success == valid, result.ErrorMessage);
        Assert.Equal(valid ? 2 : 1, runner.Calls);
        Assert.Equal(valid ? 2 : 1, result.ReleaseValidations.Length);
        Assert.Equal(valid ? 1 : 0, modulePublishCalls);
        if (valid) {
            Assert.All(result.ReleaseValidations, validation => Assert.True(validation.Succeeded));
            Assert.Contains(Assert.Single(published).AssetFilePaths, path => File.ReadAllText(path) == "original");
        } else {
            Assert.False(Assert.Single(result.ReleaseValidations).Succeeded);
            Assert.Contains("changed a release input", result.ErrorMessage);
            Assert.Empty(published);
        }
    }

    private sealed class ProbeRunner : IProcessRunner
    {
        internal Action Probe { get; set; } = () => { };
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            Probe();
            return Task.FromResult(new ProcessRunResult(0, "probe passed", "", request.FileName, TimeSpan.Zero, false));
        }
    }

    private string Payload(string name, string contents)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

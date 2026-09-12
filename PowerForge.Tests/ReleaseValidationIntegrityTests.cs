using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationIntegrityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Integrity.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationIntegrityTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("staged", true)]
    [InlineData("source", false)]
    [InlineData("real-staged", true)]
    [InlineData("native-output", false)]
    [InlineData("alternate", false)]
    [InlineData("external-config", false)]
    [InlineData("manifest", true)]
    [InlineData("checksums", true)]
    [InlineData("delete", true)]
    [InlineData("unchanged", true)]
    [InlineData("report", true)]
    public void Successful_command_cannot_change_release_inputs_before_publication(string mutation, bool unified)
    {
        var config = Payload("release.json", "{\"Tools\":{\"DotNetPublishConfigPath\":\"publish.json\"}}");
        var publishConfig = Payload("publish.json", "{}");
        var project = Payload("App.csproj", "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var artifact = Path.Combine(_root, "app.zip");
        using (var archive = ZipFile.Open(artifact, ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(archive.CreateEntry("app.exe").Open());
            writer.Write("original");
        }
        var originalBytes = File.ReadAllBytes(artifact);
        var nativeOutput = Payload("app.msi", "installer");
        var executable = Payload("app.exe", "executable");
        var alternate = Path.Combine(_root, "later.zip");
        var tokenFile = Payload("github-token.txt", "fixture-token");
        var validationPath = Payload("validation.json", ReleaseValidationService.Serialize(new() {
            Commands = [new() { Name = "Probe", FileName = "fixture-probe" }]
        }));
        var published = new List<GitHubReleasePublishRequest>();
        var runner = new ProbeRunner();
        PowerForgeReleaseValidationResult? probeResult = null;
        var service = new PowerForgeReleaseService(new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("No packages"),
            planTools: (_, _, _) => throw new InvalidOperationException("No legacy plan"),
            runTools: _ => throw new InvalidOperationException("No legacy run"),
            loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec(), config),
            planDotNetTools: (_, _, _, _) => new() { ProjectRoot = _root, ConfigurationInputPaths = [config, publishConfig], Targets = [new() {
                Name = "app", ProjectPath = project, Version = "1.2.3", Combinations = [new() {
                    Runtime = "win-x64", Framework = "net10.0", Style = DotNetPublishStyle.Portable
                }]
            }] },
            runDotNetTools: _ => new() { Succeeded = true, Artefacts = [new() {
                Category = DotNetPublishArtefactCategory.Publish, Target = "app", Framework = "net10.0",
                Runtime = "win-x64", Style = DotNetPublishStyle.Portable,
                ZipPath = mutation == "alternate" ? alternate : artifact,
                ExePath = mutation == "alternate" ? executable : null
            }, new() { Category = DotNetPublishArtefactCategory.Installer, Target = "app", OutputFiles = [nativeOutput] }] },
            runReleaseValidation: (action, context, directory, token) => {
                if (mutation == "real-staged") {
                    var path = Assert.Single(context.AssetEntries, entry => entry.Category == PowerForgeReleaseAssetCategory.Tool).StagedPath!;
                    var script = Payload("modify.ps1", $"[IO.File]::WriteAllText('{path.Replace("'", "''")}', 'modified')");
                    File.WriteAllText(validationPath, ReleaseValidationService.Serialize(new() {
                        Commands = [new() {
                            FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                "WindowsPowerShell", "v1.0", "powershell.exe") : "/bin/sh",
                            Arguments = OperatingSystem.IsWindows() ? ["-NoProfile", "-NonInteractive", "-File", script] :
                                ["-c", "printf modified > \"$1\"", "probe", path]
                        }]
                    }));
                    return probeResult = new PowerForgeReleaseValidationService(new NullLogger()).Run(action, context, directory, token);
                }
                runner.Probe = () => {
                    var path = mutation switch {
                        "source" => artifact,
                        "native-output" => nativeOutput,
                        "alternate" => alternate,
                        "external-config" => publishConfig,
                        "manifest" => context.ReleaseManifestPath!,
                        "checksums" => context.ReleaseChecksumsPath!,
                        "report" => Path.Combine(context.StagingRoot!, "probe-report.txt"),
                        _ => Assert.Single(context.AssetEntries, entry => entry.Category == PowerForgeReleaseAssetCategory.Tool).StagedPath!
                    };
                    if (mutation == "delete") { File.Delete(path); }
                    else if (mutation == "alternate") { File.WriteAllBytes(path, originalBytes); }
                    else if (mutation == "source") { File.AppendAllText(path, "modified"); }
                    else if (mutation != "unchanged") { File.WriteAllText(path, "modified"); }
                };
                return probeResult = new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(action, context, directory, token);
            },
            publishGitHubRelease: request => { published.Add(request); return new() { Succeeded = true }; });

        var result = service.Execute(new() {
            Tools = new() { DotNetPublish = new(), GitHub = new() {
                Publish = !unified, Owner = "fixture", Repository = "fixture", TokenFilePath = tokenFile
            } },
            GitHub = new() { Publish = unified, Owner = "fixture", Repository = "fixture", TokenFilePath = tokenFile },
            Validation = new() { AfterStaging = [new() { ConfigPath = validationPath }] }
        }, new() { ConfigPath = config, ToolsOnly = !unified, StageRoot = Path.Combine(_root, "stage") });

        var valid = mutation is "unchanged" or "report";
        Assert.NotNull(probeResult);
        Assert.True(probeResult.Succeeded, probeResult.StdErr);
        Assert.True(result.Success == valid, result.ErrorMessage);
        Assert.Equal(mutation == "real-staged" ? 0 : 1, runner.Calls);
        Assert.Equal(valid, Assert.Single(result.ReleaseValidations).Succeeded);
        if (valid) {
            var publication = Assert.Single(published);
            Assert.Contains(publication.AssetFilePaths, path => File.ReadAllBytes(path).SequenceEqual(originalBytes));
        } else {
            Assert.Empty(published);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
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

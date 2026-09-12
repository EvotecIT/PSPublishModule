using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Composition.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationCompositionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("direct")]
    [InlineData("inline")]
    [InlineData("external")]
    public void Configuration_load_preserves_all_polymorphic_installer_components(string form)
    {
        const string publish = """
            {"Installers":[{"Id":"setup","Authoring":{"Components":[
              {"Type":"File"},{"Type":"Folder"},{"Type":"RemoveFolder"},
              {"Type":"Service"},{"Type":"RegistryValue"},{"Type":"Shortcut"}
            ]}}]}
            """;
        var path = Path.Combine(_root, "publish.json");
        File.WriteAllText(path, publish);
        if (form != "direct")
        {
            path = Path.Combine(_root, "release.json");
            File.WriteAllText(path, form == "inline"
                ? "{\"Tools\":{\"DotNetPublish\":" + publish + "}}"
                : "{\"Tools\":{\"DotNetPublishConfigPath\":\"publish.json\"}}");
        }

        var spec = DotNetPublishConfiguration.Load(path);

        var installer = Assert.Single(spec.Installers);
        Assert.Equal("setup", installer.Id);
        Assert.Collection(installer.Authoring!.Components,
            item => Assert.IsType<PowerForgeInstallerFileComponent>(item),
            item => Assert.IsType<PowerForgeInstallerFolderComponent>(item),
            item => Assert.IsType<PowerForgeInstallerRemoveFolderComponent>(item),
            item => Assert.IsType<PowerForgeInstallerServiceComponent>(item),
            item => Assert.IsType<PowerForgeInstallerRegistryValueComponent>(item),
            item => Assert.IsType<PowerForgeInstallerShortcutComponent>(item));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_load_anchors_project_root_to_defining_tools_file(bool externalTools)
    {
        var definingDirectory = Directory.CreateDirectory(Path.Combine(_root, "config", "tools")).FullName;
        var projectRoot = Directory.CreateDirectory(Path.Combine(definingDirectory, "source")).FullName;
        File.WriteAllText(Path.Combine(projectRoot, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
        var path = Path.Combine(definingDirectory, "publish.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new DotNetPublishSpec {
            DotNet = new() { ProjectRoot = "source", Build = false, Restore = false },
            Targets = [new() { Name = "app", ProjectPath = "App.csproj", Publish = new() { Framework = "net10.0", Runtimes = ["win-x64"] } }]
        }));
        if (externalTools) {
            path = Path.Combine(_root, "release.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Tools = new { DotNetPublishConfigPath = "config/tools/publish.json" } }));
        }

        var loaded = DotNetPublishConfiguration.Load(path);
        var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(loaded, null);

        Assert.Equal(projectRoot, loaded.DotNet.ProjectRoot);
        Assert.Equal(Path.Combine(projectRoot, "App.csproj"), Assert.Single(plan.Targets).ProjectPath);
        Assert.Equal("1.2.3", plan.Targets[0].Version);
    }

    [Fact]
    public void Complete_imported_spec_survives_powershell_dsl_composition()
    {
        var original = new DotNetPublishSpec {
            DotNet = new() { ProjectRoot = _root, Configuration = "Release" },
            Targets = [new() { Name = "app", SupportedRuntimes = ["win-x64"] }],
            Bundles = [new() { Id = "portable", PrepareFromTarget = "app", PrimarySubdirectory = "bin" }],
            Hooks = [new() { Id = "probe", Command = "dotnet", Arguments = ["--version"], TimeoutSeconds = 42 }],
            StorePackages = [new() { Id = "store", PrepareFromTarget = "app", PackagingProjectPath = "Store/App.wapproj" }],
            SigningProfiles = new() { ["release"] = new() { Thumbprint = "1234567890", IncludeDlls = true } }
        };
        var path = Path.Combine(_root, "publish.json");
        File.WriteAllText(path, JsonSerializer.Serialize(original));
        var imported = DotNetPublishConfiguration.Load(path);
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("New-ConfigurationDotNetPublish", typeof(PSPublishModule.NewConfigurationDotNetPublishCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        runspace.SessionStateProxy.SetVariable("ImportedSpec", imported);
        using var shell = PowerShell.Create(runspace);
        shell.AddScript("New-ConfigurationDotNetPublish -Settings { $ImportedSpec }");

        var output = shell.Invoke();

        Assert.Empty(shell.Streams.Error);
        Assert.Empty(shell.Streams.Warning);
        var composed = Assert.IsType<DotNetPublishSpec>(Assert.Single(output).BaseObject);
        Assert.Equal(_root, composed.DotNet.ProjectRoot);
        Assert.Equal(new[] { "win-x64" }, Assert.Single(composed.Targets).SupportedRuntimes);
        var bundle = Assert.Single(composed.Bundles);
        Assert.Equal("portable", bundle.Id);
        Assert.Equal("bin", bundle.PrimarySubdirectory);
        var hook = Assert.Single(composed.Hooks);
        Assert.Equal("probe", hook.Id);
        Assert.Equal(new[] { "--version" }, hook.Arguments);
        Assert.Equal(42, hook.TimeoutSeconds);
        Assert.Equal("Store/App.wapproj", Assert.Single(composed.StorePackages).PackagingProjectPath);
        Assert.Equal("1234567890", composed.SigningProfiles!["release"].Thumbprint);
        Assert.True(composed.SigningProfiles["release"].IncludeDlls);
    }

    [Theory]
    [InlineData("valid", true, "")]
    [InlineData("missing-metadata", false, "build evidence")]
    [InlineData("unmatched-list", false, "asset set")]
    [InlineData("duplicate-list", false, "asset set")]
    [InlineData("unexpected-target", false, "unexpected payload or target")]
    [InlineData("wrong-framework", false, "matrix")]
    [InlineData("missing-evidence-file", false, "missing")]
    public async Task Staged_cli_requires_matching_payload_evidence_asset_set_and_framework_matrix(string variation, bool expectedSuccess, string error)
    {
        var entries = new List<object>();
        var staged = new List<string>();
        foreach (var framework in new[] { "net8.0", "net10.0" }) {
            var path = Payload(framework + ".zip");
            staged.Add(path);
            entries.Add(new { category = "Tool", target = "app", runtime = "win-x64",
                framework = variation == "wrong-framework" ? "net9.0" : framework, style = "Portable", version = "1.2.3", stagedPath = path });
        }
        if (variation != "missing-metadata") {
            var evidence = Payload("build-evidence.json");
            if (variation == "missing-evidence-file") File.Delete(evidence);
            staged.Add(evidence);
            entries.Add(new { category = "Metadata", stagedPath = evidence });
        }
        if (variation == "unmatched-list") staged[0] = Payload("undeclared.zip");
        if (variation == "duplicate-list") staged[1] = staged[0];
        if (variation == "unexpected-target") {
            var path = Payload("other.zip");
            staged.Add(path);
            entries.Add(new { category = "Tool", target = "other", runtime = "win-x64", framework = "net10.0", style = "Portable", version = "1.2.3", stagedPath = path });
        }
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { assetEntries = entries }));

        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["win-x64"], Frameworks = ["net8.0", "net10.0"], Styles = ["Portable"], ToolsOnly = true
        } }, request: new() { ProjectRoot = _root, Version = "1.2.3", StagedAssets = staged.ToArray(), Variables = new() { ["StagingRoot"] = _root } });

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) Assert.Equal("CLI app: 2 artifacts", Assert.Single(report.Checks));
        else {
            Assert.Contains(error, Assert.Single(report.Errors));
            Assert.Empty(report.Checks);
        }
    }

    [Theory]
    [InlineData("Environment")]
    [InlineData("WorkingDirectory")]
    [InlineData("PreferWindowsPowerShell")]
    public void Configuration_actions_reject_script_process_options(string option)
    {
        var action = new PowerForgeReleaseValidationAction { ConfigPath = "validation.json" };
        if (option == "Environment") action.Environment["NAME"] = "value";
        if (option == "WorkingDirectory") action.WorkingDirectory = _root;
        if (option == "PreferWindowsPowerShell") action.PreferWindowsPowerShell = true;

        var error = Assert.Throws<InvalidOperationException>(() => new PowerForgeReleaseValidationService(new NullLogger()).Run(
            action, new() { ProjectRoot = _root }, _root, CancellationToken.None));

        Assert.Contains("ConfigPath actions", error.Message);
        Assert.Contains("script process options", error.Message);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Optional_exit_code_skips_only_exit_assertion_and_still_rejects_timeout(bool ignoreExitCode, bool timeout, bool expectedSuccess)
    {
        var command = new ReleaseCommandValidation { Name = "Probe", FileName = "probe" };
        if (ignoreExitCode) command.ExpectedExitCode = null;
        var runner = new ResultRunner(new(17, "result", "diagnostic", "probe", TimeSpan.Zero, timeout));

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Commands = [command] }, request: new() { ProjectRoot = _root });

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) Assert.Equal("Probe", Assert.Single(report.Checks));
        else {
            Assert.Contains("Probe failed", Assert.Single(report.Errors));
            Assert.Empty(report.Checks);
        }
    }

    private string Payload(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "staged payload");
        return path;
    }
    private sealed class ResultRunner(ProcessRunResult result) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

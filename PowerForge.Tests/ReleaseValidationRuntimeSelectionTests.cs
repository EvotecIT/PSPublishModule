using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PowerForge.Tests;

public sealed class ReleaseValidationRuntimeSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.RuntimeSelection.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationRuntimeSelectionTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
    }

    [Fact]
    public void Supported_runtime_set_supplies_default_matrix_without_global_widening()
    {
        var spec = Spec();
        spec.DotNet.Runtimes = ["linux-x64"];
        spec.Matrix = new() { Runtimes = ["osx-arm64"] };

        var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);

        var target = Assert.Single(plan.Targets);
        Assert.Equal(new[] { "win-arm64", "win-x64" }, target.Publish.Runtimes);
        Assert.Equal(new[] { "win-arm64", "win-x64" }, target.Combinations.Select(item => item.Runtime));
        Assert.Empty(spec.Targets[0].Publish.Runtimes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supported_runtime_set_allows_explicit_narrowing(bool throughProfile)
    {
        var spec = Spec();
        if (throughProfile) spec.Profiles = [new() { Name = "selected", Default = true, Runtimes = ["win-x64"] }];
        else spec.Targets[0].Publish.Runtimes = ["win-x64"];

        var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);

        var combination = Assert.Single(Assert.Single(plan.Targets).Combinations);
        Assert.Equal("win-x64", combination.Runtime);
        Assert.Equal("net10.0", combination.Framework);
        Assert.Equal(DotNetPublishStyle.Portable, combination.Style);
        Assert.Equal(new[] { "win-x64", "win-arm64" }, spec.Targets[0].SupportedRuntimes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unsupported_runtime_is_rejected_in_direct_and_profile_selection(bool throughProfile)
    {
        var spec = Spec();
        if (throughProfile) spec.Profiles = [new() { Name = "linux", Default = true, Runtimes = ["linux-x64"] }];
        else spec.Targets[0].Publish.Runtimes = ["linux-x64"];

        var exception = Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

        Assert.Contains("does not support", exception.Message);
        Assert.Contains("linux-x64", exception.Message);
        Assert.Contains("app", exception.Message);
    }

    [Fact]
    public void Unrestricted_target_preserves_existing_global_runtime_defaults()
    {
        var spec = Spec();
        spec.Targets[0].SupportedRuntimes = [];
        spec.DotNet.Runtimes = ["linux-x64", "osx-arm64"];

        var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);

        Assert.Equal(new[] { "linux-x64", "osx-arm64" }, Assert.Single(plan.Targets).Publish.Runtimes);
        Assert.Equal(2, plan.Targets[0].Combinations.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Release_mapper_preserves_or_overrides_sign_timeout_and_workspace_root_without_mutating_defaults(bool overrideDefaults)
    {
        var originalRoot = Path.Combine(_root, "original");
        var replacementRoot = Path.Combine(_root, "replacement");
        var defaults = new PowerForgeReleaseRequest { SignTimeoutSeconds = 123, WorkspaceTestimoXRoot = originalRoot };
        var options = new PSPublishModule.PowerForgeReleaseInvocationOptions();
        if (overrideDefaults) {
            options.SignTimeoutSeconds = 456;
            options.WorkspaceTestimoXRoot = replacementRoot;
        }

        var request = PSPublishModule.PowerForgeReleaseRequestMapper.Build(Path.Combine(_root, "release.json"), defaults, options);

        Assert.NotSame(defaults, request);
        Assert.Equal(overrideDefaults ? 456 : 123, request.SignTimeoutSeconds);
        Assert.Equal(overrideDefaults ? replacementRoot : originalRoot, request.WorkspaceTestimoXRoot);
        Assert.Equal(123, defaults.SignTimeoutSeconds);
        Assert.Equal(originalRoot, defaults.WorkspaceTestimoXRoot);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, " \t")]
    [InlineData(true, " \t")]
    public void Profile_selection_does_not_erase_invalid_supported_runtimes(bool throughProfile, string? runtime)
    {
        var spec = Spec();
        spec.Targets[0].SupportedRuntimes = runtime is null ? null! : [runtime];
        spec.DotNet.Runtimes = ["linux-x64"];
        if (throughProfile) spec.Profiles = [new() { Name = "default", Default = true }];
        var error = Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
        Assert.Contains("SupportedRuntimes", error.Message);
    }

    [WindowsFact]
    public void Public_target_DSL_rejects_invalid_restrictions_before_emitting_a_target()
    {
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("New-ConfigurationDotNetTarget",
            typeof(PSPublishModule.NewConfigurationDotNetTargetCommand), null));
        using var runspace = RunspaceFactory.CreateRunspace(state);
        runspace.Open();
        foreach (var variant in new[] { "omitted", "empty", "valid", "null", "blank", "mixed" }) {
            using var shell = PowerShell.Create();
            shell.Runspace = runspace;
            shell.AddCommand("New-ConfigurationDotNetTarget").AddParameter("Name", "app")
                .AddParameter("ProjectPath", "App.csproj").AddParameter("Framework", "net10.0");
            if (variant != "omitted") shell.AddParameter("SupportedRuntimes", variant switch {
                "empty" => Array.Empty<string>(), "valid" => new[] { "win-x64" },
                "blank" => new[] { " \t" }, "mixed" => new[] { "win-x64", " " }, _ => null
            });
            if (variant is "null" or "blank" or "mixed") {
                var error = Assert.ThrowsAny<ParameterBindingException>(() => shell.Invoke());
                Assert.Contains("SupportedRuntimes", error.Message);
            } else {
                var target = Assert.IsType<DotNetPublishTarget>(Assert.Single(shell.Invoke()).BaseObject);
                Assert.False(shell.HadErrors);
                Assert.Equal(variant == "valid" ? new[] { "win-x64" } : Array.Empty<string>(), target.SupportedRuntimes);
            }
        }
    }

    private DotNetPublishSpec Spec() => new() {
        DotNet = new() { ProjectRoot = _root, Restore = false, Build = false },
        Targets = [new() {
            Name = "app", ProjectPath = "App.csproj", SupportedRuntimes = ["win-x64", "win-arm64"],
            Publish = new() { Framework = "net10.0", UseStaging = false }
        }]
    };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

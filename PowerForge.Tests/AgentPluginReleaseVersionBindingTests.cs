using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class AgentPluginReleaseVersionBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-plugin-release-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "plugin");
    private readonly Dictionary<string, string> _versions = new() { ["Example.Tool"] = "1.2.4" };

    public AgentPluginReleaseVersionBindingTests()
    {
        Directory.CreateDirectory(Source);
        File.WriteAllText(Path.Combine(Source, "plugin.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json","name":"example","version":"1.2.3"}
""");
        File.WriteAllText(Path.Combine(Source, "mcp.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"example":{"type":"stdio","command":"dotnet","args":["dnx","Example.Tool@1.2.3"]}}}
""");
        new AgentPluginPackageService().SyncCompatibility(Source);
    }

    private static ProjectVersionBinding[] Bindings() => new[]
    {
        new ProjectVersionBinding
        {
            Path = "plugin/plugin.json", Project = "Example.Tool",
            Pattern = "(?<=\"version\":\")\\d+\\.\\d+\\.\\d+", SyncAgentPluginCompatibility = true
        },
        new ProjectVersionBinding
        {
            Path = "plugin/mcp.json", Project = "Example.Tool", Pattern = @"(?<=Example\.Tool@)\d+\.\d+\.\d+"
        }
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Binding_generates_both_client_versions_and_MCP_pins_from_the_complete_plan(bool whatIf)
    {
        var before = Snapshot();
        new ProjectVersionBindingService(new NullLogger()).Apply(_root, _versions, Bindings(), whatIf);
        if (whatIf)
            AssertSnapshot(before);
        else
        {
            var result = new AgentPluginPackageService().Validate(Source, "1.2.4");
            Assert.Equal("1.2.4", result.Version);
            foreach (var file in new[] { ".claude-plugin/plugin.json", ".codex-plugin/plugin.json" })
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, file)));
                Assert.Equal("1.2.4", json.RootElement.GetProperty("version").GetString());
            }
            foreach (var file in new[] { "mcp.json", ".mcp.json", ".codex-plugin/mcp.json" })
                Assert.Contains("Example.Tool@1.2.4", File.ReadAllText(Path.Combine(Source, file)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repository_release_updates_project_and_generated_plugin_in_one_plan(bool missingClient)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "Example.Tool"));
        var project = Path.Combine(directory.FullName, "Example.Tool.csproj");
        var original = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><VersionPrefix>1.2.3</VersionPrefix></PropertyGroup></Project>";
        File.WriteAllText(project, original);
        if (missingClient) File.Delete(Path.Combine(Source, ".claude-plugin", "plugin.json"));
        var before = Snapshot();
        var result = new DotNetRepositoryReleaseService(new NullLogger()).Execute(new DotNetRepositoryReleaseSpec
        {
            RootPath = _root, IncludeProjects = new[] { "Example.Tool" }, ExpectedVersion = "1.2.4",
            UpdateVersions = true, Pack = false, VersionBindings = Bindings()
        });
        if (missingClient)
        {
            Assert.False(result.Success);
            Assert.Equal(original, File.ReadAllText(project));
            AssertSnapshot(before);
        }
        else
        {
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains("<VersionPrefix>1.2.4</VersionPrefix>", File.ReadAllText(project));
            Assert.Equal("1.2.4", new AgentPluginPackageService().Validate(Source, "1.2.4").Version);
        }
    }

    [Fact]
    public void Missing_generated_file_fails_before_canonical_files_are_changed()
    {
        File.Delete(Path.Combine(Source, ".claude-plugin", "plugin.json"));
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => new ProjectVersionBindingService(new NullLogger()).Apply(_root, _versions, Bindings(), false));
        AssertSnapshot(before);
    }

    [Fact]
    public void Binding_to_generated_content_cannot_override_the_generator()
    {
        var bindings = Bindings().Concat(new[] { new ProjectVersionBinding
        {
            Path = "plugin/.codex-plugin/plugin.json", Project = "Example.Tool", Pattern = "(?<=\"version\": \")\\d+\\.\\d+\\.\\d+", Replacement = "{Version}-different"
        }}).ToArray();
        var before = Snapshot();
        Assert.Throws<InvalidOperationException>(() => new ProjectVersionBindingService(new NullLogger()).Apply(_root, _versions, bindings, false));
        AssertSnapshot(before);
    }

    [Fact]
    public void Release_does_not_preserve_an_encoding_that_would_make_generated_files_stale()
    {
        var path = Path.Combine(Source, ".claude-plugin", "plugin.json");
        File.WriteAllText(path, File.ReadAllText(path), Encoding.Unicode);
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => new ProjectVersionBindingService(new NullLogger()).Apply(_root, _versions, Bindings(), false));
        AssertSnapshot(before);
    }

    [Fact]
    public void Invalid_binding_cannot_claim_alignment_without_changing_the_plugin_version()
    {
        var bindings = Bindings(); bindings[0].Pattern = "(?<=\"name\":\")example";
        var before = Snapshot();
        Assert.Throws<InvalidDataException>(() => new ProjectVersionBindingService(new NullLogger()).Apply(_root, _versions, bindings, false));
        AssertSnapshot(before);
    }

    [Theory]
    [InlineData("<AssemblyVersion>1.2.3</AssemblyVersion>", null)]
    [InlineData("<FileVersion>1.2.3</FileVersion>", null)]
    [InlineData("<InformationalVersion>1.2.3</InformationalVersion>", null)]
    [InlineData("<VersionPrefix>1.2.3</VersionPrefix><VersionSuffix>rc.1</VersionSuffix>", "1.2.3-rc.1")]
    [InlineData("<Version>1.2.3</Version><InformationalVersion>9.0.0</InformationalVersion>", "1.2.3")]
    [InlineData("<Version>1.2.3</Version><PackageVersion>2.0.0</PackageVersion>", "2.0.0")]
    public void Owning_package_version_uses_package_declarations_only(string declarations, string? expected)
    {
        var project = Path.Combine(_root, "Tool.csproj");
        File.WriteAllText(project, "<Project><PropertyGroup>" + declarations + "</PropertyGroup></Project>");
        Assert.Equal(expected is not null, CsprojVersionEditor.TryGetPackageVersion(project, out var version));
        Assert.Equal(expected ?? string.Empty, version);
    }

    [Fact]
    public void Package_validation_rejects_a_different_owning_release_version()
        => Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Validate(Source, "1.2.4"));

    private Dictionary<string, byte[]> Snapshot()
        => Directory.GetFiles(Source, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);

    private void AssertSnapshot(Dictionary<string, byte[]> before)
    {
        Assert.Equal(before.Count, Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Length);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

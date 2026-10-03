using System.Text.Json;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebOutputPathGuardTests
{
    [Fact]
    public void PipelineClean_PreservesConcreteWildcardCollectionSources()
    {
        WithSite(root =>
        {
            var source = Path.Combine(root, "articles", "first", "docs", "index.md");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "---\ntitle: Wildcard\n---\nWildcard source");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"collections":[{"name":"docs","input":"articles/*/docs","output":"/"}]}""");
            var result = RunCleanBuild(root, "articles/first/docs");
            Assert.False(result.Success);
            Assert.Contains("Wildcard source", File.ReadAllText(source));
        });
    }

    [Theory]
    [InlineData("dataRoot", "data")]
    [InlineData("themesRoot", "themes")]
    public void PipelineClean_PreservesDefaultInputFoldersWhenConfigValueIsEmpty(string key, string folder)
    {
        WithSite(root =>
        {
            var source = Path.Combine(root, folder, "source.json");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "{}");
            File.WriteAllText(Path.Combine(root, "site.json"), "{\"" + key + "\":\"\",\"collections\":[{\"name\":\"pages\",\"input\":\"content\",\"output\":\"/\"}]}");
            Assert.False(RunCleanBuild(root, folder).Success);
            Assert.True(File.Exists(source));
        });
    }

    [Fact]
    public void PipelineClean_PreservesVersionHubOutsideDefaultDataFolder()
    {
        WithSite(root =>
        {
            var source = Path.Combine(root, "version-hubs", "release.json");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "{}");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"versioning":{"hubPath":"version-hubs/release.json"},"collections":[{"name":"pages","input":"content","output":"/"}]}""");
            Assert.False(RunCleanBuild(root, "version-hubs").Success);
            Assert.True(File.Exists(source));
        });
    }

    [Theory]
    [InlineData(".razor")]
    [InlineData(".cshtml")]
    [InlineData(".vbhtml")]
    [InlineData(".xaml")]
    [InlineData(".axaml")]
    [InlineData(".resx")]
    [InlineData(".resw")]
    public void DotNetPublishClean_PreservesUiSourceFiles(string extension)
    {
        WithSite(root =>
        {
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project/>");
            var source = Path.Combine(root, "Pages", "Home" + extension);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "<h1>Application source</h1>");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"dotnet-publish","project":"App.csproj","out":"Pages","clean":true}]}""");
            Assert.False(WebPipelineRunner.RunPipeline(pipeline, logger: null).Success);
            Assert.True(File.Exists(source));
        });
    }

    [Theory]
    [InlineData(".")]
    [InlineData("App")]
    public void Publish_PreflightsPublishSpecAndApplicationBeforeWebsiteCleanup(string buildOutput)
    {
        WithSite(root =>
        {
            var authoring = Path.Combine(root, "authoring");
            var project = Path.Combine(authoring, "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(project, "<Project/>");
            var publish = Path.Combine(authoring, "publish.json");
            File.WriteAllText(publish, JsonSerializer.Serialize(new
            {
                build = new { config = "../site.json", @out = buildOutput, clean = true },
                publish = new { project = "App/App.csproj", @out = "published" }
            }));
            Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.HandlePublish(
                new[] { "--config", publish }, true, new WebConsoleLogger(), 1));
            Assert.True(File.Exists(publish));
            Assert.Equal("<Project/>", File.ReadAllText(project));
        });
    }

    private static PowerForge.Web.WebPipelineResult RunCleanBuild(string root, string output)
    {
        var pipeline = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipeline, JsonSerializer.Serialize(new
        {
            steps = new[] { new { task = "build", config = "site.json", @out = output, clean = true } }
        }));
        return WebPipelineRunner.RunPipeline(pipeline, logger: null);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("dotnet-publish")]
    public void PipelineClean_PreservesInheritedPipelineInput(string task)
    {
        WithSite(root =>
        {
            var inherited = Path.Combine(root, "config", "base.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inherited)!);
            File.WriteAllText(inherited, "{\"steps\":[]}");
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project/>");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                extends = "config/base.json",
                steps = new[] { new { task, config = "site.json", project = "App.csproj", @out = "config", clean = true } }
            }));
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.Contains("overlaps build source", Assert.Single(result.Steps).Message);
            Assert.Equal("{\"steps\":[]}", File.ReadAllText(inherited));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publish_GeneratedBuildOutputCanFeedOverlay(bool clean)
    {
        WithSite(root =>
        {
            var project = Path.Combine(root, "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            // Stop at dotnet's project validation after observing generation and the overlay.
            File.WriteAllText(project, "<Project/>");
            var publish = Path.Combine(root, "publish.json");
            File.WriteAllText(publish, JsonSerializer.Serialize(new
            {
                build = new { config = "site.json", @out = "Artifacts/site", clean },
                overlay = new { source = "Artifacts/site", destination = "App/wwwroot" },
                publish = new { project = "App/App.csproj", @out = "Artifacts/published", noRestore = true }
            }));
            var error = Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.HandlePublish(
                new[] { "--config", publish }, true, new WebConsoleLogger(), 1));
            Assert.Contains("dotnet", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Home", File.ReadAllText(Path.Combine(root, "App", "wwwroot", "index.html")));
        });
    }

    [Fact]
    public void PipelineClean_PreservesExternalMarkdownIncludeDependencies()
    {
        WithSite(root =>
        {
            var include = Path.Combine(root, "snippets", "intro.md");
            Directory.CreateDirectory(Path.GetDirectoryName(include)!);
            File.WriteAllText(include, "Included source");
            File.AppendAllText(Path.Combine(root, "content", "index.md"), "\n{{< include path=\"../snippets/intro.md\" >}}");
            Assert.False(RunCleanBuild(root, "snippets").Success);
            Assert.Equal("Included source", File.ReadAllText(include));
        });
    }
}

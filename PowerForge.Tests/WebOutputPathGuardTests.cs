using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebOutputPathGuardTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("content")]
    [InlineData("themes")]
    [InlineData("data")]
    [InlineData("static")]
    public void PipelineClean_PreservesSourcesWhenOutputOverlapsInput(string output)
    {
        WithSite(root =>
        {
            var directory = Path.Combine(root, output);
            Directory.CreateDirectory(directory);
            var sentinel = Path.Combine(directory, "source-sentinel.txt");
            File.WriteAllText(sentinel, "preserve source");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                steps = new[] { new { task = "build", config = "site.json", @out = output, clean = true } }
            }));

            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);

            Assert.False(result.Success);
            Assert.Contains("overlaps build source", Assert.Single(result.Steps).Message);
            Assert.Equal("preserve source", File.ReadAllText(sentinel));
            Assert.True(File.Exists(pipeline));
            Assert.True(File.Exists(Path.Combine(root, "site.json")));
            Assert.True(File.Exists(Path.Combine(root, "content", "index.md")));
        });
    }

    [Fact]
    public void Builder_RejectsSourceOutputWithoutClean_AndAllowsSeparateOutput()
    {
        WithSite(root =>
        {
            var (spec, config) = WebSiteSpecLoader.LoadWithPath(Path.Combine(root, "site.json"));
            var plan = WebSitePlanner.Plan(spec, config);
            Assert.Throws<InvalidOperationException>(() => WebSiteBuilder.Build(spec, plan, root));
            var result = WebSiteBuilder.Build(spec, plan, Path.Combine(root, "_site"));
            Assert.True(File.Exists(Path.Combine(result.OutputPath, "index.html")));
            Assert.True(File.Exists(Path.Combine(root, "content", "index.md")));
        });
    }

    [Fact]
    public void PipelineClean_ProtectsPipelineRootWhenSiteLivesElsewhere()
    {
        WithSite(root =>
        {
            var pipelineRoot = Path.Combine(root, "authoring");
            Directory.CreateDirectory(pipelineRoot);
            var pipeline = Path.Combine(pipelineRoot, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"build","config":"../site.json","out":".","clean":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.True(File.Exists(pipeline));
        });
    }

    [Fact]
    public void PipelineClean_ProtectsExternalStaticInputs()
    {
        WithSite(root =>
        {
            var assets = Path.Combine(root, "external-assets");
            Directory.CreateDirectory(assets);
            var sentinel = Path.Combine(assets, "logo.svg");
            File.WriteAllText(sentinel, "<svg/>");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"collections":[{"name":"pages","input":"content","output":"/"}],"staticAssets":[{"source":"external-assets","destination":"assets"}]}""");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"build","config":"site.json","out":"external-assets","clean":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.Equal("<svg/>", File.ReadAllText(sentinel));
        });
    }

    [Fact]
    public void DotNetPublish_RejectsProjectRootBeforeCleanupOrProcessLaunch()
    {
        WithSite(root =>
        {
            var project = Path.Combine(root, "App.csproj");
            File.WriteAllText(project, "<Project/>");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"dotnet-publish","project":"App.csproj","out":".","clean":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.Contains("overlaps build source", Assert.Single(result.Steps).Message);
            Assert.Equal("<Project/>", File.ReadAllText(project));
            Assert.Throws<InvalidOperationException>(() => WebDotNetRunner.Publish(new WebDotNetPublishOptions
            {
                ProjectPath = project, OutputPath = root
            }));
        });
    }

    private static void WithSite(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-output-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "content"));
        try
        {
            File.WriteAllText(Path.Combine(root, "content", "index.md"), "---\ntitle: Home\nslug: index\n---\nHome");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"collections":[{"name":"pages","input":"content","output":"/"}]}""");
            action(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void DotNetPublishClean_PreservesNestedSourceDirectory()
    {
        WithSite(root =>
        {
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project/>");
            var source = Path.Combine(root, "src", "Program.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            File.WriteAllText(source, "Console.WriteLine(\"Source\");");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"dotnet-publish","project":"App.csproj","out":"src","clean":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.Contains("containing project source", Assert.Single(result.Steps).Message);
            Assert.Equal("Console.WriteLine(\"Source\");", File.ReadAllText(source));
        });
    }

    [Fact]
    public void PipelineClean_PreservesInheritedConfigurationInsideOutput()
    {
        WithSite(root =>
        {
            var inherited = Path.Combine(root, "settings", "shared.json");
            Directory.CreateDirectory(Path.GetDirectoryName(inherited)!);
            File.WriteAllText(inherited, """{"baseUrl":"https://example.test"}""");
            File.WriteAllText(Path.Combine(root, "site.json"), """{"extends":"settings/shared.json","collections":[{"name":"pages","input":"content","output":"/"}]}""");
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"build","config":"site.json","out":"settings","clean":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.True(File.Exists(inherited));
        });
    }

    [Fact]
    public void PipelineClean_RejectsLinkedOutputBeforeFollowingItIntoSources()
    {
        WithSite(root =>
        {
            var output = Path.Combine(root, "_site");
            Directory.CreateSymbolicLink(output, Path.Combine(root, "content"));
            try
            {
                var pipeline = Path.Combine(root, "pipeline.json");
                File.WriteAllText(pipeline, """{"steps":[{"task":"build","config":"site.json","out":"_site","clean":true}]}""");
                var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
                Assert.False(result.Success);
                Assert.True(File.Exists(Path.Combine(root, "content", "index.md")));
            }
            finally { Directory.Delete(output); }
        });
    }
}

using System.Diagnostics;
using System.Text.Json;
using PowerForge.Web.Cli;

public sealed class WebPipelineProjectSetTests
{
    [Fact]
    public async Task Build_SharedReferenceOnceAndRefreshesChangedSourceWithPipelineCacheEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-project-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            foreach (var name in new[] { "Shared", "First", "Second" }) Directory.CreateDirectory(Path.Combine(root, name));
            File.WriteAllText(Path.Combine(root, "Shared", "Shared.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                <Target Name="RecordBuild" BeforeTargets="CoreCompile"><WriteLinesToFile File="../shared-builds.txt" Lines="build" Overwrite="false" /></Target></Project>
                """);
            var source = Path.Combine(root, "Shared", "Value.cs");
            File.WriteAllText(source, "public static class Value { public const string Text = \"first\"; }");
            foreach (var name in new[] { "First", "Second" })
            {
                File.WriteAllText(Path.Combine(root, name, name + ".csproj"), """
                    <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
                    <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup></Project>
                    """);
                File.WriteAllText(Path.Combine(root, name, "Program.cs"), "System.Console.Write(Value.Text);");
            }
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, JsonSerializer.Serialize(new
            {
                cache = true, cachePath = "cache.json", steps = new[] { new { task = "dotnet-build", projects = new[] { "First/First.csproj", "Second/Second.csproj" }, configuration = "Release", framework = "net10.0" } }
            }));
            var first = await Task.Run(() => WebPipelineRunner.RunPipeline(pipeline, logger: null));
            Assert.True(first.Success, first.Steps.FirstOrDefault()?.Message);
            Assert.Single(File.ReadAllLines(Path.Combine(root, "shared-builds.txt")));
            File.WriteAllText(source, "public static class Value { public const string Text = \"updated value\"; }");
            var updated = await Task.Run(() => WebPipelineRunner.RunPipeline(pipeline, logger: null));
            Assert.True(updated.Success, updated.Steps.FirstOrDefault()?.Message);
            Assert.False(updated.Steps[0].Cached);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(root, "shared-builds.txt")).Length);
            foreach (var name in new[] { "First", "Second" })
            {
                using var process = Process.Start(new ProcessStartInfo("dotnet")
                {
                    ArgumentList = { Path.Combine(root, name, "bin", "Release", "net10.0", name + ".dll") },
                    RedirectStandardOutput = true, UseShellExecute = false
                })!;
                var output = process.StandardOutput.ReadToEnd();
                Assert.True(process.WaitForExit(30_000));
                Assert.Equal(0, process.ExitCode);
                Assert.Equal("updated value", output);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_ProjectSetRejectsSolutionLevelRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-project-set-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"dotnet-build","projects":["First.csproj","Second.csproj"],"runtime":"win-x64"}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.False(result.Success);
            Assert.Contains("solution-level runtime identifier", result.Steps[0].Message);
            Assert.Contains("separate project build steps", result.Steps[0].Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Build_OptionalProjectSetSkipsAllMissingProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-optional-project-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pipeline = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipeline, """{"steps":[{"task":"dotnet-build","projects":["Missing.csproj","Other.csproj"],"skipIfProjectMissing":true}]}""");
            var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
            Assert.True(result.Success);
            Assert.Contains("skipped", result.Steps[0].Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

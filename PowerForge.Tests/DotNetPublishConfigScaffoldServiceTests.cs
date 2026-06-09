using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishConfigScaffoldServiceTests
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    [Fact]
    public void ResolveOutputPath_UsesWorkingDirectoryAndProjectRoot()
    {
        var root = CreateTempRoot();
        try
        {
            var service = new DotNetPublishConfigScaffoldService();
            var path = service.ResolveOutputPath(new DotNetPublishConfigScaffoldRequest
            {
                WorkingDirectory = root,
                ProjectRoot = ".\\src\\Repo",
                OutputPath = ".\\Artifacts\\publish.json"
            });

            Assert.Equal(Path.Combine(root, "src", "Repo", "Artifacts", "publish.json"), path);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Generate_NormalizesInputsBeforeScaffolding()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.Combine(root, "src", "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

            var service = new DotNetPublishConfigScaffoldService();
            var result = service.Generate(
                new DotNetPublishConfigScaffoldRequest
                {
                    WorkingDirectory = root,
                    ProjectRoot = ".",
                    ProjectPath = " src\\App\\App.csproj ",
                    TargetName = " AppTarget ",
                    Framework = " net10.0 ",
                    Runtimes = ["win-x64", " win-x64 ", "linux-x64", ""],
                    Styles = [DotNetPublishStyle.PortableCompat, DotNetPublishStyle.PortableCompat, DotNetPublishStyle.AotSize],
                    Configuration = " ",
                    OutputPath = "Artifacts\\powerforge.dotnetpublish.json"
                },
                new NullLogger());

            Assert.Equal(Path.Combine(root, "Artifacts", "powerforge.dotnetpublish.json"), result.ConfigPath);
            Assert.Equal("AppTarget", result.TargetName);
            Assert.Equal("net10.0", result.Framework);
            Assert.Equal(["win-x64", "linux-x64"], result.Runtimes);
            Assert.Equal([DotNetPublishStyle.PortableCompat, DotNetPublishStyle.AotSize], result.Styles);

            var json = File.ReadAllText(result.ConfigPath);
            var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, ReadOptions);
            Assert.NotNull(spec);
            Assert.Equal("Release", spec!.DotNet.Configuration);
            Assert.Equal(["win-x64", "linux-x64"], spec.DotNet.Runtimes);
            Assert.Equal(["win-x64", "linux-x64"], spec.Targets[0].Publish.Runtimes);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private static JsonSerializerOptions CreateReadOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

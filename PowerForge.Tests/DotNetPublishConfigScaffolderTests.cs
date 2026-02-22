using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishConfigScaffolderTests
{
    private static readonly JsonSerializerOptions ReadOptions = CreateReadOptions();

    [Fact]
    public void Generate_UsesExplicitProjectAndInferredSettings()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.Combine(root, "src", "MyApp", "MyApp.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers></PropertyGroup></Project>");

            var scaffolder = new DotNetPublishConfigScaffolder(new NullLogger());
            var result = scaffolder.Generate(new DotNetPublishConfigScaffoldOptions
            {
                ProjectRoot = root,
                ProjectPath = projectPath
            });

            Assert.True(File.Exists(result.ConfigPath));
            Assert.Equal("MyApp", result.TargetName);
            Assert.Equal("net10.0", result.Framework);
            Assert.Equal(new[] { "win-x64", "win-arm64" }, result.Runtimes);

            var json = File.ReadAllText(result.ConfigPath);
            Assert.Contains("\"$schema\": \"./Schemas/powerforge.dotnetpublish.schema.json\"", json, StringComparison.Ordinal);

            var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, ReadOptions);
            Assert.NotNull(spec);
            var target = Assert.Single(spec!.Targets);
            Assert.Equal("src/MyApp/MyApp.csproj", target.ProjectPath.Replace('\\', '/'));
            Assert.Equal("net10.0", target.Publish.Framework);
            Assert.Equal(new[] { DotNetPublishStyle.PortableCompat }, target.Publish.Styles);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Generate_Throws_WhenOutputExistsAndOverwriteDisabled()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.Combine(root, "App", "App.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

            var outputPath = Path.Combine(root, "powerforge.dotnetpublish.json");
            File.WriteAllText(outputPath, "{ }");

            var scaffolder = new DotNetPublishConfigScaffolder(new NullLogger());
            var ex = Assert.Throws<IOException>(() => scaffolder.Generate(new DotNetPublishConfigScaffoldOptions
            {
                ProjectRoot = root
            }));

            Assert.Contains("Config already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Generate_AutoSelectsProject_WhenSingleProjectExists()
    {
        var root = CreateTempRoot();
        try
        {
            var projectPath = Path.Combine(root, "Service", "Service.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net8.0</TargetFrameworks></PropertyGroup></Project>");

            var scaffolder = new DotNetPublishConfigScaffolder(new NullLogger());
            var result = scaffolder.Generate(new DotNetPublishConfigScaffoldOptions
            {
                ProjectRoot = root,
                Styles = new[] { DotNetPublishStyle.PortableSize, DotNetPublishStyle.AotSize },
                Runtimes = new[] { "linux-x64" }
            });

            Assert.Equal("Service", result.TargetName);
            Assert.Equal("net10.0", result.Framework);
            Assert.Equal(new[] { "linux-x64" }, result.Runtimes);
            Assert.Equal(new[] { DotNetPublishStyle.PortableSize, DotNetPublishStyle.AotSize }, result.Styles);

            var json = File.ReadAllText(result.ConfigPath);
            var spec = JsonSerializer.Deserialize<DotNetPublishSpec>(json, ReadOptions);
            Assert.NotNull(spec);
            Assert.Equal(new[] { DotNetPublishStyle.PortableSize, DotNetPublishStyle.AotSize }, spec!.Targets[0].Publish.Styles);
            Assert.Equal(new[] { "linux-x64" }, spec.Targets[0].Publish.Runtimes);
            Assert.Equal("net10.0", spec.Targets[0].Publish.Framework);
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
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
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

using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotnetPublisherTests
{
    [Fact]
    public void BuildPublishArguments_AppendsAdditionalRestoreSources()
    {
        var sourceA = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Feed A");
        var sourceB = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Feed B");

        var args = DotnetPublisher.BuildPublishArguments(
            projectPath: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Module.csproj"),
            versionIsolationTargets: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "PowerForge.VersionIsolation.targets"),
            configuration: "Release",
            version: "1.2.3",
            tfm: "net10.0",
            useIsolatedArtifacts: true,
            artifacts: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "artifacts"),
            maxCpuCountArgument: "-m:1",
            publishDir: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "publish"),
            restoreSources: new[] { sourceA, sourceB, sourceA });

        Assert.Contains(args, arg => string.Equals(
            arg,
            $"-p:RestoreAdditionalProjectSources={sourceA};{sourceB}",
            StringComparison.Ordinal));
        Assert.Single(args, arg => arg.StartsWith("-p:RestoreAdditionalProjectSources=", StringComparison.Ordinal));
        Assert.Contains("--no-restore", args);
        Assert.DoesNotContain("-p:BuildProjectReferences=false", args);
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:Version=", StringComparison.Ordinal));
        Assert.Contains(args, arg => arg.StartsWith("-p:PowerForgeRootVersion=1.2.3", StringComparison.Ordinal));
        Assert.Contains(args, arg => arg.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildDependencyArguments_DoesNotStampReferencedProjects()
    {
        var args = DotnetPublisher.BuildDependencyArguments(
            configuration: "Release",
            tfm: "net8.0",
            useIsolatedArtifacts: true,
            artifacts: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "artifacts"),
            maxCpuCountArgument: "-m:1",
            restoreSources: null);

        Assert.Equal("build", args[0]);
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:Version=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:AssemblyVersion=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:FileVersion=", StringComparison.Ordinal));
    }

    [Fact]
    public void Publish_StampsOnlyTheModuleProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "DotnetPublisher", Guid.NewGuid().ToString("N"));
        var dependencyDirectory = Path.Combine(root, "Dependency");
        var transitiveDirectory = Path.Combine(root, "Transitive");
        var moduleDirectory = Path.Combine(root, "Module");
        var artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(dependencyDirectory);
        Directory.CreateDirectory(transitiveDirectory);
        Directory.CreateDirectory(moduleDirectory);

        try
        {
            File.WriteAllText(Path.Combine(transitiveDirectory, "Transitive.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyVersion>4.5.6.0</AssemblyVersion><FileVersion>4.5.6.0</FileVersion></PropertyGroup><ItemGroup><None Include=\"transitive-content.txt\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(transitiveDirectory, "Transitive.cs"),
                "namespace Transitive; public sealed class Value { public string Text => \"transitive\"; }");
            File.WriteAllText(Path.Combine(transitiveDirectory, "transitive-content.txt"), "transitive publish content");
            File.WriteAllText(Path.Combine(dependencyDirectory, "Dependency.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyVersion>2.3.4.0</AssemblyVersion><FileVersion>2.3.4.0</FileVersion></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Transitive\\Transitive.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(dependencyDirectory, "Dependency.cs"),
                "namespace Dependency; public sealed class Value { public string Text => new Transitive.Value().Text; }");
            var moduleProject = Path.Combine(moduleDirectory, "Module.csproj");
            File.WriteAllText(moduleProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><AssemblyVersion>1.0.0.0</AssemblyVersion><FileVersion>1.0.0.0</FileVersion></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Dependency\\Dependency.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Module.cs"),
                "namespace Module; public sealed class Value { public string Text => new Dependency.Value().Text; }");

            var published = new DotnetPublisher(new NullLogger()).Publish(
                moduleProject,
                "Release",
                new[] { "net8.0" },
                "9.8.7",
                artifacts);
            var publishDirectory = published["net8.0"];

            Assert.Equal(new Version(9, 8, 7, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Module.dll")).Version);
            Assert.Equal(new Version(2, 3, 4, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Dependency.dll")).Version);
            Assert.Equal(new Version(4, 5, 6, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Transitive.dll")).Version);
            Assert.Equal("transitive publish content", File.ReadAllText(Path.Combine(publishDirectory, "transitive-content.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

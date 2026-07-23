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
            version: "9.8.7",
            configuration: "Release",
            tfm: "net10.0",
            useIsolatedArtifacts: true,
            artifacts: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "artifacts"),
            maxCpuCountArgument: "-m:1",
            publishDir: Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "publish"),
            restoreSources: new[] { sourceA, sourceB, sourceA },
            existingPathMap: "C:\\source=/_/PowerForge/source");

        Assert.Contains(args, arg => string.Equals(
            arg,
            $"-p:RestoreAdditionalProjectSources={sourceA};{sourceB}",
            StringComparison.Ordinal));
        Assert.Single(args, arg => arg.StartsWith("-p:RestoreAdditionalProjectSources=", StringComparison.Ordinal));
        Assert.Contains(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "Module.csproj"), args);
        Assert.DoesNotContain("-p:BuildProjectReferences=false", args);
        Assert.Contains("-p:Version=9.8.7", args);
        Assert.Contains("-p:AssemblyVersion=9.8.7", args);
        Assert.Contains("-p:FileVersion=9.8.7", args);
        Assert.Contains("-p:_GlobalPropertiesToRemoveFromProjectReferences=%3BVersion%3BAssemblyVersion%3BFileVersion", args);
        Assert.Contains("-p:ContinuousIntegrationBuild=true", args);
        Assert.Contains(
            $"-p:PathMap=C:\\source=/_/PowerForge/source%2C{Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "artifacts")}=/_/PowerForge/artifacts",
            args);
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:PublishProfile", StringComparison.Ordinal));
        Assert.DoesNotContain(args, arg => arg.StartsWith("-p:CustomAfterMicrosoftCommonTargets=", StringComparison.Ordinal));
    }

    [Fact]
    public void Publish_ProducesIdenticalBinariesAcrossIsolatedArtifactRoots()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests",
            "DotnetPublisherDeterminism",
            Guid.NewGuid().ToString("N"));
        try
        {
            static string CreateSourceTree(string sourceRoot)
            {
                var dependencyDirectory = Path.Combine(sourceRoot, "Dependency");
                var moduleDirectory = Path.Combine(sourceRoot, "Module");
                Directory.CreateDirectory(dependencyDirectory);
                Directory.CreateDirectory(moduleDirectory);
                var escapedRoot = System.Security.SecurityElement.Escape(sourceRoot);
                File.WriteAllText(
                    Path.Combine(sourceRoot, "Directory.Build.props"),
                    $"<Project><PropertyGroup Condition=\"'$(ContinuousIntegrationBuild)' == 'true' And '$(UseArtifactsOutput)' == 'true'\"><PathMap>{escapedRoot}=/_/PowerForge/source</PathMap></PropertyGroup></Project>");
                File.WriteAllText(
                    Path.Combine(dependencyDirectory, "Dependency.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Deterministic>true</Deterministic><DebugType>portable</DebugType></PropertyGroup></Project>");
                File.WriteAllText(
                    Path.Combine(dependencyDirectory, "Dependency.cs"),
                    "namespace Dependency; public sealed class Value { public string Text => \"stable\"; }");
                var moduleProject = Path.Combine(moduleDirectory, "Module.csproj");
                File.WriteAllText(
                    moduleProject,
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Deterministic>true</Deterministic><DebugType>portable</DebugType></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Dependency\\Dependency.csproj\" /></ItemGroup></Project>");
                File.WriteAllText(
                    Path.Combine(moduleDirectory, "Module.cs"),
                    "namespace Module; public sealed class Value { public string Text => new Dependency.Value().Text; }");
                return moduleProject;
            }

            var firstProject = CreateSourceTree(Path.Combine(root, "source-a"));
            var secondProject = CreateSourceTree(Path.Combine(root, "source-b"));

            var publisher = new DotnetPublisher(new NullLogger());
            var first = publisher.Publish(
                firstProject,
                "Release",
                new[] { "net8.0" },
                "1.2.3",
                Path.Combine(root, "artifacts-a"))["net8.0"];
            var second = publisher.Publish(
                secondProject,
                "Release",
                new[] { "net8.0" },
                "1.2.3",
                Path.Combine(root, "artifacts-b"))["net8.0"];

            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first, "Dependency.dll")),
                File.ReadAllBytes(Path.Combine(second, "Dependency.dll")));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first, "Module.dll")),
                File.ReadAllBytes(Path.Combine(second, "Module.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Version>1.0.0</Version><AssemblyVersion>1.0.0.0</AssemblyVersion><FileVersion>1.0.0.0</FileVersion><CustomAfterMicrosoftCommonTargets>$(MSBuildProjectDirectory)/Custom.After.targets</CustomAfterMicrosoftCommonTargets><PublishProfileFullPath>$(MSBuildProjectDirectory)/Module.pubxml</PublishProfileFullPath></PropertyGroup><ItemGroup><ProjectReference Include=\"..\\Dependency\\Dependency.csproj\" GlobalPropertiesToRemove=\"RuntimeIdentifier;SelfContained\" /></ItemGroup><Target Name=\"RecordModuleBuild\" AfterTargets=\"Build\"><WriteLinesToFile File=\"$(MSBuildProjectDirectory)/module-builds.txt\" Lines=\"build\" Overwrite=\"false\" /></Target></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Custom.After.targets"),
                "<Project><Target Name=\"PreserveCustomAfterHook\" AfterTargets=\"Publish\"><WriteLinesToFile File=\"$(PublishDir)custom-after-hook.txt\" Lines=\"custom hook preserved\" Overwrite=\"true\" /></Target></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Module.pubxml"),
                "<Project><Target Name=\"PreservePublishProfile\" AfterTargets=\"Publish\"><WriteLinesToFile File=\"$(PublishDir)publish-profile.txt\" Lines=\"publish profile preserved\" Overwrite=\"true\" /></Target></Project>");
            File.WriteAllText(Path.Combine(moduleDirectory, "Module.cs"),
                "namespace Module; public sealed class Value { public string Text => new Dependency.Value().Text; }");

            var published = new DotnetPublisher(new NullLogger()).Publish(
                Path.GetRelativePath(Environment.CurrentDirectory, moduleProject),
                "Release",
                new[] { "net8.0" },
                "9.8.7",
                artifacts);
            var publishDirectory = published["net8.0"];

            Assert.Equal(new Version(9, 8, 7, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Module.dll")).Version);
            Assert.Equal(new Version(2, 3, 4, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Dependency.dll")).Version);
            Assert.Equal(new Version(4, 5, 6, 0), AssemblyName.GetAssemblyName(Path.Combine(publishDirectory, "Transitive.dll")).Version);
            Assert.Equal("transitive publish content", File.ReadAllText(Path.Combine(publishDirectory, "transitive-content.txt")));
            Assert.Equal("custom hook preserved", File.ReadAllText(Path.Combine(publishDirectory, "custom-after-hook.txt")).Trim());
            Assert.Equal("publish profile preserved", File.ReadAllText(Path.Combine(publishDirectory, "publish-profile.txt")).Trim());
            Assert.Single(File.ReadAllLines(Path.Combine(moduleDirectory, "module-builds.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

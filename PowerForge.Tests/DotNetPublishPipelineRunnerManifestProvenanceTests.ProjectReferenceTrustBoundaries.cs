using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_ParsesManyOrdinaryProjectReferencePropertiesDeterministically()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string inputDirectory = Directory.CreateDirectory(Path.Combine(root, "inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string selectedSource = Path.Combine(inputDirectory, "Selected.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceProperties>A=1;B=2;C=3;D=4;E=5;F=6;G=7;H=8;I=9</ReferenceProperties>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(A)|$(B)|$(C)|$(D)|$(E)|$(F)|$(G)|$(H)|$(I)' == '1|2|3|4|5|6|7|8|9'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.True(
                provenance.DirtyPaths.Any(path =>
                    path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.DoesNotContain(
                provenance.DirtyReasons,
                reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksTrackedProjectReferenceOutputUnderSourceOnlyOutputRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string assetsDirectory = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string payloadPath = Path.Combine(assetsDirectory, "Library.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../assets/</OutDir>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(payloadPath, "approved payload");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(payloadPath, "modified payload");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("assets/Library.dll", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksUntrackedProjectReferencePayloadUnderArbitraryOutputRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string assetsDirectory = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string payloadPath = Path.Combine(assetsDirectory, "Payload.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Payload.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../assets/</OutDir>
                    <PayloadPath>$(OutDir)Payload.dll</PayloadPath>
                    <TargetPath>$(PayloadPath)</TargetPath>
                  </PropertyGroup>
                  <Target Name="Build" Returns="$(PayloadPath)" />
                  <Target Name="GetTargetPath" Returns="$(PayloadPath)" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nassets/\n");
            File.WriteAllText(payloadPath, "source payload");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.True(
                provenance.DirtyReasons.Any(reason =>
                    reason.Replace('\\', '/').Contains("assets/Payload.dll", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksProjectReferenceOutputUnderBroadOutputRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string payloadPath = Path.Combine(root, "Library.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../</OutDir>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(payloadPath, "approved payload");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(payloadPath, "modified payload");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("Library.dll", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksProjectReferenceOutputWhenOutputRootTraversesDirectoryLink()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        string outputLink = Path.Combine(root, "artifacts-link");
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            Directory.CreateDirectory(Path.Combine(externalRoot, "release"));
            if (!TryCreateDirectoryLink(outputLink, externalRoot))
                return;

            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../artifacts-link/release/</OutDir>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nartifacts-link/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            Assert.True(File.Exists(Path.Combine(externalRoot, "release", "Library.dll")));

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("artifacts-link", StringComparison.OrdinalIgnoreCase) &&
                          reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(outputLink); } catch { }
            DeleteTestRepository(root);
            try { Directory.Delete(externalRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksGeneratedProjectReferenceOutputWithMultipleHardLinks()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string inputsDirectory = Directory.CreateDirectory(Path.Combine(root, "inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string trackedPayload = Path.Combine(inputsDirectory, "Tracked.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(trackedPayload, "approved payload");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            string outputPath = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            Assert.True(File.Exists(outputPath));
            File.Delete(outputPath);
            TestFileLink.CreateHardLink(outputPath, trackedPayload);
            File.WriteAllText(trackedPayload, "modified payload");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

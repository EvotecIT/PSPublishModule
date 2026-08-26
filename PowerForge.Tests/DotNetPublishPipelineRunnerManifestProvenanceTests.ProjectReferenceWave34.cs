using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadSourceProvenance_RejectsGeneratedOutputCopiedFromAbsoluteConfiguredInput(
        bool useEnvironmentVariable)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(libraryDirectory, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string payloadPath = Path.Combine(cacheDirectory, "payload.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ReplaceOutputFromAbsoluteProperty"
                          AfterTargets="Rebuild"
                          Condition="Exists('$(PayloadPath)')">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            File.Copy(libraryOutput, payloadPath);
            File.AppendAllText(payloadPath, "ignored absolute-property payload");
            RunDotNet(
                root,
                $"build \"{libraryProject}\" -c Release --no-restore --nologo -t:Rebuild -p:PayloadPath=\"{payloadPath}\"");
            Assert.Equal(File.ReadAllBytes(payloadPath), File.ReadAllBytes(libraryOutput));

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };
            if (useEnvironmentVariable)
                plan.EnvironmentVariables["PayloadPath"] = payloadPath;
            else
                plan.MsBuildProperties["PayloadPath"] = payloadPath;

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => useEnvironmentVariable
                    ? reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal)
                    : reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RemapPreservesTrackedAbsolutePropertyInput()
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
            string payloadPath = Path.Combine(inputsDirectory, "payload.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ReplaceOutputFromTrackedProperty"
                          AfterTargets="Build"
                          Condition="Exists('$(PayloadPath)')">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            File.Copy(libraryOutput, payloadPath);
            RunGit(root, "add inputs/payload.dll");
            RunGit(root, "commit -m \"approve generated payload\"");
            RunDotNet(
                root,
                $"build \"{libraryProject}\" -c Release --no-restore --nologo -t:Rebuild -p:PayloadPath=\"{payloadPath}\"");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };
            plan.MsBuildProperties["PayloadPath"] = payloadPath;

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RebuildsTransitiveReferencesInsideControlledCheckout()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string middleDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Middle")).FullName;
            string leafDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Leaf")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string middleProject = Path.Combine(middleDirectory, "Middle.csproj");
            string leafProject = Path.Combine(leafDirectory, "Leaf.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Middle/Middle.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(middleProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Leaf/Leaf.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                leafProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(
                Path.Combine(middleDirectory, "Middle.cs"),
                "public static class Middle { public static int Value => Leaf.Value; }");
            File.WriteAllText(
                Path.Combine(leafDirectory, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{middleProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

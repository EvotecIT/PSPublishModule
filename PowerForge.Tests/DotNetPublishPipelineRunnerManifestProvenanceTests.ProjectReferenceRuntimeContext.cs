using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_BuildPublishIgnoresPreexistingProjectReferenceOutputFromAnotherVersionContext()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and dependency locks\"");
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release --no-restore --nologo -p:Version=0.1.0");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = false,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Sign = new DotNetPublishSignOptions { Enabled = true },
                            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Version"] = "27.0.9742"
                            }
                        },
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

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
            Assert.True(File.Exists(Path.Combine(
                libraryDirectory,
                "bin",
                "Release",
                "net8.0",
                "Library.dll")));

            plan.MsBuildProperties["BuildProjectReferences"] = "false";

            DotNetPublishPipelineRunner.SourceProvenance prebuiltProvenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(prebuiltProvenance.Dirty);
            Assert.Contains(prebuiltProvenance.DirtyReasons, reason =>
                reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void GeneratedProjectReferenceOutputProofs_RunOnlyWhenPublishConsumesPrebuiltOutputs()
    {
        var target = new DotNetPublishTargetPlan
        {
            Name = "App",
            ProjectPath = Path.GetFullPath(Path.Combine("src", "App", "App.csproj")),
            Publish = new DotNetPublishPublishOptions
            {
                Sign = new DotNetPublishSignOptions { Enabled = true }
            }
        };
        var combination = new DotNetPublishTargetCombination
        {
            Framework = "net8.0",
            Runtime = "win-x64",
            Style = DotNetPublishStyle.FrameworkDependent
        };
        var plan = new DotNetPublishPlan
        {
            NoBuildInPublish = false
        };

        Assert.False(DotNetPublishPipelineRunner.PublishConsumesPrebuiltProjectReferenceOutputs(
            plan,
            target,
            combination));
        Assert.False(DotNetPublishPipelineRunner.RequiresPrebuiltProjectReferenceOutputProof(
            plan,
            target,
            combination));

        plan.MsBuildProperties["BuildProjectReferences"] = "false";

        Assert.True(DotNetPublishPipelineRunner.PublishConsumesPrebuiltProjectReferenceOutputs(
            plan,
            target,
            combination));

        plan.MsBuildProperties["BuildProjectReferences"] = "true";
        target.Publish.MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BuildProjectReferences"] = "false"
        };

        Assert.True(DotNetPublishPipelineRunner.PublishConsumesPrebuiltProjectReferenceOutputs(
            plan,
            target,
            combination));

        target.Publish.MsBuildProperties = null;
        target.Publish.StyleOverrides = new Dictionary<string, DotNetPublishStyleOverride>(
            StringComparer.OrdinalIgnoreCase)
        {
            [DotNetPublishStyle.FrameworkDependent.ToString()] = new DotNetPublishStyleOverride
            {
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildProjectReferences"] = "false"
                }
            }
        };

        Assert.True(DotNetPublishPipelineRunner.PublishConsumesPrebuiltProjectReferenceOutputs(
            plan,
            target,
            combination));

        target.Publish.StyleOverrides = null;
        plan.MsBuildProperties.Clear();

        plan.NoBuildInPublish = true;

        Assert.True(DotNetPublishPipelineRunner.PublishConsumesPrebuiltProjectReferenceOutputs(
            plan,
            target,
            combination));
        Assert.True(DotNetPublishPipelineRunner.RequiresPrebuiltProjectReferenceOutputProof(
            plan,
            target,
            combination));
    }

    [Theory]
    [InlineData("ProjectProperty")]
    [InlineData("EnvironmentProperty")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_BuildPublishProvesEvaluatedProjectReferenceSuppression(string suppressionMode)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string projectProperty = suppressionMode == "ProjectProperty"
                ? "<BuildProjectReferences>false</BuildProjectReferences>"
                : string.Empty;
            File.WriteAllText(appProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    {projectProperty}
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and dependency locks\"");
            RunDotNet(
                root,
                $"build \"{libraryProject}\" -c Release --no-restore --nologo -p:Version=0.1.0");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = false,
                EnvironmentVariables = suppressionMode == "EnvironmentProperty"
                    ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["BuildProjectReferences"] = "false"
                    }
                    : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Sign = new DotNetPublishSignOptions { Enabled = true },
                            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Version"] = "27.0.9742"
                            }
                        },
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

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyReasons, reason =>
                reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_BuildPublishRejectsPrebuiltProjectReferenceOutputReplacement()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string outputDirectory = Path.Combine(root, "publish");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and dependency locks\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release -f net8.0 --no-restore --nologo " +
                $"/p:SourceRevisionId={revision} " +
                "/p:IncludeSourceRevisionInInformationalVersion=true");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            Assert.True(File.Exists(libraryOutput), libraryOutput);
            byte[] provenBytes = File.ReadAllBytes(libraryOutput);
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = false,
                NoRestoreInPublish = true,
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildProjectReferences"] = "false"
                },
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Publish = new DotNetPublishPublishOptions
                        {
                            OutputPath = outputDirectory,
                            Style = DotNetPublishStyle.FrameworkDependent,
                            Sign = new DotNetPublishSignOptions { Enabled = true }
                        },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Runtime = string.Empty,
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = string.Empty,
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                ]
            };
            var runner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new RestoringProjectReferenceOutputRunner(libraryOutput, provenBytes));

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.False(result.Succeeded);
            string errorMessage = result.ErrorMessage ?? string.Empty;
            Assert.True(
                errorMessage.Contains(
                    "Release source changed after planning",
                    StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains(
                    "cannot access the file",
                    StringComparison.OrdinalIgnoreCase),
                errorMessage);
            Assert.Equal(provenBytes, File.ReadAllBytes(libraryOutput));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

}

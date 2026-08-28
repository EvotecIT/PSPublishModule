using System.Reflection;
using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void Run_NoBuildPublishRejectsMutableDirectPostPublishCopy()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DotNetPublishResult result = RunNoBuildProjectReferenceSnapshotScenario(
                root,
                """
                <Target Name="OverwritePublishedApp" AfterTargets="CopyFilesToPublishDirectory">
                  <Copy SourceFiles="../Library/bin/Release/net8.0/Library.dll" DestinationFolder="$(PublishDir)" />
                </Target>
                """,
                out string outputDirectory,
                out byte[] provenLibraryBytes,
                out string libraryOutput);

            bool publishedProvenBytes = File.Exists(Path.Combine(outputDirectory, "Library.dll")) &&
                provenLibraryBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(outputDirectory, "Library.dll")));
            Assert.True(
                !result.Succeeded || publishedProvenBytes,
                result.ErrorMessage);
            Assert.Equal(provenLibraryBytes, File.ReadAllBytes(libraryOutput));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void PublishProvenanceLease_WatchesOnlyGuardedParentDirectories()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            File.WriteAllText(projectPath, "<Project />");

            using DotNetPublishPipelineRunner.PublishProvenanceLease lease =
                DotNetPublishPipelineRunner.PublishProvenanceLease.Create([projectPath]);
            FieldInfo watchersField = typeof(DotNetPublishPipelineRunner.PublishProvenanceLease)
                .GetField("_watchers", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var watchers = Assert.IsType<List<FileSystemWatcher>>(watchersField.GetValue(lease));

            FileSystemWatcher watcher = Assert.Single(watchers);
            Assert.False(watcher.IncludeSubdirectories);
            Assert.Equal(
                Path.GetFullPath(projectDirectory),
                Path.GetFullPath(watcher.Path),
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void PublishProvenanceLease_IncludesNoBuildPublishOutputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            string outputPath = Path.Combine(root, "bin", "Library.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(outputPath, "controlled-library");
            var output = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                outputPath,
                "Library.dll",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath))));

            string[] guardedPaths = DotNetPublishPipelineRunner.PublishProvenanceLease.BuildGuardedPaths(
                [projectPath],
                [output],
                includeNoBuildPublishInputs: true);

            StringComparer comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            Assert.Contains(projectPath, guardedPaths, comparer);
            Assert.Contains(outputPath, guardedPaths, comparer);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_PreservesOriginalSourceBasename()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(),
                sha256);

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            string snapshotFile = Assert.Single(Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories));

            Assert.Equal("Library.dll", Path.GetFileName(snapshotFile));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static DotNetPublishResult RunNoBuildProjectReferenceSnapshotScenario(
        string root,
        string targetXml,
        out string outputDirectory,
        out byte[] provenLibraryBytes,
        out string libraryOutput)
    {
        RunGit(root, "init");
        RunGit(root, "config user.name \"PowerForge Tests\"");
        RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
        string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
        string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
        string appProject = Path.Combine(appDirectory, "App.csproj");
        string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
        outputDirectory = Path.Combine(root, "publish");
        File.WriteAllText(
            appProject,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
              {{targetXml}}
            </Project>
            """);
        File.WriteAllText(
            libraryProject,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(appDirectory, "Program.cs"),
            "internal static class Program { private static void Main() { _ = Library.Value; } }");
        File.WriteAllText(
            Path.Combine(libraryDirectory, "Library.cs"),
            "public static class Library { public const int Value = 1; }");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish/\n");
        RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
        RunGit(root, "add .");
        RunGit(root, "commit -m \"approved source\"");
        string revision = RunGit(root, "rev-parse HEAD").Trim();
        RunDotNet(
            root,
            $"build \"{appProject}\" -c Release -f net8.0 --no-restore --nologo " +
            $"/p:SourceRevisionId={revision} " +
            "/p:IncludeSourceRevisionInInformationalVersion=true");
        libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
        provenLibraryBytes = File.ReadAllBytes(libraryOutput);
        var plan = new DotNetPublishPlan
        {
            ProjectRoot = root,
            Configuration = "Release",
            SourceRevision = revision,
            NoBuildInPublish = true,
            NoRestoreInPublish = true,
            Targets =
            [
                new DotNetPublishTargetPlan
                {
                    Name = "App",
                    ProjectPath = appProject,
                    Publish = new DotNetPublishPublishOptions
                    {
                        OutputPath = outputDirectory,
                        Style = DotNetPublishStyle.FrameworkDependent
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
            new RestoringProjectReferenceOutputRunner(libraryOutput, provenLibraryBytes));
        return runner.Run(plan, progress: null);
    }
}

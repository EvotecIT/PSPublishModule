using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void CachedProvenance_AllowsTrackedManifestOutputsWrittenByThePipeline()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string manifestJson = Path.Combine(root, "manifest.json");
            string manifestText = Path.Combine(root, "manifest.txt");
            string checksums = Path.Combine(root, "SHA256SUMS.txt");
            File.WriteAllText(manifestJson, "old json");
            File.WriteAllText(manifestText, "old text");
            File.WriteAllText(checksums, "old checksums");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            IReadOnlyDictionary<string, string> cleanState =
                DotNetPublishPipelineRunner.CaptureCleanTrackedGeneratedProvenanceState(
                    root,
                    [manifestJson, manifestText, checksums]);
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, sourceRootPaths: [root]);

            File.WriteAllText(manifestJson, "new json");
            File.WriteAllText(manifestText, "new text");
            File.WriteAllText(checksums, "new checksums");
            IReadOnlyDictionary<string, string> writtenState =
                DotNetPublishPipelineRunner.CaptureTrackedManifestOutputState(
                    root,
                    cleanState,
                    manifestJson,
                    manifestText,
                    checksums);

            Assert.Equal(3, writtenState.Count);
            provenance.ValidateCurrentSource(writtenState.Keys);
            DotNetPublishPipelineRunner.ValidateTrackedManifestOutputState(writtenState);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void CachedProvenance_AllowsADeclaredHookOutputCreatedAfterTheCheckpoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Generated/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = revision,
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.CommandHook,
                        HookId = "generate",
                        HookGeneratedOutputs = ["Generated/later.txt"]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance checkpoint =
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan);
            string output = Path.Combine(root, "Generated", "later.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, "generated later");

            checkpoint.ValidateCurrentSource();
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_UsesImportedFrameworkForOrdinaryProjectReference()
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
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string selectedInput = Path.Combine(inputDirectory, "net8.json");
            string unselectedInput = Path.Combine(inputDirectory, "net10.json");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <AdditionalFiles Include="../../inputs/net8.json" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <AdditionalFiles Include="../../inputs/net10.json" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(selectedInput, "approved");
            File.WriteAllText(unselectedInput, "approved");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(unselectedInput, "changed");

            DotNetPublishPipelineRunner.SourceProvenance unselectedProvenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(
                unselectedProvenance.Dirty,
                string.Join(Environment.NewLine, unselectedProvenance.DirtyReasons));
            File.WriteAllText(unselectedInput, "approved");
            File.WriteAllText(selectedInput, "changed");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/net8.json", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

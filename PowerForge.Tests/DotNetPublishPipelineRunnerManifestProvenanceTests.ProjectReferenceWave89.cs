using PowerForge;
using System.Reflection;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void IsFinalPublishInputRetained_KeepsNestedFilesThatCleanupDoesNotRemove()
    {
        Assert.False(DotNetPublishPipelineRunner.IsFinalPublishInputRetained(
            "App.pdb",
            "App.pdb",
            keepSymbols: false,
            keepDocs: false));
        Assert.True(DotNetPublishPipelineRunner.IsFinalPublishInputRetained(
            "plugin/App.pdb",
            "plugin/App.pdb",
            keepSymbols: false,
            keepDocs: false));
        Assert.True(DotNetPublishPipelineRunner.IsFinalPublishInputRetained(
            "docs/App.xml",
            "docs\\App.xml",
            keepSymbols: false,
            keepDocs: false));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ValidatePublishProvenanceEntries_RevalidatesEveryDistinctArtifactScope()
    {
        int firstValidations = 0;
        int secondValidations = 0;
        var first = new DotNetPublishPipelineRunner.SourceProvenance(
            "revision",
            dirty: false,
            validateCurrentSource: () => firstValidations++);
        var second = new DotNetPublishPipelineRunner.SourceProvenance(
            "revision",
            dirty: false,
            validateCurrentSource: () => secondValidations++);
        var provenances = new Dictionary<string, DotNetPublishPipelineRunner.SourceProvenance>
        {
            ["first"] = first,
            ["duplicate"] = first,
            ["second"] = second
        };

        DotNetPublishPipelineRunner.ValidatePublishProvenanceEntries(provenances);

        Assert.Equal(1, firstValidations);
        Assert.Equal(1, secondValidations);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ResolvePlannedPublishGeneratedPaths_ExcludesEveryPublishDirectoryAndZipFromCachedSourceChecks()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = Path.Combine(root, "App.csproj"),
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net8.0",
                            Style = DotNetPublishStyle.FrameworkDependent,
                            OutputPath = "Artifacts/{target}/{rid}/{framework}/{style}",
                            Zip = true
                        }
                    },
                    new DotNetPublishTargetPlan
                    {
                        Name = "Tool",
                        ProjectPath = Path.Combine(root, "Tool.csproj"),
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net8.0",
                            Style = DotNetPublishStyle.FrameworkDependent,
                            OutputPath = "Artifacts/{target}/{rid}/{framework}/{style}",
                            Zip = true,
                            ZipPath = "Artifacts/Archives/{rid}/tool.zip"
                        }
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent
                    },
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = "linux-x64",
                        Style = DotNetPublishStyle.FrameworkDependent
                    },
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "Tool",
                        Framework = "net8.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                ]
            };
            string[] outputs = DotNetPublishPipelineRunner.ResolvePlannedPublishGeneratedPaths(plan);
            Assert.Equal(6, outputs.Length);
            Assert.Contains(outputs, path => path.EndsWith(
                "App-net8.0-win-x64-FrameworkDependent.zip",
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains(outputs, path => path.EndsWith(
                "App-net8.0-linux-x64-FrameworkDependent.zip",
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains(outputs, path => path.Replace('\\', '/').EndsWith(
                "Artifacts/Archives/win-x64/tool.zip",
                StringComparison.OrdinalIgnoreCase));
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    generatedPaths: outputs,
                    sourceRootPaths: [root]);
            Assert.False(provenance.Dirty);

            foreach (string output in outputs)
            {
                if (output.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    File.WriteAllText(output, "archive");
                }
                else
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "App.dll"), "published");
                }
            }

            provenance.ValidateCurrentSource();
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ProjectEvaluationRequest_RequiresSdkPackageEvidenceForReferencedProject()
    {
        Type runnerType = typeof(DotNetPublishPipelineRunner);
        Type requestType = runnerType.GetNestedType("ProjectEvaluationRequest", BindingFlags.NonPublic)!;
        Type referenceType = runnerType.GetNestedType("EvaluatedProjectReference", BindingFlags.NonPublic)!;
        ConstructorInfo requestConstructor = Assert.Single(
            requestType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        ConstructorInfo referenceConstructor = Assert.Single(
            referenceType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        object request = requestConstructor.Invoke(
        [
            Path.GetFullPath("App.csproj"),
            "net8.0",
            "Release",
            null,
            null,
            null,
            null,
            true,
            null,
            true
        ]);
        object projectReference = referenceConstructor.Invoke(
        [
            Path.GetFullPath("Library.csproj"),
            "net8.0",
            null,
            null
        ]);

        object childRequest = requestType.GetMethod(
                "ForProject",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [referenceType],
                modifiers: null)!
            .Invoke(request, [projectReference])!;

        Assert.True((bool)requestType.GetProperty(
                "RequiresSdkPackageEvidence",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(childRequest)!);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SourceProvenance_AllowsAuthorizedTrackedGeneratedPathAtCachedCheckpoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string statePath = Path.Combine(root, "version-state.json");
            File.WriteAllText(statePath, "{\"version\":1}");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved state\"");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, sourceRootPaths: [root]);
            Assert.False(provenance.Dirty);

            File.WriteAllText(statePath, "{\"version\":2}");

            Assert.Throws<InvalidOperationException>(provenance.ValidateCurrentSource);
            provenance.ValidateCurrentSource([statePath]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadPortableInventorySourceProvenance_RefreshRejectsNewIgnoredEvaluatedInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><AdditionalFiles Include=\"ignored/*.json\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nignored/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = revision,
                Configuration = "Release",
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net8.0",
                            Style = DotNetPublishStyle.FrameworkDependent
                        }
                    }
                ]
            };
            DotNetPublishPipelineRunner.SourceProvenance checkpoint =
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan);

            string ignoredDirectory = Directory.CreateDirectory(Path.Combine(root, "ignored")).FullName;
            File.WriteAllText(Path.Combine(ignoredDirectory, "rules.json"), "{}");
            checkpoint.ValidateCurrentSource();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan));
            Assert.Contains("untrusted evaluated build input", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

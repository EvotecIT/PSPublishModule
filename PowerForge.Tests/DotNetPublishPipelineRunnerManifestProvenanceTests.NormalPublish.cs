using PowerForge;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void SignedInventory_LabelsPrebuiltOutputWithoutClaimingSourceDerivation()
    {
        var plan = new DotNetPublishPlan { NoBuildInPublish = true };
        Assert.Equal("working-tree", DotNetPublishPipelineRunner.DescribeBuildInputMode(plan));

        plan.SkipBuildRequested = true;
        Assert.Equal("prebuilt-unverified", DotNetPublishPipelineRunner.DescribeBuildInputMode(plan));

        plan.UseControlledSourceProvenance = true;
        Assert.Equal("controlled-source", DotNetPublishPipelineRunner.DescribeBuildInputMode(plan));

        var inventory = new PowerForgePortablePayloadInventory { BuildInputMode = "prebuilt-unverified" };
        string explicitMode = Encoding.UTF8.GetString(PowerForgePortablePayloadInventoryCms.Serialize(inventory));
        Assert.Contains("\"BuildInputMode\": \"prebuilt-unverified\"", explicitMode);

        inventory.BuildInputMode = null;
        string legacyMode = Encoding.UTF8.GetString(PowerForgePortablePayloadInventoryCms.Serialize(inventory));
        Assert.DoesNotContain("BuildInputMode", legacyMode);
        Assert.Null(JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(legacyMode)!.BuildInputMode);
    }

    [Fact]
    public void NormalPublish_RunsBuildPhaseInsteadOfForcingNoBuild()
    {
        var plan = new DotNetPublishPlan
        {
            NoBuildInPublish = true
        };
        var target = new DotNetPublishTargetPlan
        {
            Name = "app",
            ProjectPath = "App.csproj",
            Publish = new DotNetPublishPublishOptions()
        };

        var normalArgs = DotNetPublishPipelineRunner.BuildPublishArguments(
            plan, target, "net10.0", "win-x64", DotNetPublishStyle.PortableCompat, "out");
        Assert.DoesNotContain("--no-build", normalArgs);

        plan.UseControlledSourceProvenance = true;
        var controlledArgs = DotNetPublishPipelineRunner.BuildPublishArguments(
            plan, target, "net10.0", "win-x64", DotNetPublishStyle.PortableCompat, "out");
        Assert.Contains("--no-build", controlledArgs);

        plan.UseControlledSourceProvenance = false;
        plan.SkipBuildRequested = true;
        var explicitSkipArgs = DotNetPublishPipelineRunner.BuildPublishArguments(
            plan, target, "net10.0", "win-x64", DotNetPublishStyle.PortableCompat, "out");
        Assert.Contains("--no-build", explicitSkipArgs);
    }

    [Fact]
    public void NormalSignedPublish_RejectsSourceMutationByPublishBeforeSigning()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            string sourcePath = Path.Combine(root, "Program.cs");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(sourcePath, "class Program { static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Artifacts/\nbin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            int publishCalls = 0;
            var runner = new DotNetPublishPipelineRunner(new NullLogger(), new RecordingProcessRunner(_ =>
            {
                publishCalls++;
                File.AppendAllText(sourcePath, "// changed by publish");
                return new ProcessRunResult(0, string.Empty, string.Empty, "dotnet", TimeSpan.Zero, timedOut: false);
            }));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = RunGit(root, "rev-parse HEAD").Trim(),
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        ExecutableIdentities = ["App"],
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = ["win-x64"],
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputPath = Path.Combine("Artifacts", "app"),
                            UseStaging = false,
                            Sign = new DotNetPublishSignOptions { Enabled = true }
                        }
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "publish",
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.PortableCompat
                    }
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.False(result.Succeeded);
            Assert.Contains("source changed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, publishCalls);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NormalSignedPublish_UsesWorkingTreeWithoutControlledMsBuildReconstruction()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            string sourcePath = Path.Combine(projectDirectory, "Program.cs");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(sourcePath, "class Program { static void Main() { } }");
            File.WriteAllText(Path.Combine(root, "Directory.Packages.props"),
                "<Project><PropertyGroup><IsRunner>$([System.String]::Copy('$(MSBuildProjectFullPath)').Contains('/runner/'))</IsRunner></PropertyGroup></Project>");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"normal publish source\"");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = RunGit(root, "rev-parse HEAD").Trim(),
                Targets = new[] { new DotNetPublishTargetPlan { ProjectPath = projectPath } }
            };

            DotNetPublishPipelineRunner.SourceProvenance clean =
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan);
            Assert.False(clean.Dirty);

            File.AppendAllText(sourcePath, "// changed");
            InvalidOperationException dirty = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan));
            Assert.Contains("source changed", dirty.Message, StringComparison.OrdinalIgnoreCase);
            InvalidOperationException bundleDirty = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ValidateBundleSourceBeforeSigning(
                    plan,
                    Path.Combine(root, "bundle-output"),
                    Array.Empty<DotNetPublishArtefactResult>(),
                    new[] { new DotNetPublishStep() }));
            Assert.Contains("source changed", bundleDirty.Message, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(sourcePath, "class Program { static void Main() { } }");
            string siblingDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string siblingPath = Path.Combine(siblingDirectory, "Library.cs");
            File.WriteAllText(siblingPath, "class Library { }");
            InvalidOperationException siblingDirty = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.ReadPortableInventorySourceProvenance(plan));
            Assert.Contains("Library/Library.cs", siblingDirty.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

}

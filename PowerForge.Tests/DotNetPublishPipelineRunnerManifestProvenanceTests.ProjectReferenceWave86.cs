using System.Reflection;
using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildEnvironment_DropsPrivateFeedCredential()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["NuGetPackageSourceCredentials_PrivateFeed"] = "Username=user;Password=secret"
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));
            Assert.False(environment.ContainsKey("NuGetPackageSourceCredentials_PrivateFeed"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ShouldRefreshLockedRestoreOutputs_HonorsNoRestorePlan()
    {
        Assert.True(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(null));
        Assert.True(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(
            new DotNetPublishPlan { NoRestoreInPublish = false }));
        Assert.False(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(
            new DotNetPublishPlan { NoRestoreInPublish = true }));
    }

    [Fact]
    public void RemapControlledPublishSourceValue_MapsMetadataBackToOriginalCheckout()
    {
        string controlledRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "controlled-source"));
        string originalRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "original-source"));
        string value = Path.Combine(controlledRoot, "src", "App") + ";unchanged";

        string mapped = DotNetPublishPipelineRunner.RemapControlledPublishSourceValue(
            value,
            controlledRoot,
            originalRoot);

        Assert.Equal(Path.Combine(originalRoot, "src", "App") + ";unchanged", mapped);
    }

    [Fact]
    public void SelectPublishInputSnapshotCandidates_IncludesPackageInputForBuildPublish()
    {
        var generatedInput = new DotNetPublishPipelineRunner.NoBuildPublishInput(
            "evaluation",
            Path.GetFullPath("generated.dll"),
            "generated.dll",
            new Dictionary<string, string>(),
            "AA");
        var packageInput = new DotNetPublishPipelineRunner.NoBuildPublishInput(
            "evaluation",
            Path.GetFullPath("package.dll"),
            "package.dll",
            new Dictionary<string, string>(),
            "BB",
            isPackageBacked: true);

        DotNetPublishPipelineRunner.NoBuildPublishInput selected = Assert.Single(
            DotNetPublishPipelineRunner.SelectPublishInputSnapshotCandidates(
                noBuildInPublish: false,
                [generatedInput, packageInput]));

        Assert.Same(packageInput, selected);
        Assert.Equal(
            2,
            DotNetPublishPipelineRunner.SelectPublishInputSnapshotCandidates(
                noBuildInPublish: true,
                [generatedInput, packageInput]).Length);
    }

    [Fact]
    public void NoBuildPublishSnapshot_RejectsUnattestedUnixMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "apphost");
            byte[] bytes = "controlled-apphost"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            UnixFileMode actualMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(sourcePath, actualMode);
            int provenMode = (int)(actualMode | UnixFileMode.UserExecute);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "apphost",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)),
                unixFileMode: provenMode);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null));

            Assert.Contains("Unix mode changed", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledGeneratedOutputEquivalence_RejectsUnixModeMismatch()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string candidatePath = Path.Combine(root, "candidate");
            string controlledPath = Path.Combine(root, "controlled");
            File.WriteAllText(candidatePath, "same-bytes");
            File.WriteAllText(controlledPath, "same-bytes");
            File.SetUnixFileMode(
                candidatePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(
                controlledPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "AreControlledGeneratedOutputsEquivalent",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            Assert.False((bool)method.Invoke(null, [candidatePath, controlledPath])!);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

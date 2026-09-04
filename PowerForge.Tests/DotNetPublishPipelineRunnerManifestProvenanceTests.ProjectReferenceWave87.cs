using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void TrustedDotNetInstallationSnapshot_RejectsSdkAssemblyReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            string sdkDirectory = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100")).FullName;
            string sdkAssembly = Path.Combine(sdkDirectory, "MSBuild.dll");
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(sdkAssembly, "trusted-sdk");
            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(executablePath);

            File.WriteAllText(sdkAssembly, "replaced-sdk");

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.ValidateUnchanged(verifyHashes: true));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void TrustedDotNetInstallationSnapshot_BindsOnlySelectedSdkClosure()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            string selectedSdk = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100")).FullName;
            string unrelatedSdk = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.200")).FullName;
            string selectedAssembly = Path.Combine(selectedSdk, "MSBuild.dll");
            string unrelatedAssembly = Path.Combine(unrelatedSdk, "MSBuild.dll");
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(selectedAssembly, "selected-sdk");
            File.WriteAllText(unrelatedAssembly, "unrelated-sdk");
            File.WriteAllText(
                Path.Combine(root, "global.json"),
                "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"disable\"}}");
            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(executablePath, root);

            Assert.True(snapshot.AffectsCapturedClosureForTest(selectedAssembly));
            Assert.False(snapshot.AffectsCapturedClosureForTest(unrelatedAssembly));
            File.WriteAllText(unrelatedAssembly, "changed-unrelated-sdk");
            snapshot.ValidateUnchanged(verifyHashes: true);
            File.WriteAllText(selectedAssembly, "changed-selected-sdk");

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.ValidateUnchanged(verifyHashes: true));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void TrustedDotNetInstallationSnapshot_RejectsDifferentSdkSelectionForLaterChild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100"));
            Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.200"));
            string firstWorkingDirectory = Directory.CreateDirectory(Path.Combine(root, "first")).FullName;
            string secondWorkingDirectory = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(
                Path.Combine(firstWorkingDirectory, "global.json"),
                "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"disable\"}}");
            File.WriteAllText(
                Path.Combine(secondWorkingDirectory, "global.json"),
                "{\"sdk\":{\"version\":\"10.0.200\",\"rollForward\":\"disable\"}}");
            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(
                    executablePath,
                    firstWorkingDirectory);

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.EnsureSelection(executablePath, secondWorkingDirectory));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void PublishProvenanceLease_WatchesOnlyGuardedFilesAndMissingAncestors()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string guardedPath = Path.Combine(root, "App.csproj");
            string missingParent = Path.Combine(root, "missing", "nested");
            string absentControlPath = Path.Combine(missingParent, "Directory.Build.targets");
            File.WriteAllText(guardedPath, "<Project />");
            using DotNetPublishPipelineRunner.PublishProvenanceLease lease =
                DotNetPublishPipelineRunner.PublishProvenanceLease.Create([guardedPath, absentControlPath]);

            Assert.True(lease.AffectsGuardedPathForTest(guardedPath));
            Assert.True(lease.AffectsGuardedPathForTest(missingParent));
            Assert.False(lease.AffectsGuardedPathForTest(Path.Combine(root, "obj")));
            Assert.False(lease.AffectsGuardedPathForTest(Path.Combine(root, "unrelated.txt")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_PreservesOriginalDefiningProjectSemantics()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            byte[] bytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>
                {
                    ["DefiningProjectFullPath"] = Path.Combine(root, "Library.csproj")
                },
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            XDocument targets = XDocument.Load(snapshot.TargetsPath);
            string snapshotPath = Assert.Single(Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories));

            Assert.Empty(targets.Descendants("ResolvedFileToPublish"));
            StringComparison pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            Assert.Contains(targets.Descendants(), element =>
                (element.Name.LocalName.StartsWith("_ResolvedFileToPublish", StringComparison.Ordinal) ||
                 element.Name.LocalName.Equals("_FilesToBundle", StringComparison.Ordinal)) &&
                !string.IsNullOrWhiteSpace(element.Attribute("Include")?.Value) &&
                string.Equals(
                    Path.GetFullPath(element.Attribute("Include")!.Value),
                    snapshotPath,
                    pathComparison));
            Assert.Contains(targets.Descendants("Error"), error =>
                error.Attribute("Text")?.Value.Contains(sourcePath, StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildTasks_RejectHookToSdkDefinedTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sdkRoot = Directory.CreateDirectory(Path.Combine(root, "Sdks")).FullName;
            string toolsRoot = Directory.CreateDirectory(Path.Combine(root, "Tools")).FullName;
            File.WriteAllText(
                Path.Combine(sdkRoot, "Sdk.targets"),
                "<Project><Target Name=\"CoreCompile\" /></Project>");
            XDocument project = XDocument.Parse(
                "<Project><Target Name=\"Inject\" AfterTargets=\"CoreCompile\"><Exec Command=\"payload\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
                project,
                [project],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBuildSDKsPath"] = sdkRoot,
                    ["MSBuildToolsPath"] = toolsRoot
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildTasks_RejectHookToSdkTargetDeclaredInProps()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sdkRoot = Directory.CreateDirectory(Path.Combine(root, "Sdks")).FullName;
            string toolsRoot = Directory.CreateDirectory(Path.Combine(root, "Tools")).FullName;
            File.WriteAllText(
                Path.Combine(sdkRoot, "Sdk.props"),
                "<Project><Target Name=\"PrepareForBuild\" /></Project>");
            XDocument project = XDocument.Parse(
                "<Project><Target Name=\"Inject\" BeforeTargets=\"PrepareForBuild\"><Exec Command=\"payload\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
                project,
                [project],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBuildSDKsPath"] = sdkRoot,
                    ["MSBuildToolsPath"] = toolsRoot
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void PublishProvenanceLease_RejectsNewAncestorBuildControlFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string absentPath = Path.Combine(root, "Directory.Build.targets");
            using DotNetPublishPipelineRunner.PublishProvenanceLease lease =
                DotNetPublishPipelineRunner.PublishProvenanceLease.Create([absentPath]);

            File.WriteAllText(absentPath, "<Project />");

            Assert.Throws<InvalidOperationException>(lease.ValidateUnchanged);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void PublishProvenanceLease_RejectsCreationOfMissingGuardedAncestor()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string missingDirectory = Path.Combine(root, "not-yet-created");
            string absentPath = Path.Combine(missingDirectory, "nested", "Directory.Build.targets");
            using DotNetPublishPipelineRunner.PublishProvenanceLease lease =
                DotNetPublishPipelineRunner.PublishProvenanceLease.Create([absentPath]);

            Directory.CreateDirectory(missingDirectory);

            Assert.Throws<InvalidOperationException>(lease.ValidateUnchanged);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void BuildControlCandidates_IncludeAncestorsAboveRepositoryRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string repository = Directory.CreateDirectory(Path.Combine(root, "repository")).FullName;
            string projectDirectory = Directory.CreateDirectory(Path.Combine(repository, "src", "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            string outerTargets = Path.Combine(root, "Directory.Build.targets");

            string[] candidates = DotNetPublishPipelineRunner
                .EnumerateAncestorBuildControlCandidatePaths(projectPath)
                .ToArray();

            Assert.Contains(outerTargets, candidates, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void BuildControlCandidates_IncludeOnlyProjectLocalDefaultNuGetLockFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string repository = Directory.CreateDirectory(Path.Combine(root, "repository")).FullName;
            string projectDirectory = Directory.CreateDirectory(Path.Combine(repository, "src", "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");

            string[] candidates = DotNetPublishPipelineRunner
                .EnumerateAncestorBuildControlCandidatePaths(projectPath)
                .ToArray();
            StringComparer comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

            Assert.Contains(Path.Combine(projectDirectory, "packages.lock.json"), candidates, comparer);
            Assert.DoesNotContain(Path.Combine(repository, "packages.lock.json"), candidates, comparer);
            Assert.DoesNotContain(Path.Combine(root, "packages.lock.json"), candidates, comparer);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledResolutionItems_DropProofOnlyInfrastructurePaths()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string controlledOutput = Directory.CreateDirectory(Path.Combine(root, "proof")).FullName;
            string controlledSource = Directory.CreateDirectory(Path.Combine(controlledOutput, "source")).FullName;
            string controlledIntermediate = Directory.CreateDirectory(Path.Combine(controlledOutput, "obj")).FullName;
            string controlledPackages = Directory.CreateDirectory(Path.Combine(controlledOutput, "packages")).FullName;
            string originalSource = Directory.CreateDirectory(Path.Combine(root, "original")).FullName;
            string originalIntermediate = Directory.CreateDirectory(Path.Combine(originalSource, "obj")).FullName;
            using JsonDocument items = JsonDocument.Parse(
                $$"""
                {
                  "None": [
                    { "Identity": "{{Path.Combine(controlledOutput, ".globalconfig").Replace("\\", "\\\\")}}", "FullPath": "{{Path.Combine(controlledOutput, ".globalconfig").Replace("\\", "\\\\")}}" },
                    { "Identity": "{{Path.Combine(controlledSource, "tracked.txt").Replace("\\", "\\\\")}}", "FullPath": "{{Path.Combine(controlledSource, "tracked.txt").Replace("\\", "\\\\")}}" },
                    { "Identity": "{{(controlledOutput + "-sibling.txt").Replace("\\", "\\\\")}}", "FullPath": "{{(controlledOutput + "-sibling.txt").Replace("\\", "\\\\")}}" }
                  ],
                  "ProjectReference": [
                    {
                      "Identity": "../Library/Library.csproj",
                      "FullPath": "{{Path.Combine(controlledSource, "Library", "Library.csproj").Replace("\\", "\\\\")}}",
                      "DefiningProjectFullPath": "{{Path.Combine(controlledOutput, "PowerForge.ControlledProjectReferences.targets").Replace("\\", "\\\\")}}"
                    }
                  ]
                }
                """);

            Assert.True(DotNetPublishPipelineRunner.TryMapControlledResolutionItemsForTest(
                items.RootElement,
                controlledOutput,
                controlledSource,
                originalSource,
                controlledIntermediate,
                originalIntermediate,
                controlledPackages,
                out string mappedJson));
            using JsonDocument mapped = JsonDocument.Parse(mappedJson);
            JsonElement none = mapped.RootElement.GetProperty("None");
            JsonElement projectReferences = mapped.RootElement.GetProperty("ProjectReference");

            Assert.Equal(2, none.GetArrayLength());
            Assert.Single(projectReferences.EnumerateArray());
            Assert.Equal(
                Path.Combine(originalSource, "tracked.txt"),
                none[0].GetProperty("FullPath").GetString(),
                ignoreCase: OperatingSystem.IsWindows());
            Assert.Equal(
                Path.Combine(originalSource, "Library", "Library.csproj"),
                projectReferences[0].GetProperty("FullPath").GetString(),
                ignoreCase: OperatingSystem.IsWindows());
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

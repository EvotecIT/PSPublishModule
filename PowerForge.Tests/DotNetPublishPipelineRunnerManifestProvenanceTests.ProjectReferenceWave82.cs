using System.Security.Cryptography;
using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectInheritedReferenceHintPathReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string controlledReference = Path.Combine(root, "placeholder.dll");
            string externalReference = Path.Combine(externalRoot, "external.dll");
            string linkedReference = Path.Combine(root, "linked.dll");
            File.WriteAllText(controlledReference, "controlled");
            File.WriteAllText(externalReference, "external");
            try
            {
                File.CreateSymbolicLink(linkedReference, externalReference);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemDefinitionGroup>
                    <Reference><HintPath>linked.dll</HintPath></Reference>
                  </ItemDefinitionGroup>
                  <Target Name="Build">
                    <ItemGroup><Reference Include="placeholder.dll" /></ItemGroup>
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, controlledReference],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectInheritedEmbeddedResourceDependentUponReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string resourcePath = Path.Combine(root, "Strings.resx");
            string externalSource = Path.Combine(externalRoot, "External.cs");
            string linkedSource = Path.Combine(root, "Linked.cs");
            File.WriteAllText(resourcePath, "<root />");
            File.WriteAllText(externalSource, "external");
            try
            {
                File.CreateSymbolicLink(linkedSource, externalSource);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemDefinitionGroup>
                    <EmbeddedResource><DependentUpon>Linked.cs</DependentUpon></EmbeddedResource>
                  </ItemDefinitionGroup>
                  <Target Name="Build">
                    <ItemGroup><EmbeddedResource Include="Strings.resx" /></ItemGroup>
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, resourcePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_ExplicitMetadataOverridesInheritedDefault()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string referencePath = Path.Combine(root, "controlled.dll");
            string explicitHintPath = Path.Combine(root, "explicit.dll");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(referencePath, "controlled reference");
            File.WriteAllText(explicitHintPath, "controlled hint");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemDefinitionGroup>
                    <Reference><HintPath>missing-default.dll</HintPath></Reference>
                  </ItemDefinitionGroup>
                  <Target Name="Build">
                    <ItemGroup>
                      <Reference Include="controlled.dll"><HintPath>explicit.dll</HintPath></Reference>
                    </ItemGroup>
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, referencePath, explicitHintPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_PreservesUnixExecutableMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "apphost");
            byte[] bytes = "controlled-apphost"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            UnixFileMode expectedMode = UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute;
            File.SetUnixFileMode(sourcePath, expectedMode);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "apphost",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)));

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null);
            string snapshotPath = Assert.Single(Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*",
                SearchOption.AllDirectories));

            Assert.Equal(expectedMode, File.GetUnixFileMode(snapshotPath));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_AllowsControlledProjectGeneratedPublishInput()
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
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="AddGeneratedPayload" BeforeTargets="ComputeFilesToPublish">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/generated.txt"
                                      Lines="controlled"
                                      Overwrite="true" />
                    <ItemGroup>
                      <ResolvedFileToPublish Include="$(MSBuildProjectDirectory)/obj/generated.txt"
                                             RelativePath="generated.txt"
                                             CopyToPublishDirectory="Always" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Class1.cs"), "public static class Class1 { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(
                root,
                $"build \"{projectPath}\" -c Release -f net8.0 --no-restore --nologo " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true");
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
                        ProjectPath = projectPath,
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
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void DotNetExecutionEnvironment_UnsignedChildPreservesSafeAmbientVariables()
    {
        IReadOnlyDictionary<string, string?> environment =
            DotNetPublishPipelineRunner.CreateSafeDotNetChildEnvironment(
                environmentVariables: null,
                ["CI", "GITHUB_ACTIONS", "SOURCE_DATE_EPOCH", "DOTNET_STARTUP_HOOKS"],
                removeUnapprovedAmbient: false);

        Assert.False(environment.ContainsKey("CI"));
        Assert.False(environment.ContainsKey("GITHUB_ACTIONS"));
        Assert.False(environment.ContainsKey("SOURCE_DATE_EPOCH"));
        Assert.Null(environment["DOTNET_STARTUP_HOOKS"]);
    }

    [Fact]
    public void Run_UnsignedBuildLeavesSafeAmbientVariablesInherited()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previousCi = Environment.GetEnvironmentVariable("CI");
        string? previousGitHubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        string? previousSourceDateEpoch = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        try
        {
            Environment.SetEnvironmentVariable("CI", "true");
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            ProcessRunRequest? captured = null;
            var runner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new RecordingProcessRunner(request =>
                {
                    captured = request;
                    return new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        request.FileName,
                        TimeSpan.Zero,
                        timedOut: false);
                }));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "build",
                        Kind = DotNetPublishStepKind.Build,
                        Title = "Build"
                    }
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.NotNull(captured?.EnvironmentVariables);
            Assert.False(captured!.EnvironmentVariables!.ContainsKey("CI"));
            Assert.False(captured.EnvironmentVariables.ContainsKey("GITHUB_ACTIONS"));
            Assert.False(captured.EnvironmentVariables.ContainsKey("SOURCE_DATE_EPOCH"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", previousCi);
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", previousGitHubActions);
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", previousSourceDateEpoch);
            DeleteTestRepository(root);
        }
    }
}

using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectTrackedFileLinks()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.targets");
            string linkPath = Path.Combine(root, "payload.targets");
            File.WriteAllText(externalPath, "<Project />");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("CustomBuild.xml", "<Project><PropertyGroup><Payload>{0}</Payload></PropertyGroup></Project>")]
    [InlineData("Directory.Build.rsp", "-p:Payload={0}")]
    public void ControlledBuildInputs_RejectRootedValuesAcrossEvaluatedInputShapes(
        string fileName,
        string content)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, fileName),
                string.Format(content, Path.Combine(externalRoot, "payload.dll")));

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildEnvironment_RemovesUnapprovedAmbientVariables()
    {
        string key = "POWERFORGE_UNTRUSTED_AMBIENT_" + Guid.NewGuid().ToString("N");
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        Environment.SetEnvironmentVariable(key, Path.Combine(root, "payload.dll"));
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>(),
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));
            Assert.True(environment.TryGetValue(key, out string? inheritedValue));
            Assert.Null(inheritedValue);
            Assert.StartsWith(
                Path.GetDirectoryName(controlledRoot)!,
                environment["APPDATA"]!,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildEnvironment_ReplaysOnlyAdmittedNonSecretPlanValues()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["DETERMINISTIC_BUILD_VALUE"] = "2026-08-29T20:00:00Z",
                    ["PRIVATE_BUILD_TOKEN"] = "private-token"
                },
                ["DETERMINISTIC_BUILD_VALUE"],
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));

            Assert.Equal("2026-08-29T20:00:00Z", environment["DETERMINISTIC_BUILD_VALUE"]);
            Assert.False(environment.ContainsKey("PRIVATE_BUILD_TOKEN"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RecoversPropertyFunctionTaskOutputItemName()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="CreateReference" BeforeTargets="ResolveReferences">
                    <CreateItem Include="../Library/Library.csproj">
                      <Output TaskParameter="Include"
                              ItemName="$([System.String]::Copy('ProjectReference'))" />
                    </CreateItem>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedForUnexpandedScheduledPropertyAssignment()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk" InitialTargets="ConfigureReferenceTargets">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ConfigureReferenceTargets">
                    <PropertyGroup>
                      <ResolveReferencesDependsOn>$(ResolveReferencesDependsOn);$([System.String]::Copy('CreateReference').ToUpperInvariant())</ResolveReferencesDependsOn>
                    </PropertyGroup>
                  </Target>
                  <Target Name="CREATEREFERENCE">
                    <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net8.0");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_ContainsHeadMutationInsideDetachedEvaluation()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="MutateHead" BeforeTargets="ResolveReferences">
                    <Exec Command="git -c user.name=PowerForge -c user.email=powerforge@example.invalid commit --allow-empty -m provenance-mutation" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string approvedRevision = RunGit(root, "rev-parse HEAD").Trim();

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Equal(approvedRevision, RunGit(root, "rev-parse HEAD").Trim());
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

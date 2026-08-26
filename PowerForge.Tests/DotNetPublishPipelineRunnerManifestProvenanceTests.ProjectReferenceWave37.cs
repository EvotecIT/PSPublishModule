using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputCopiedFromLiteralAbsoluteInput()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadLiteralGeneratedOutputFixture(configureFsMonitor: false, out _);

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotExecuteConfiguredFsMonitorDuringControlledProof()
    {
        if (!OperatingSystem.IsWindows())
            return;

        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadLiteralGeneratedOutputFixture(configureFsMonitor: true, out bool fsMonitorExecuted);

        Assert.True(provenance.Dirty);
        Assert.False(fsMonitorExecuted);
    }

    [Fact]
    public void ReadSourceProvenance_RecoversComputedTaskOutputItemName()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceItemName>ProjectReference</ReferenceItemName>
                  </PropertyGroup>
                  <Target Name="CreateReference" BeforeTargets="ResolveReferences">
                    <CreateItem Include="../Library/Library.csproj"
                                AdditionalMetadata="AdditionalProperties=A=1%3BB=2">
                      <Output TaskParameter="Include" ItemName="$(ReferenceItemName)" />
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
    public void ReadSourceProvenance_UsesPublishBuildProjectReferencesForTaskOutputs()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="CreateReference"
                          BeforeTargets="ResolveReferences"
                          Condition="'$(BuildProjectReferences)' == 'true'">
                    <CreateItem Include="../Library/Library.csproj"
                                AdditionalMetadata="AdditionalProperties=A=1%3BB=2">
                      <Output TaskParameter="Include" ItemName="ProjectReference" />
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
            buildProperties: new Dictionary<string, string>
            {
                ["BuildProjectReferences"] = "true"
            },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotExecuteUnscheduledTaskOutputs()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="NeverRuns">
                    <CreateItem Include="../Library/Library.csproj"
                                AdditionalMetadata="AdditionalProperties=A=1%3BB=2">
                      <Output TaskParameter="Include" ItemName="ProjectReference" />
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

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsStringCopyTargetSchedulingExpression()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="RemoveFlavor"
                          BeforeTargets="$([System.String]::Copy('ResolveReferences'))">
                    <PropertyGroup>
                      <_GlobalPropertiesToRemoveFromProjectReferences>Flavor</_GlobalPropertiesToRemoveFromProjectReferences>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == ''">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string> { ["Flavor"] = "Parent" },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_PreservesAmbiguousImportedRelativeRemove()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="A=1;B=2" />
                  </ItemGroup>
                  <Import Project="build/References.targets" />
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["build/References.targets"] = """
                    <Project>
                      <ItemGroup>
                        <ProjectReference Remove="../../Library/Library.csproj" />
                      </ItemGroup>
                    </Project>
                    """
            });

        AssertSelectedInputIsDirty(provenance);
    }

    private static DotNetPublishPipelineRunner.SourceProvenance ReadLiteralGeneratedOutputFixture(
        bool configureFsMonitor,
        out bool fsMonitorExecuted)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string markerPath = Path.Combine(root, "fsmonitor-invoked.txt");
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string payloadPath = Path.Combine(cacheDirectory, "payload.dll");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(
                libraryProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ReplaceOutput" AfterTargets="Build" Condition="Exists('{payloadPath}')">
                    <Copy SourceFiles="{payloadPath}" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\nfsmonitor-invoked.txt\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            File.Copy(libraryOutput, payloadPath);
            File.AppendAllText(payloadPath, "ignored literal payload");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            if (configureFsMonitor)
            {
                string scriptPath = Path.Combine(cacheDirectory, "fsmonitor.sh");
                File.WriteAllText(
                    scriptPath,
                    $"#!/bin/sh\ncase \"$PWD\" in *powerforge-provenance-build*) printf invoked > '{markerPath.Replace('\\', '/')}' ;; esac\nprintf '2\\nbuiltin:fake\\n'\n");
                RunGit(root, $"config core.fsmonitor \"sh '{scriptPath.Replace('\\', '/')}'\"");
            }

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");
            fsMonitorExecuted = File.Exists(markerPath);
            return provenance;
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}

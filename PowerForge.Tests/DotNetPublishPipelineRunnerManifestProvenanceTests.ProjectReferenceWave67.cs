using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void TrustedBuildTool_AcceptsExplicitAttestedDotNetPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previous = Environment.GetEnvironmentVariable("POWERFORGE_DOTNET_PATH");
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool(
                "dotnet",
                out string installedDotNet));
            string configuredPath = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            File.Copy(installedDotNet, configuredPath);
            Directory.CreateDirectory(Path.Combine(root, "host", "fxr"));
            Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App"));
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", configuredPath);

            Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool(
                "dotnet",
                out string resolvedPath));
            Assert.Equal(Path.GetFullPath(configuredPath), resolvedPath, OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", previous);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_NoBuildProofIsBoundToEvaluationContext()
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
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                    <OutputPath>bin/Release/shared/</OutputPath>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{projectPath}\" -c Release -f net8.0 --no-restore --nologo");
            RunDotNet(root, $"build \"{projectPath}\" -c Release -f net10.0 --no-restore --nologo");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
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
                            },
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
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
                reason.Contains("App", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectInactiveConditionalPropertyOverride()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "external payload");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(Path.Combine(root, "safe.txt"), "safe");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <Files>payload-link.txt</Files>
                    <Files Condition="'false' == 'true'">safe.txt</Files>
                  </PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, Path.Combine(root, "safe.txt")],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptInactiveConditionalPropertyGroup()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "external payload");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            string safePath = Path.Combine(root, "safe.txt");
            File.WriteAllText(safePath, "safe");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <Files>safe.txt</Files>
                  </PropertyGroup>
                  <PropertyGroup Condition="'false' == 'true'">
                    <Files>payload-link.txt</Files>
                  </PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, safePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectUnprovableConditionalPropertyAssignment()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string safePath = Path.Combine(root, "safe.txt");
            File.WriteAllText(safePath, "safe");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <Files>safe.txt</Files>
                    <Files Condition="'$(Unproven)' == 'enabled'">other.txt</Files>
                  </PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, safePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectActiveChoosePropertyBranch()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "external payload");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            string safePath = Path.Combine(root, "safe.txt");
            File.WriteAllText(safePath, "safe");
            File.WriteAllText(projectPath, """
                <Project>
                  <Choose>
                    <When Condition="'true' == 'true'">
                      <PropertyGroup>
                        <Files>payload-link.txt</Files>
                      </PropertyGroup>
                    </When>
                    <Otherwise>
                      <PropertyGroup>
                        <Files>safe.txt</Files>
                      </PropertyGroup>
                    </Otherwise>
                  </Choose>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, safePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}

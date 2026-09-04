using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("$(ComSpec)")]
    [InlineData("%PROCESSOR_ARCHITECTURE%")]
    [InlineData("$(PROCESSOR_ARCHITEW6432)")]
    public void ControlledBuildInputs_RejectAmbientPlatformEnvironmentReferences(string value)
    {
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledEnvironmentReference(value));
    }

    [Theory]
    [InlineData("'$([MSBuild]::IsOSPlatform(`Windows`))'")]
    [InlineData("'$([MSBuild]::IsOSPlatform('Linux'))'")]
    [InlineData("'$([MSBuild]::IsOSUnixLike())'")]
    public void ControlledBuildInputs_AllowLiteralPlatformPredicatesInConditions(string condition)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"""
                <Project>
                  <PropertyGroup Condition="{condition.Replace("'", "&apos;", StringComparison.Ordinal)}">
                    <TargetFrameworks>net8.0;net472</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectPlatformPredicatesOutsideConditions()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.proj"), """
                <Project>
                  <PropertyGroup>
                    <Trademark>$([MSBuild]::IsOSPlatform(`Windows`))</Trademark>
                  </PropertyGroup>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("MSBUILDADDITIONALSDKRESOLVERSFOLDER")]
    [InlineData("MSBUILD_EXE_PATH")]
    [InlineData("MSBUILDSDKSPATH")]
    [InlineData("MSBUILDNODEHANDLER")]
    [InlineData("MSBUILDNODEHANDLER_TYPE")]
    public void ControlledBuildEnvironment_RejectsMsBuildRuntimeInjection(string variableName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.False(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?> { [variableName] = "payload" },
                root,
                controlledRoot,
                out _));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("Csc")]
    [InlineData("Vbc")]
    [InlineData("Fsc")]
    public void ControlledBuildInputs_RejectExplicitCompilerPlugins(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string analyzerPath = Path.Combine(root, "analyzer.dll");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Compile\"><{taskName} Analyzers=\"analyzer.dll\" /></Target></Project>");
            File.WriteAllText(analyzerPath, "contained analyzer");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, analyzerPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("/analyzer:analyzer.dll")]
    [InlineData("/generator:generator.dll")]
    public void ControlledBuildInputs_RejectCompilerPluginsFromResponseFile(string responseLine)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Compile\"><Csc ResponseFiles=\"compiler.rsp\" /></Target></Project>");
            File.WriteAllText(responsePath, responseLine);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("/resource:payload-link.bin")]
    [InlineData("/reference:Fixture=payload-link.bin")]
    [InlineData("payload-link.bin")]
    public void ControlledBuildInputs_RejectResponseFileInputReparsePoint(string responseLine)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link.bin");
            File.WriteAllText(externalPath, "external resource");
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
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Compile\"><Csc ResponseFiles=\"compiler.rsp\" /></Target></Project>");
            File.WriteAllText(responsePath, responseLine);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath, linkPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_IgnoresUnreachedResponseFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "unreached.rsp");
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(responsePath, "/analyzer:missing.dll");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptContainedResponseFileResource()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            string resourcePath = Path.Combine(root, "payload.resources");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Compile\"><Csc ResponseFiles=\"compiler.rsp\" /></Target></Project>");
            File.WriteAllText(responsePath, "/resource:payload.resources,Fixture.Resource");
            File.WriteAllText(resourcePath, "contained resource");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath, resourcePath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void TrustedBuildTool_AcceptsExplicitAttestedGitPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previous = Environment.GetEnvironmentVariable("POWERFORGE_GIT_PATH");
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("git", out string sourcePath));
            string configuredPath = Path.Combine(root, OperatingSystem.IsWindows() ? "configured-git.exe" : "configured-git");
            File.Copy(sourcePath, configuredPath);
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", configuredPath);

            if (OperatingSystem.IsWindows())
            {
                Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("git", out string resolvedPath));
                Assert.Equal(Path.GetFullPath(configuredPath), resolvedPath, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Assert.False(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("git", out _));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", previous);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void TrustedBuildTool_AcceptsAppleSealedSystemGitWithSharedInode()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("git", out string resolvedPath));
        Assert.Equal("/usr/bin/git", resolvedPath, StringComparer.Ordinal);
    }

    [Fact]
    public void ReadSourceProvenance_AcceptsPackageTaskInputRelativeToConsumingProject()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadPackageTargetProvenance(
            "Package.RelativeInput",
            "<Project><Target Name=\"HashProjectInput\" BeforeTargets=\"Build\"><GetFileHash Files=\"project-input.txt\" /></Target></Project>",
            configureRepository: libraryDirectory => File.WriteAllText(
                Path.Combine(libraryDirectory, "project-input.txt"),
                "controlled project input"));

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_AcceptsInactiveWildcardPackageImport()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadPackageTargetProvenance(
            "Package.InactiveWildcard",
            "<Project><Import Project=\"$(MSBuildThisFileDirectory)optional/*.targets\" Condition=\"'$(EnableOptional)' == 'true'\" /></Project>");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_IgnoresUnreachedPackageResponseFile()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadPackageTargetProvenance(
            "Package.UnreachedResponse",
            "<Project />",
            new Dictionary<string, string> { ["build/unreached.rsp"] = "/analyzer:missing.dll" });

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    private static DotNetPublishPipelineRunner.SourceProvenance ReadPackageTargetProvenance(
        string packageId,
        string targetsContent,
        IReadOnlyDictionary<string, string>? additionalPackageFiles = null,
        Action<string>? configureRepository = null)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string packageRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "feed")).FullName;
            string targetsName = packageId + ".targets";
            string packageProject = Path.Combine(packageRoot, packageId + ".csproj");
            var packageItems = new List<string>
            {
                $"<None Include=\"build/{targetsName}\" Pack=\"true\" PackagePath=\"build/{targetsName}\" />"
            };
            foreach (KeyValuePair<string, string> file in additionalPackageFiles ??
                         new Dictionary<string, string>())
            {
                string path = Path.Combine(packageRoot, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value);
                packageItems.Add($"<None Include=\"{file.Key}\" Pack=\"true\" PackagePath=\"{file.Key}\" />");
            }
            File.WriteAllText(packageProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>{packageId}</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>{string.Join(Environment.NewLine, packageItems)}</ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(buildDirectory, targetsName), targetsContent);
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                $"<PackageReference Include=\"{packageId}\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
            if (configureRepository is not null)
            {
                configureRepository(Path.GetDirectoryName(libraryProject)!);
                RunGit(root, "add .");
                RunGit(root, "commit -m \"add controlled package task input\"");
            }
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            return DotNetPublishPipelineRunner.ReadSourceProvenance(
                root,
                buildProjectPaths: [appProject],
                buildConfiguration: "Release");
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(packageRoot);
        }
    }
}

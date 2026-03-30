namespace PowerForge.Tests;

public sealed class PowerForgeProjectDslMapperTests
{
    [Fact]
    public void CreateRelease_MapsTargetWorkspaceSigningAndInstallerDefaults()
    {
        var project = new ConfigurationProject
        {
            Name = "IntelligenceX",
            ProjectRoot = ".",
            Release = new ConfigurationProjectRelease
            {
                Configuration = "Release",
                PublishToolGitHub = true,
                SkipRestore = true,
                SkipBuild = true,
                SkipToolOutput = new[] { ConfigurationProjectReleaseOutputType.Tool }
            },
            Workspace = new ConfigurationProjectWorkspace
            {
                Profile = "oss",
                EnableFeatures = new[] { "chat" },
                DisableFeatures = new[] { "tests" }
            },
            Signing = new ConfigurationProjectSigning
            {
                Mode = ConfigurationProjectSigningMode.OnDemand,
                Thumbprint = "ABC123",
                Description = "IntelligenceX Chat"
            },
            Output = new ConfigurationProjectOutput
            {
                OutputRoot = "Artifacts/Custom",
                StageRoot = "Artifacts/Release",
                ManifestJsonPath = "Artifacts/Release/release-manifest.json",
                ChecksumsPath = "Artifacts/Release/SHA256SUMS.txt",
                IncludeChecksums = false
            },
            Targets = new[]
            {
                new ConfigurationProjectTarget
                {
                    Name = "ChatApp",
                    ProjectPath = "src/ChatApp/ChatApp.csproj",
                    Framework = "net8.0-windows10.0.26100.0",
                    Runtimes = new[] { "win-x64" },
                    Style = DotNetPublishStyle.PortableCompat,
                    OutputType = new[]
                    {
                        ConfigurationProjectTargetOutputType.Tool,
                        ConfigurationProjectTargetOutputType.Portable
                    }
                }
            },
            Installers = new[]
            {
                new ConfigurationProjectInstaller
                {
                    Id = "chatapp.msi",
                    Target = "ChatApp",
                    InstallerProjectPath = "installer/ChatApp/ChatApp.wixproj"
                }
            }
        };

        var (spec, request) = PowerForgeProjectDslMapper.CreateRelease(project, @"C:\repo\.powerforge\release.project.ps1", @"C:\repo");

        Assert.NotNull(spec.Tools);
        Assert.NotNull(spec.Tools!.DotNetPublish);
        Assert.Equal("Release", spec.Tools.Configuration);
        Assert.Single(spec.Tools.DotNetPublish.Targets);
        Assert.Single(spec.Tools.DotNetPublish.Bundles);
        Assert.Single(spec.Tools.DotNetPublish.Installers);
        Assert.NotNull(spec.WorkspaceValidation);
        Assert.Equal("oss", spec.WorkspaceValidation!.Profile);

        var target = spec.Tools.DotNetPublish.Targets[0];
        Assert.Equal("ChatApp", target.Name);
        Assert.Equal(Path.GetFullPath(@"C:\repo\src\ChatApp\ChatApp.csproj"), target.ProjectPath);
        Assert.Equal("net8.0-windows10.0.26100.0", target.Publish.Framework);
        Assert.Contains("win-x64", target.Publish.Runtimes);
        Assert.NotNull(target.Publish.Sign);
        Assert.False(target.Publish.Sign!.Enabled);
        Assert.Equal("ABC123", target.Publish.Sign.Thumbprint);

        var bundle = spec.Tools.DotNetPublish.Bundles[0];
        Assert.Equal("ChatApp", bundle.PrepareFromTarget);
        Assert.True(bundle.Zip);

        var installer = spec.Tools.DotNetPublish.Installers[0];
        Assert.Equal("chatapp.msi", installer.Id);
        Assert.Equal(Path.GetFullPath(@"C:\repo\installer\ChatApp\ChatApp.wixproj"), installer.InstallerProjectPath);
        Assert.Equal(bundle.Id, installer.PrepareFromBundleId);
        Assert.NotNull(installer.Sign);
        Assert.False(installer.Sign!.Enabled);

        Assert.True(request.ToolsOnly);
        Assert.Equal("Release", request.Configuration);
        Assert.True(request.PublishToolGitHub);
        Assert.True(request.SkipRestore);
        Assert.True(request.SkipBuild);
        Assert.Equal("oss", request.WorkspaceProfile);
        Assert.Equal("Artifacts/Custom", request.OutputRoot);
        Assert.Equal("Artifacts/Release", request.StageRoot);
        Assert.Equal("Artifacts/Release/release-manifest.json", request.ManifestJsonPath);
        Assert.Equal("Artifacts/Release/SHA256SUMS.txt", request.ChecksumsPath);
        Assert.True(request.SkipReleaseChecksums);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Tool, request.ToolOutputs);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Portable, request.ToolOutputs);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Installer, request.ToolOutputs);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Tool, request.SkipToolOutputs);
    }

    [Fact]
    public void CreateRelease_DefaultsToToolOutputWhenNoExplicitOutputsAreProvided()
    {
        var project = new ConfigurationProject
        {
            Name = "Demo",
            Targets = new[]
            {
                new ConfigurationProjectTarget
                {
                    Name = "Cli",
                    ProjectPath = "src/Cli/Cli.csproj",
                    Framework = "net10.0"
                }
            }
        };

        var (spec, request) = PowerForgeProjectDslMapper.CreateRelease(project, @"C:\repo\.powerforge\release.project.ps1", @"C:\repo");

        Assert.NotNull(spec.Tools);
        Assert.NotNull(spec.Tools!.DotNetPublish);
        Assert.Single(spec.Tools.DotNetPublish.Targets);
        Assert.Empty(spec.Tools.DotNetPublish.Bundles);
        Assert.Empty(spec.Tools.DotNetPublish.Installers);
        Assert.Single(request.ToolOutputs);
        Assert.Equal(PowerForgeReleaseToolOutputKind.Tool, request.ToolOutputs[0]);
    }

    [Fact]
    public void CreateRelease_UsesExplicitReleaseOutputDefaultsWhenProvided()
    {
        var project = new ConfigurationProject
        {
            Name = "Demo",
            Release = new ConfigurationProjectRelease
            {
                ToolOutput = new[]
                {
                    ConfigurationProjectReleaseOutputType.Portable,
                    ConfigurationProjectReleaseOutputType.Installer
                }
            },
            Targets = new[]
            {
                new ConfigurationProjectTarget
                {
                    Name = "Cli",
                    ProjectPath = "src/Cli/Cli.csproj",
                    Framework = "net10.0",
                    Runtimes = new[] { "win-x64" }
                }
            }
        };

        var (_, request) = PowerForgeProjectDslMapper.CreateRelease(project, @"C:\repo\.powerforge\release.project.ps1", @"C:\repo");

        Assert.Equal(2, request.ToolOutputs.Length);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Portable, request.ToolOutputs);
        Assert.Contains(PowerForgeReleaseToolOutputKind.Installer, request.ToolOutputs);
        Assert.DoesNotContain(PowerForgeReleaseToolOutputKind.Tool, request.ToolOutputs);
    }

    [Fact]
    public void CreateRelease_GeneratesBundleWhenReleaseDefaultsRequestPortableOutput()
    {
        var project = new ConfigurationProject
        {
            Name = "Demo",
            Release = new ConfigurationProjectRelease
            {
                ToolOutput = new[] { ConfigurationProjectReleaseOutputType.Portable }
            },
            Targets = new[]
            {
                new ConfigurationProjectTarget
                {
                    Name = "Cli",
                    ProjectPath = "src/Cli/Cli.csproj",
                    Framework = "net10.0",
                    Runtimes = new[] { "win-x64" },
                    OutputType = new[] { ConfigurationProjectTargetOutputType.Tool }
                }
            }
        };

        var (spec, request) = PowerForgeProjectDslMapper.CreateRelease(project, @"C:\repo\.powerforge\release.project.ps1", @"C:\repo");

        Assert.NotNull(spec.Tools);
        Assert.NotNull(spec.Tools!.DotNetPublish);
        Assert.Single(spec.Tools.DotNetPublish.Bundles);
        Assert.Equal("Cli", spec.Tools.DotNetPublish.Bundles[0].PrepareFromTarget);
        Assert.Single(request.ToolOutputs);
        Assert.Equal(PowerForgeReleaseToolOutputKind.Portable, request.ToolOutputs[0]);
    }
}

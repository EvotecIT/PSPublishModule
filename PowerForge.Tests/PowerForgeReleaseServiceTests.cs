using System.Text;
using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ToolReleasePlan_AppliesOverridesAcrossSelectedTarget()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeToolReleaseService(new NullLogger());
            var plan = service.Plan(
                new PowerForgeToolReleaseSpec
                {
                    ProjectRoot = ".",
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTarget
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            OutputName = "PowerForge",
                            Frameworks = new[] { "net10.0" },
                            Runtimes = new[] { "win-x64", "linux-x64" },
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained
                        }
                    }
                },
                Path.Combine(root, "release.json"),
                new PowerForgeReleaseRequest
                {
                    Targets = new[] { "PowerForge" },
                    Runtimes = new[] { "osx-arm64" },
                    Frameworks = new[] { "net8.0" },
                    Flavors = new[] { PowerForgeToolReleaseFlavor.SingleFx }
                });

            var target = Assert.Single(plan.Targets);
            var combination = Assert.Single(target.Combinations);
            Assert.Equal("1.2.3", target.Version);
            Assert.Equal("osx-arm64", combination.Runtime);
            Assert.Equal("net8.0", combination.Framework);
            Assert.Equal(PowerForgeToolReleaseFlavor.SingleFx, combination.Flavor);
            Assert.Contains("PowerForge", combination.OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_GroupsToolAssetsIntoSingleGitHubReleasePerTarget()
    {
        var zipA = Path.GetTempFileName();
        var zipB = Path.GetTempFileName();
        try
        {
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = Path.GetTempPath(),
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            OutputName = "PowerForge",
                            Version = "1.2.3",
                            ArtifactRootPath = Path.GetTempPath(),
                            Combinations = new[]
                            {
                                new PowerForgeToolReleaseCombinationPlan
                                {
                                    Runtime = "win-x64",
                                    Framework = "net10.0",
                                    Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                    OutputPath = Path.GetTempPath(),
                                    ZipPath = zipA
                                }
                            }
                        }
                    }
                },
                runTools: _ => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts = new[]
                    {
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.2.3",
                            OutputName = "PowerForge",
                            Runtime = "win-x64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = Path.GetTempPath(),
                            ExecutablePath = Path.Combine(Path.GetTempPath(), "PowerForge.exe"),
                            ZipPath = zipA
                        },
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.2.3",
                            OutputName = "PowerForge",
                            Runtime = "linux-x64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = Path.GetTempPath(),
                            ExecutablePath = Path.Combine(Path.GetTempPath(), "PowerForge"),
                            ZipPath = zipB
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release",
                        ReusedExistingRelease = true
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "PSPublishModule",
                            Token = "token",
                            TagTemplate = "{Target}-v{Version}",
                            ReleaseNameTemplate = "{Target} {Version}"
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            var publish = Assert.Single(publishCalls);
            Assert.Equal("PowerForge-v1.2.3", publish.TagName);
            Assert.Equal("PowerForge 1.2.3", publish.ReleaseName);
            Assert.Equal(2, publish.AssetFilePaths.Count);

            var release = Assert.Single(result.ToolGitHubReleases);
            Assert.True(release.Success);
            Assert.Equal(2, release.AssetPaths.Length);
            Assert.True(release.ReusedExistingRelease);
        }
        finally
        {
            TryDelete(zipA);
            TryDelete(zipB);
        }
    }

    [Fact]
    public void Execute_PlanOnly_UsesDotNetPublishWorkflowFromInlineToolsConfig()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>2.4.6</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64", "linux-x64" },
                                        Style = DotNetPublishStyle.PortableCompat,
                                        Zip = true
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    Targets = new[] { "PowerForge" },
                    Runtimes = new[] { "osx-arm64" },
                    Frameworks = new[] { "net8.0" }
                });

            Assert.True(result.Success);
            Assert.Null(result.ToolPlan);
            Assert.NotNull(result.DotNetToolPlan);

            var target = Assert.Single(result.DotNetToolPlan!.Targets);
            Assert.Equal("PowerForge", target.Name);

            var combination = Assert.Single(target.Combinations);
            Assert.Equal("osx-arm64", combination.Runtime);
            Assert.Equal("net8.0", combination.Framework);
            Assert.Equal(DotNetPublishStyle.PortableCompat, combination.Style);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ConfigurationOverride_AppliesToPackagesAndDotNetTools()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            ProjectBuildConfiguration? capturedPackages = null;

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, config, _) =>
                {
                    capturedPackages = config;
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = "release.json",
                        PlanOutputPath = "plan.json",
                        Result = new ProjectBuildResult()
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        Configuration = "Release"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat,
                                        Zip = true
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    Configuration = "Debug",
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(capturedPackages);
            Assert.Equal("Debug", capturedPackages!.Configuration);
            Assert.Equal("Debug", result.DotNetToolPlan?.Configuration);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_PlanOnly_BuildsAppleAppArchiveUploadPlan()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        ArchiveRoot = "Artifacts/Apple/Archives",
                        ExportRoot = "Artifacts/Apple/Exports",
                        Upload = true,
                        TeamId = "8ZPGZ79T7J",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra iPhone",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            },
                            new AppleAppConfiguration
                            {
                                Name = "Tactra Mac",
                                BundleId = "com.evotecit.tactra.mac",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "TactraMac",
                                Platform = ApplePlatform.macOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.AppleAppPlan);
            Assert.True(result.AppleAppPlan!.Archive);
            Assert.True(result.AppleAppPlan.Upload);
            Assert.Equal("Release", result.AppleAppPlan.Configuration);

            var phone = result.AppleAppPlan.Apps[0];
            Assert.Equal("Tactra iPhone", phone.Name);
            Assert.Equal(ApplePlatform.iOS, phone.Platform);
            Assert.Equal("generic/platform=iOS", phone.Destination);
            Assert.Equal(Path.Combine(root, "Tactra.xcodeproj"), phone.ProjectPath);
            Assert.Equal(Path.Combine(root, "Artifacts", "Apple", "Archives", "iOS", "Tactra-iPhone.xcarchive"), phone.ArchivePath);
            Assert.Equal(Path.Combine(root, "Artifacts", "Apple", "Exports", "iOS", "Tactra-iPhone"), phone.ExportPath);
            Assert.True(phone.Upload);
            Assert.Equal("8ZPGZ79T7J", phone.TeamId);

            var mac = result.AppleAppPlan.Apps[1];
            Assert.Equal("generic/platform=macOS", mac.Destination);
            Assert.Equal(Path.Combine(root, "Artifacts", "Apple", "Archives", "macOS", "Tactra-Mac.xcarchive"), mac.ArchivePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RunsArchiveAndUploadThroughSharedService()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();
            var uploadRequests = new List<AppleAppArchiveUploadRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: request =>
                {
                    uploadRequests.Add(request);
                    return new AppleAppArchiveUploadResult
                    {
                        ArchivePath = request.ArchivePath,
                        ExportPath = request.ExportPath!,
                        ExportOptionsPlistPath = Path.Combine(request.ExportPath!, "ExportOptions.plist"),
                        ProcessResult = new ProcessRunResult(0, "upload-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Upload = true,
                        TeamId = "TEAMID",
                        XcodeBuildExecutable = "xcodebuild-test",
                        AllowProvisioningUpdates = false,
                        SigningStyle = "automatic",
                        ManageAppVersionAndBuildNumber = true,
                        AppStoreConnectApiKeyPath = "secrets/AuthKey_ABC123DEFG.p8",
                        AppStoreConnectApiKeyId = "ABC123DEFG",
                        AppStoreConnectApiIssuerId = "issuer-id",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iPadOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            var archiveRequest = Assert.Single(archiveRequests);
            Assert.Equal("xcodebuild-test", archiveRequest.XcodeBuildExecutable);
            Assert.Equal(ApplePlatform.iPadOS, archiveRequest.Platform);
            Assert.Equal("generic/platform=iOS", archiveRequest.Destination);
            Assert.EndsWith(Path.Combine("secrets", "AuthKey_ABC123DEFG.p8"), archiveRequest.AppStoreConnectApiKeyPath, StringComparison.Ordinal);
            Assert.Equal("ABC123DEFG", archiveRequest.AppStoreConnectApiKeyId);
            Assert.Equal("issuer-id", archiveRequest.AppStoreConnectApiIssuerId);

            var uploadRequest = Assert.Single(uploadRequests);
            Assert.Equal("TEAMID", uploadRequest.TeamId);
            Assert.Equal("xcodebuild-test", uploadRequest.XcodeBuildExecutable);
            Assert.False(uploadRequest.AllowProvisioningUpdates);
            Assert.True(uploadRequest.ManageAppVersionAndBuildNumber);
            Assert.EndsWith(Path.Combine("secrets", "AuthKey_ABC123DEFG.p8"), uploadRequest.AppStoreConnectApiKeyPath, StringComparison.Ordinal);
            Assert.Equal("ABC123DEFG", uploadRequest.AppStoreConnectApiKeyId);
            Assert.Equal("issuer-id", uploadRequest.AppStoreConnectApiIssuerId);

            var appResult = Assert.Single(result.AppleApps);
            Assert.True(appResult.Success);
            Assert.NotNull(appResult.Archive);
            Assert.NotNull(appResult.Upload);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ToolsOnly_DoesNotRunAppleApps()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveCalled = false;

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = root,
                    Configuration = "Release"
                },
                runTools: _ => new PowerForgeToolReleaseResult
                {
                    Success = true
                },
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ =>
                {
                    archiveCalled = true;
                    throw new InvalidOperationException("Apple apps should not run.");
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec(),
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            Assert.False(archiveCalled);
            Assert.Null(result.AppleAppPlan);
            Assert.Empty(result.AppleApps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsScreenshotSyncUntilUnifiedSupportExists()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<NotSupportedException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        SyncScreenshots = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                }));

            Assert.Contains("SyncScreenshots is not supported", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_ValidatesProjectPathBeforeReportingSuccess()
    {
        var root = CreateSandbox();
        try
        {
            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<FileNotFoundException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Missing.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                }));

            Assert.Contains("Missing.xcodeproj", ex.FileName, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_UpdatesXcodeVersionBeforeArchive()
    {
        var root = CreateSandbox();
        try
        {
            var xcodeproj = CreateXcodeProject(root, "Tactra.xcodeproj", "1.0.0", "7");
            var pbxproj = Path.Combine(xcodeproj, "project.pbxproj");
            var archiveCalled = false;

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveCalled = true;
                    var content = File.ReadAllText(pbxproj);
                    Assert.Contains("MARKETING_VERSION = 2.1.0;", content, StringComparison.Ordinal);
                    Assert.Contains("CURRENT_PROJECT_VERSION = 8;", content, StringComparison.Ordinal);

                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Upload = false,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                MarketingVersion = "2.1.0",
                                BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            Assert.True(archiveCalled);
            var appResult = Assert.Single(result.AppleApps);
            Assert.NotNull(appResult.VersionUpdate);
            Assert.Equal("1.0.0", appResult.VersionUpdate!.Before.MarketingVersion);
            Assert.Equal("7", appResult.VersionUpdate.Before.BuildNumber);
            Assert.Equal("2.1.0", appResult.VersionUpdate.After.MarketingVersion);
            Assert.Equal("8", appResult.VersionUpdate.After.BuildNumber);
            Assert.NotNull(appResult.Archive);
            Assert.Null(appResult.Upload);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_NormalizesPbxprojPathForArchive()
    {
        var root = CreateSandbox();
        try
        {
            var xcodeproj = CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Upload = false,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = Path.Combine("Tactra.xcodeproj", "project.pbxproj"),
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            var app = Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Equal(xcodeproj, app.ProjectPath);
            Assert.False(app.IsWorkspace);

            var archiveRequest = Assert.Single(archiveRequests);
            Assert.Equal(xcodeproj, archiveRequest.ProjectPath);
            Assert.False(archiveRequest.IsWorkspace);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsWorkspaceVersionUpdatesBeforeArchive()
    {
        var root = CreateSandbox();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Tactra.xcworkspace"));

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra Workspace",
                                ProjectPath = "Tactra.xcworkspace",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                MarketingVersion = "2.1.0"
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains(".xcworkspace", ex.Message, StringComparison.Ordinal);
            Assert.Contains(".xcodeproj", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_StopsAfterFirstAppFailure()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "First.xcodeproj");
            CreateXcodeProject(root, "Second.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();
            var uploadRequests = new List<AppleAppArchiveUploadRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(65, string.Empty, "archive-failed", "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: request =>
                {
                    uploadRequests.Add(request);
                    throw new InvalidOperationException("Upload should not run after archive failure.");
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Upload = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "First",
                                ProjectPath = "First.xcodeproj",
                                Scheme = "First",
                                Platform = ApplePlatform.iOS
                            },
                            new AppleAppConfiguration
                            {
                                Name = "Second",
                                ProjectPath = "Second.xcodeproj",
                                Scheme = "Second",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.False(result.Success);
            Assert.Contains("First", result.ErrorMessage, StringComparison.Ordinal);
            var appResult = Assert.Single(result.AppleApps);
            Assert.Equal("First", appResult.Plan.Name);
            Assert.False(appResult.Success);
            Assert.Single(archiveRequests);
            Assert.Empty(uploadRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_HonorsTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Phone.xcodeproj");
            CreateXcodeProject(root, "Mac.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra iPhone",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Phone.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            },
                            new AppleAppConfiguration
                            {
                                Name = "Tactra Mac",
                                BundleId = "com.evotecit.tactra.mac",
                                ProjectPath = "Mac.xcodeproj",
                                Scheme = "TactraMac",
                                Platform = ApplePlatform.macOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "Tactra Mac" }
                });

            Assert.True(result.Success);
            var app = Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Equal("Tactra Mac", app.Name);
            Assert.Equal(ApplePlatform.macOS, app.Platform);
            Assert.Single(archiveRequests);
            Assert.Equal(Path.Combine(root, "Mac.xcodeproj"), archiveRequests[0].ProjectPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsUnknownTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "MissingApp" }
                }));

            Assert.Contains("Unknown release target", ex.Message, StringComparison.Ordinal);
            Assert.Contains("MissingApp", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsPartiallyUnknownTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<ArgumentException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "Tactra", "MissingApp" }
                }));

            Assert.Contains("Unknown release target", ex.Message, StringComparison.Ordinal);
            Assert.Contains("MissingApp", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedDotNetToolsAndAppleApps_AllowsAppleOnlyTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet config should not load for an Apple-only target."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj"
                                }
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Single(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedExternalDotNetToolsAndAppleApps_AllowsAppleOnlyTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("External DotNet config should not load for an Apple-only target."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishConfigPath = "missing-dotnet-publish.json"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Single(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedExternalDotNetToolsAndAppleApps_SkipsExistingDotNetConfigForAppleBundleIdTarget()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            File.WriteAllText(Path.Combine(root, "dotnet-publish.json"), "{ invalid json");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishConfigPath = "dotnet-publish.json"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedExternalDotNetToolsAndAppleApps_RunsSharedTargetInBothSections()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var dotNetConfigPath = Path.Combine(root, "dotnet-publish.json");
            File.WriteAllText(dotNetConfigPath, """
{
  "Targets": [
    {
      "Name": "Tactra",
      "ProjectPath": "Tactra.Cli.csproj"
    }
  ]
}
""");
            var plannedToolTargets = Array.Empty<string>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec
                {
                    Targets = new[]
                    {
                        new DotNetPublishTarget
                        {
                            Name = "Tactra",
                            ProjectPath = "Tactra.Cli.csproj"
                        }
                    }
                }, dotNetConfigPath),
                planDotNetTools: (_, _, request, _) =>
                {
                    plannedToolTargets = request.Targets;
                    return new DotNetPublishPlan
                    {
                        ProjectRoot = root,
                        Configuration = "Release",
                        Targets = new[]
                        {
                            new DotNetPublishTargetPlan
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.Cli.csproj",
                                Combinations = Array.Empty<DotNetPublishTargetCombination>()
                            }
                        }
                    };
                },
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run in plan mode."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run in plan mode."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishConfigPath = "dotnet-publish.json"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "Tactra" }
                });

            Assert.True(result.Success);
            Assert.Equal(new[] { "Tactra" }, plannedToolTargets);
            Assert.NotNull(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedExternalDotNetToolsAndAppleApps_RespectsDotNetPublishProfileTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var dotNetConfigPath = Path.Combine(root, "dotnet-publish.json");
            File.WriteAllText(dotNetConfigPath, """
{
  "Profile": "tools",
  "Profiles": [
    {
      "Name": "tools",
      "Targets": [ "PowerForge" ]
    }
  ],
  "Targets": [
    {
      "Name": "Tactra",
      "ProjectPath": "Tactra.Cli.csproj"
    },
    {
      "Name": "PowerForge",
      "ProjectPath": "PowerForge.Cli.csproj"
    }
  ]
}
""");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, _) => (new DotNetPublishSpec
                {
                    Profile = "tools",
                    Profiles = new[]
                    {
                        new DotNetPublishProfile
                        {
                            Name = "tools",
                            Targets = new[] { "PowerForge" }
                        }
                    },
                    Targets = new[]
                    {
                        new DotNetPublishTarget
                        {
                            Name = "Tactra",
                            ProjectPath = "Tactra.Cli.csproj"
                        },
                        new DotNetPublishTarget
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj"
                        }
                    }
                }, dotNetConfigPath),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run for a target excluded by the active profile."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run in plan mode."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run in plan mode."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishConfigPath = "dotnet-publish.json"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "Tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedInlineDotNetToolsAndAppleApps_RespectsDotNetPublishProfileOverrideBeforeTargetMatching()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var plannedToolTargets = Array.Empty<string>();
            var dotNetPublish = new DotNetPublishSpec
            {
                Profile = "tools",
                Profiles = new[]
                {
                    new DotNetPublishProfile
                    {
                        Name = "tools",
                        Targets = new[] { "PowerForge" }
                    },
                    new DotNetPublishProfile
                    {
                        Name = "apple",
                        Targets = new[] { "Tactra" }
                    }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "Tactra",
                        ProjectPath = "Tactra.Cli.csproj"
                    },
                    new DotNetPublishTarget
                    {
                        Name = "PowerForge",
                        ProjectPath = "PowerForge.Cli.csproj"
                    }
                }
            };

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (dotNetPublish, configPath),
                planDotNetTools: (_, _, request, _) =>
                {
                    plannedToolTargets = request.Targets;
                    return new DotNetPublishPlan
                    {
                        ProjectRoot = root,
                        Configuration = "Release",
                        Targets = new[]
                        {
                            new DotNetPublishTargetPlan
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.Cli.csproj",
                                Combinations = Array.Empty<DotNetPublishTargetCombination>()
                            }
                        }
                    };
                },
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run in plan mode."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run in plan mode."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublishProfile = "apple",
                        DotNetPublish = dotNetPublish
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "Tactra" }
                });

            Assert.True(result.Success);
            Assert.Equal("apple", dotNetPublish.Profile);
            Assert.Equal(new[] { "Tactra" }, plannedToolTargets);
            Assert.NotNull(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedDotNetToolsAndAppleApps_RunsSharedTargetInBothSections()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();
            var plannedToolTargets = Array.Empty<string>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec
                {
                    Targets = new[]
                    {
                        new DotNetPublishTarget
                        {
                            Name = "Tactra",
                            ProjectPath = "Tactra.Cli.csproj"
                        }
                    }
                }, configPath),
                planDotNetTools: (_, _, request, _) =>
                {
                    plannedToolTargets = request.Targets;
                    return new DotNetPublishPlan
                    {
                        ProjectRoot = root,
                        Configuration = "Release",
                        Targets = new[]
                        {
                            new DotNetPublishTargetPlan
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.Cli.csproj",
                                Combinations = Array.Empty<DotNetPublishTargetCombination>()
                            }
                        }
                    };
                },
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run in plan mode."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "Tactra",
                                    ProjectPath = "Tactra.Cli.csproj"
                                }
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "Tactra" }
                });

            Assert.True(result.Success);
            Assert.Equal(new[] { "Tactra" }, plannedToolTargets);
            Assert.NotNull(result.DotNetToolPlan);
            Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Empty(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedLegacyToolsAndAppleApps_AllowsAppleOnlyTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EmailIMO.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run for an Apple-only target."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run for an Apple-only target."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets = new[]
                        {
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "emailimo backend macOS arm64"
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "EmailIMO Mac",
                                BundleId = "com.codebybedizen.emailimo",
                                ProjectPath = "EmailIMO.xcodeproj",
                                Scheme = "EmailIMO",
                                Platform = ApplePlatform.macOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "EmailIMO Mac" }
                });

            Assert.True(result.Success);
            Assert.Null(result.ToolPlan);
            var app = Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Equal("EmailIMO Mac", app.Name);
            Assert.Single(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedModulePackagesAndAppleApps_AllowsAppleOnlyTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run for an Apple-only target."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    return new AppleAppArchiveResult
                    {
                        ArchivePath = request.ArchivePath!,
                        Destination = request.Destination!,
                        ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                    };
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ScriptPath = "Missing-Build-Module.ps1"
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.ModulePlan);
            Assert.Null(result.Packages);
            Assert.Single(result.AppleAppPlan!.Apps);
            Assert.Single(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedLegacyToolsAndAppleApps_AllowsToolOnlyTargetFilter()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "EmailIMO.xcodeproj");
            var plannedTargets = Array.Empty<string>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, request) =>
                {
                    plannedTargets = request.Targets;
                    return new PowerForgeToolReleasePlan
                    {
                        ProjectRoot = root,
                        Configuration = "Release",
                        Targets = new[]
                        {
                            new PowerForgeToolReleaseTargetPlan
                            {
                                Name = "emailimo backend macOS arm64",
                                ProjectPath = "Backend.csproj",
                                OutputName = "EmailImo.Backend",
                                Version = "1.0.0",
                                ArtifactRootPath = root,
                                Combinations = Array.Empty<PowerForgeToolReleaseCombinationPlan>()
                            }
                        }
                    };
                },
                runTools: _ => throw new InvalidOperationException("Tools should not run in plan mode."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple apps should not run for a tool-only target."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets = new[]
                        {
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "emailimo backend macOS arm64"
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "EmailIMO Mac",
                                BundleId = "com.codebybedizen.emailimo",
                                ProjectPath = "EmailIMO.xcodeproj",
                                Scheme = "EmailIMO",
                                Platform = ApplePlatform.macOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "emailimo backend macOS arm64" }
                });

            Assert.True(result.Success);
            Assert.Equal(new[] { "emailimo backend macOS arm64" }, plannedTargets);
            Assert.NotNull(result.ToolPlan);
            Assert.Null(result.AppleAppPlan);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedToolsAndAppleApps_ValidatesAppleBeforePublishingTools()
    {
        var root = CreateSandbox();
        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tool planning should not run before Apple validation succeeds."),
                runTools: _ => throw new InvalidOperationException("Tools should not run before Apple validation succeeds."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run after validation failure."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var ex = Assert.Throws<FileNotFoundException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets = new[]
                        {
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "PowerForge"
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Missing.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains("Missing.xcodeproj", ex.FileName, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedPackagesAndAppleApps_ValidatesAppleBeforePublishingPackages()
    {
        var root = CreateSandbox();
        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run before Apple validation succeeds."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run after validation failure."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var ex = Assert.Throws<FileNotFoundException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Missing.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains("Missing.xcodeproj", ex.FileName, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_AllowsCustomXcodeConfiguration()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Configuration = "AppStore",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal("AppStore", result.AppleAppPlan!.Configuration);
            Assert.Equal("AppStore", Assert.Single(result.AppleAppPlan.Apps).Configuration);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_AllowsCustomRequestConfigurationForAppleOnlyTarget()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run for an Apple-only target."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run for an Apple-only target."),
                runTools: _ => throw new InvalidOperationException("Tools should not run for an Apple-only target."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run for an Apple-only target."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run in plan mode."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ScriptPath = "Missing-Build-Module.ps1"
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets = new[]
                        {
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "PowerForge",
                                ProjectPath = "PowerForge.Cli.csproj"
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    Configuration = "AppStore",
                    PlanOnly = true,
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.ModulePlan);
            Assert.Null(result.Packages);
            Assert.Null(result.ToolPlan);
            Assert.Equal("AppStore", result.AppleAppPlan!.Configuration);
            Assert.Equal("AppStore", Assert.Single(result.AppleAppPlan.Apps).Configuration);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_UsesModuleVersionForResolvedVersion()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ModuleVersion = "2.3.4"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                UseResolvedVersion = true
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal("2.3.4", result.ModulePlan!.ModuleVersion);
            Assert.Equal("2.3.4", Assert.Single(result.AppleAppPlan!.Apps).MarketingVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_TargetFilterUsesConfiguredModuleVersionWithoutRunningModule()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ModuleVersion = "2.3.4"
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                UseResolvedVersion = true
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    Targets = new[] { "com.evotecit.tactra" }
                });

            Assert.True(result.Success);
            Assert.Null(result.ModulePlan);
            Assert.Equal("2.3.4", Assert.Single(result.AppleAppPlan!.Apps).MarketingVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedModuleDotNetToolsAndAppleApps_DoesNotApplyModuleVersionToTools()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var buildScript = Path.Combine(root, "Module", "Build", "Build-Module.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(buildScript)!);
            File.WriteAllText(buildScript, "# test build script");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec
                {
                    Targets = new[]
                    {
                        new DotNetPublishTarget
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj"
                        }
                    }
                }, configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Version"] = "9.9.9",
                        ["PackageVersion"] = "9.9.9"
                    },
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            Combinations = Array.Empty<DotNetPublishTargetCombination>()
                        }
                    }
                },
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run in plan mode."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Apple archive should not run in plan mode."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = ".",
                        ModuleVersion = "2.3.4"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj"
                                }
                            }
                        }
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                UseResolvedVersion = true
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.Equal("9.9.9", result.DotNetToolPlan!.MsBuildProperties["Version"]);
            Assert.Equal("9.9.9", result.DotNetToolPlan.MsBuildProperties["PackageVersion"]);
            Assert.Equal("2.3.4", Assert.Single(result.AppleAppPlan!.Apps).MarketingVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsSkipBuildWhenArchiveIsEnabled()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archiveRequests = new List<AppleAppArchiveRequest>();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request =>
                {
                    archiveRequests.Add(request);
                    throw new InvalidOperationException("Archive should not run when SkipBuild is set.");
                },
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run."));

            var ex = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    SkipBuild = true
                }));

            Assert.Contains("SkipBuild", ex.Message, StringComparison.Ordinal);
            Assert.Contains("AppleApps.Archive", ex.Message, StringComparison.Ordinal);
            Assert.Empty(archiveRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsVersionUpdatesWhenArchiveIsDisabled()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        Upload = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS,
                                MarketingVersion = "1.2.3"
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                }));

            Assert.Contains("version updates", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AppleApps.Archive=true", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsMissingReuseArchiveBeforeUpload()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<FileNotFoundException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        Upload = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains("Tactra.xcarchive", ex.FileName, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_MixedPackagesAndAppleApps_ValidatesMissingReuseArchiveBeforePublishingPackages()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run before Apple archive reuse validation succeeds."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: _ => throw new InvalidOperationException("Archive should not run when archive reuse validation fails."),
                uploadAppleApp: _ => throw new InvalidOperationException("Upload should not run when archive reuse validation fails."));

            var ex = Assert.Throws<FileNotFoundException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = "."
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        Upload = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                }));

            Assert.Contains("Tactra.xcarchive", ex.FileName, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_PlanOnly_AllowsMissingReuseArchive()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Archive = false,
                        Upload = true,
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal(Path.Combine(root, "Artifacts", "Apple", "Archives", "iOS", "Tactra.xcarchive"),
                Assert.Single(result.AppleAppPlan!.Apps).ArchivePath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_RejectsExistingNonProjectDirectoryBeforeArchive()
    {
        var root = CreateSandbox();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Source"));

            var service = new PowerForgeReleaseService(new NullLogger());
            var ex = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Source",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                }));

            Assert.Contains(".xcodeproj", ex.Message, StringComparison.Ordinal);
            Assert.Contains(".xcworkspace", ex.Message, StringComparison.Ordinal);
            Assert.Contains(source.FullName, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_NormalizesTrailingSeparatorProjectPath()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = CreateXcodeProject(root, "Tactra.xcodeproj");

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                ProjectPath = "Tactra.xcodeproj" + Path.DirectorySeparatorChar,
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal(projectPath, Assert.Single(result.AppleAppPlan!.Apps).ProjectPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleApps_WritesUnifiedManifestSection()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Tactra.xcodeproj");
            var archivePath = Path.Combine(root, "Artifacts", "Apple", "Archives", "iOS", "Tactra.xcarchive");
            var exportPath = Path.Combine(root, "Artifacts", "Apple", "Exports", "iOS", "Tactra");

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools should not run."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools should not run."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                archiveAppleApp: request => new AppleAppArchiveResult
                {
                    ArchivePath = archivePath,
                    Destination = request.Destination!,
                    ProcessResult = new ProcessRunResult(0, "archive-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                },
                uploadAppleApp: request => new AppleAppArchiveUploadResult
                {
                    ArchivePath = request.ArchivePath,
                    ExportPath = exportPath,
                    ExportOptionsPlistPath = Path.Combine(exportPath, "ExportOptions.plist"),
                    ProcessResult = new ProcessRunResult(0, "upload-ok", string.Empty, "xcodebuild", TimeSpan.FromSeconds(1), false)
                });

            var summaryManifest = Path.Combine(root, "release-manifest.json");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        ManifestJsonPath = summaryManifest
                    },
                    AppleApps = new PowerForgeAppleReleaseOptions
                    {
                        ProjectRoot = ".",
                        Upload = true,
                        TeamId = "TEAMID",
                        Apps = new[]
                        {
                            new AppleAppConfiguration
                            {
                                Name = "Tactra",
                                BundleId = "com.evotecit.tactra",
                                ProjectPath = "Tactra.xcodeproj",
                                Scheme = "Tactra",
                                Platform = ApplePlatform.iOS
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json")
                });

            Assert.True(result.Success);
            Assert.Equal(summaryManifest, result.ReleaseManifestPath);
            Assert.True(File.Exists(summaryManifest));
            var manifestJson = File.ReadAllText(summaryManifest);
            Assert.Contains("\"appleApps\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"Tactra\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"com.evotecit.tactra\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"ArchivePath\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"ExportPath\"", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"Succeeded\": true", manifestJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_PlanOnly_InstallerPropertyOverridesFlowIntoDotNetInstallerPlan()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var installerPath = Path.Combine(root, "Installer", "Package.wixproj");
            Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);
            File.WriteAllText(installerPath, "<Project />", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(new NullLogger());
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "App",
                                    ProjectPath = "App.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0-windows",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            },
                            Installers = new[]
                            {
                                new DotNetPublishInstaller
                                {
                                    Id = "app.msi",
                                    PrepareFromTarget = "App",
                                    InstallerProjectPath = "Installer/Package.wixproj",
                                    Versioning = new DotNetPublishMsiVersionOptions
                                    {
                                        Enabled = true,
                                        Major = 1,
                                        Minor = 0,
                                        FloorDateUtc = "2026-01-01"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    InstallerMsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ProductName"] = "Custom Chat",
                        ["ProductVersion"] = "9.8.7"
                    }
                });

            Assert.True(result.Success);
            var installer = Assert.Single(result.DotNetToolPlan!.Installers);
            Assert.NotNull(installer.MsBuildProperties);
            Assert.Equal("Custom Chat", installer.MsBuildProperties!["ProductName"]);
            Assert.Equal("9.8.7", installer.MsBuildProperties["ProductVersion"]);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_WorkspaceValidation_RunsBeforePackages()
    {
        var root = CreateSandbox();
        try
        {
            File.WriteAllText(Path.Combine(root, "workspace.validation.json"), """
{
  "SchemaVersion": 1,
  "ProjectRoot": ".",
  "Profiles": [
    { "Name": "full-private" }
  ],
  "Steps": [
    {
      "Id": "dotnet-info",
      "Name": "Dotnet info",
      "Kind": "DotNet",
      "Arguments": [ "--version" ]
    }
  ]
}
""", new UTF8Encoding(false));

            var packageCalls = 0;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    packageCalls++;
                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = "release.json",
                        PlanOutputPath = "plan.json",
                        Result = new ProjectBuildResult()
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    WorkspaceValidation = new PowerForgeWorkspaceValidationOptions
                    {
                        ConfigPath = "workspace.validation.json",
                        Profile = "full-private"
                    },
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        Configuration = "Release"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PackagesOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal(1, packageCalls);
            Assert.NotNull(result.WorkspaceValidationPlan);
            Assert.NotNull(result.WorkspaceValidation);
            Assert.True(result.WorkspaceValidation!.Succeeded);
            Assert.Single(result.WorkspaceValidation.Steps);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_PackageSigningOverrides_ApplyToPackages()
    {
        ProjectBuildConfiguration? capturedPackages = null;

        var service = new PowerForgeReleaseService(
            new NullLogger(),
            executePackages: (_, config, _) =>
            {
                capturedPackages = config;
                return new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    ConfigPath = "release.json",
                    PlanOutputPath = "plan.json",
                    Result = new ProjectBuildResult()
                };
            },
            planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
            runTools: _ => throw new InvalidOperationException("Tools should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

        var result = service.Execute(
            new PowerForgeReleaseSpec
            {
                Packages = new ProjectBuildConfiguration
                {
                    RootPath = ".",
                    Configuration = "Release",
                    CertificateThumbprint = "OLD",
                    CertificateStore = "CurrentUser",
                    TimeStampServer = "http://old.example"
                }
            },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                PackagesOnly = true,
                PlanOnly = true,
                PackageSignThumbprint = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
                PackageSignStore = "LocalMachine",
                PackageSignTimestampUrl = "http://timestamp.example"
            });

        Assert.True(result.Success);
        Assert.NotNull(capturedPackages);
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", capturedPackages!.CertificateThumbprint);
        Assert.Equal("LocalMachine", capturedPackages.CertificateStore);
        Assert.Equal("http://timestamp.example", capturedPackages.TimeStampServer);
    }

    [Fact]
    public void Execute_KeepSymbolsAndSigningOverrides_ApplyToDotNetTargetsAndInstallers()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            var installerPath = Path.Combine(root, "PowerForge.Installer.wixproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            File.WriteAllText(installerPath, "<Project />", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            },
                            Installers = new[]
                            {
                                new DotNetPublishInstaller
                                {
                                    Id = "PowerForge.Msi",
                                    PrepareFromTarget = "PowerForge",
                                    InstallerProjectPath = "PowerForge.Installer.wixproj"
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    KeepSymbols = true,
                    EnableSigning = true,
                    SignThumbprint = "ABCDEF0123456789ABCDEF0123456789ABCDEF01",
                    SignDescription = "PowerForge",
                    SignOnMissingTool = DotNetPublishPolicyMode.Fail,
                    SignOnFailure = DotNetPublishPolicyMode.Skip,
                    SignTimeoutSeconds = 42
                });

            Assert.True(result.Success);
            Assert.NotNull(result.DotNetToolPlan);

            var target = Assert.Single(result.DotNetToolPlan!.Targets);
            Assert.True(target.Publish.KeepSymbols);
            Assert.NotNull(target.Publish.Sign);
            Assert.True(target.Publish.Sign!.Enabled);
            Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", target.Publish.Sign.Thumbprint);
            Assert.Equal("PowerForge", target.Publish.Sign.Description);
            Assert.Equal(DotNetPublishPolicyMode.Fail, target.Publish.Sign.OnMissingTool);
            Assert.Equal(DotNetPublishPolicyMode.Skip, target.Publish.Sign.OnSignFailure);
            Assert.Equal(42, target.Publish.Sign.TimeoutSeconds);

            var installer = Assert.Single(result.DotNetToolPlan.Installers);
            Assert.NotNull(installer.Sign);
            Assert.True(installer.Sign!.Enabled);
            Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", installer.Sign.Thumbprint);
            Assert.Equal("PowerForge", installer.Sign.Description);
            Assert.Equal(DotNetPublishPolicyMode.Fail, installer.Sign.OnMissingTool);
            Assert.Equal(DotNetPublishPolicyMode.Skip, installer.Sign.OnSignFailure);
            Assert.Equal(42, installer.Sign.TimeoutSeconds);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_SkipRestoreAndBuild_ApplyToDotNetToolPlan()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release",
                                Restore = true,
                                Build = true
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    SkipRestore = true,
                    SkipBuild = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.DotNetToolPlan);
            Assert.False(result.DotNetToolPlan!.Restore);
            Assert.False(result.DotNetToolPlan.Build);
            Assert.True(result.DotNetToolPlan.NoRestoreInPublish);
            Assert.True(result.DotNetToolPlan.NoBuildInPublish);
            Assert.DoesNotContain(result.DotNetToolPlan.Steps, step => step.Kind == DotNetPublishStepKind.Restore);
            Assert.DoesNotContain(result.DotNetToolPlan.Steps, step => step.Kind == DotNetPublishStepKind.Build);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_OutputRootOverride_RewritesDotNetPublishOutputs()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            var installerPath = Path.Combine(root, "PowerForge.Installer.wixproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            File.WriteAllText(installerPath, "<Project />", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            },
                            Bundles = new[]
                            {
                                new DotNetPublishBundle
                                {
                                    Id = "portable",
                                    PrepareFromTarget = "PowerForge",
                                    Zip = true
                                }
                            },
                            Installers = new[]
                            {
                                new DotNetPublishInstaller
                                {
                                    Id = "PowerForge.Msi",
                                    PrepareFromTarget = "PowerForge",
                                    InstallerProjectPath = "PowerForge.Installer.wixproj",
                                    Harvest = DotNetPublishMsiHarvestMode.Auto
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    OutputRoot = "CustomOutput"
                });

            Assert.True(result.Success);
            Assert.NotNull(result.DotNetToolPlan);

            var plan = result.DotNetToolPlan!;
            var target = Assert.Single(plan.Targets);
            Assert.Equal(
                Path.Combine("CustomOutput", "Artifacts", "DotNetPublish", "{target}", "{rid}", "{framework}", "{style}").Replace('\\', '/'),
                (target.Publish.OutputPath ?? string.Empty).Replace('\\', '/'));

            var bundle = Assert.Single(plan.Bundles);
            Assert.Equal(
                Path.Combine("CustomOutput", "Artifacts", "DotNetPublish", "Bundles", "{bundle}", "{rid}", "{framework}", "{style}").Replace('\\', '/'),
                (bundle.OutputPath ?? string.Empty).Replace('\\', '/'));

            var installer = Assert.Single(plan.Installers);
            Assert.Equal(
                Path.Combine("CustomOutput", "Artifacts", "DotNetPublish", "Msi", "{installer}", "{target}", "{rid}", "{framework}", "{style}", "payload").Replace('\\', '/'),
                (installer.StagingPath ?? string.Empty).Replace('\\', '/'));
            Assert.Equal(Path.Combine(root, "CustomOutput", "Artifacts", "DotNetPublish", "manifest.json"), plan.Outputs.ManifestJsonPath);
            Assert.Equal(Path.Combine(root, "CustomOutput", "Artifacts", "DotNetPublish", "run-report.json"), plan.Outputs.RunReportPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_SignProfileOverride_AppliesToDotNetTargetsAndInstallers()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            var installerPath = Path.Combine(root, "PowerForge.Installer.wixproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));
            File.WriteAllText(installerPath, "<Project />", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            SigningProfiles = new Dictionary<string, DotNetPublishSignOptions>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["LocalCert"] = new()
                                {
                                    Enabled = true,
                                    SubjectName = "Local Software Cert"
                                },
                                ["SafeNetToken"] = new()
                                {
                                    Enabled = true,
                                    Csp = "SafeNet eToken Base Cryptographic Provider",
                                    KeyContainer = "Token Container"
                                }
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat,
                                        SignProfile = "LocalCert"
                                    }
                                }
                            },
                            Installers = new[]
                            {
                                new DotNetPublishInstaller
                                {
                                    Id = "PowerForge.Msi",
                                    PrepareFromTarget = "PowerForge",
                                    InstallerProjectPath = "PowerForge.Installer.wixproj",
                                    SignProfile = "LocalCert"
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    SignProfile = "SafeNetToken",
                    SignDescription = "Release signing"
                });

            Assert.True(result.Success);
            Assert.NotNull(result.DotNetToolPlan);

            var target = Assert.Single(result.DotNetToolPlan!.Targets);
            Assert.NotNull(target.Publish.Sign);
            Assert.True(target.Publish.Sign!.Enabled);
            Assert.Equal("SafeNet eToken Base Cryptographic Provider", target.Publish.Sign.Csp);
            Assert.Equal("Token Container", target.Publish.Sign.KeyContainer);
            Assert.Equal("Release signing", target.Publish.Sign.Description);

            var installer = Assert.Single(result.DotNetToolPlan.Installers);
            Assert.NotNull(installer.Sign);
            Assert.True(installer.Sign!.Enabled);
            Assert.Equal("SafeNet eToken Base Cryptographic Provider", installer.Sign.Csp);
            Assert.Equal("Token Container", installer.Sign.KeyContainer);
            Assert.Equal("Release signing", installer.Sign.Description);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_OutputRootOverrideOutsideProjectRoot_RequiresExplicitAllowFlags()
    {
        var root = CreateSandbox();
        var outsideRoot = Path.Combine(Path.GetTempPath(), "ixpf-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var blocked = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    OutputRoot = outsideRoot
                }));
            Assert.Contains("outside ProjectRoot", blocked.Message, StringComparison.OrdinalIgnoreCase);

            var allowed = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec
                        {
                            DotNet = new DotNetPublishDotNetOptions
                            {
                                ProjectRoot = ".",
                                Configuration = "Release"
                            },
                            Targets = new[]
                            {
                                new DotNetPublishTarget
                                {
                                    Name = "PowerForge",
                                    ProjectPath = "PowerForge.Cli.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = new[] { "win-x64" },
                                        Style = DotNetPublishStyle.PortableCompat
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    OutputRoot = outsideRoot,
                    AllowOutputOutsideProjectRoot = true,
                    AllowManifestOutsideProjectRoot = true
                });

            Assert.True(allowed.Success);
            Assert.NotNull(allowed.DotNetToolPlan);
            Assert.True(allowed.DotNetToolPlan!.AllowOutputOutsideProjectRoot);
            Assert.True(allowed.DotNetToolPlan.AllowManifestOutsideProjectRoot);
            Assert.Equal(Path.Combine(outsideRoot, "Artifacts", "DotNetPublish", "manifest.json"), allowed.DotNetToolPlan.Outputs.ManifestJsonPath);
        }
        finally
        {
            TryDelete(root);
            TryDelete(outsideRoot);
        }
    }

    [Fact]
    public void Execute_PublishesDotNetPublishAssetsToGitHub()
    {
        var zip = Path.GetTempFileName();
        var msi = Path.GetTempFileName();
        var storeUpload = Path.GetTempFileName();
        var manifest = Path.GetTempFileName();
        var checksums = Path.GetTempFileName();
        try
        {
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (
                    new DotNetPublishSpec
                    {
                        Targets = new[]
                        {
                            new DotNetPublishTarget
                            {
                                Name = "PowerForge",
                                ProjectPath = "PowerForge.Cli.csproj",
                                Publish = new DotNetPublishPublishOptions
                                {
                                    Framework = "net10.0",
                                    Runtimes = new[] { "win-x64" },
                                    Style = DotNetPublishStyle.PortableCompat,
                                    Zip = true
                                }
                            }
                        }
                    },
                    configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = Path.GetTempPath(),
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    ManifestJsonPath = manifest,
                    ChecksumsPath = checksums,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Target = "PowerForge",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = Path.GetTempPath(),
                            ZipPath = zip
                        }
                    },
                    MsiBuilds = new[]
                    {
                        new DotNetPublishMsiBuildResult
                        {
                            InstallerId = "PowerForgeMsi",
                            Target = "PowerForge",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            Version = "1.2.3",
                            OutputFiles = new[] { msi }
                        }
                    },
                    StorePackages = new[]
                    {
                        new DotNetPublishStorePackageResult
                        {
                            StorePackageId = "PowerForge.Store",
                            Target = "PowerForge",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = Path.GetTempPath(),
                            UploadFiles = new[] { storeUpload }
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release",
                        ReusedExistingRelease = true
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec(),
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "PSPublishModule",
                            Token = "token",
                            TagTemplate = "{Target}-v{Version}",
                            ReleaseNameTemplate = "{Target} {Version}"
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);

            var publish = Assert.Single(publishCalls);
            Assert.Equal("PowerForge-v1.2.3", publish.TagName);
            Assert.Equal("PowerForge 1.2.3", publish.ReleaseName);
            Assert.Contains(zip, publish.AssetFilePaths);
            Assert.Contains(msi, publish.AssetFilePaths);
            Assert.Contains(storeUpload, publish.AssetFilePaths);
            Assert.Contains(manifest, publish.AssetFilePaths);
            Assert.Contains(checksums, publish.AssetFilePaths);

            var release = Assert.Single(result.ToolGitHubReleases);
            Assert.True(release.Success);
            Assert.Equal(5, release.AssetPaths.Length);
        }
        finally
        {
            TryDelete(zip);
            TryDelete(msi);
            TryDelete(storeUpload);
            TryDelete(manifest);
            TryDelete(checksums);
        }
    }

    [Fact]
    public void Execute_PlanOnly_UsesResolvedPackageVersionForDotNetPublishPlan()
    {
        var service = new PowerForgeReleaseService(
            new NullLogger(),
            executePackages: (_, _, _) =>
            {
                var release = new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    ResolvedVersion = "0.1.5"
                };
                release.ResolvedVersionsByProject["IntelligenceX"] = "0.1.5";

                return new ProjectBuildHostExecutionResult
                {
                    Success = true,
                    Result = new ProjectBuildResult
                    {
                        Success = true,
                        Release = release
                    }
                };
            },
            planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
            runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
            loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
            planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
            {
                ProjectRoot = Path.GetTempPath(),
                Configuration = "Release"
            },
            runDotNetTools: _ => throw new InvalidOperationException("DotNet publish should not run."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

        var result = service.Execute(
            new PowerForgeReleaseSpec
            {
                Packages = new ProjectBuildConfiguration
                {
                    GitHubPrimaryProject = "IntelligenceX"
                },
                Tools = new PowerForgeToolReleaseSpec
                {
                    DotNetPublish = new DotNetPublishSpec()
                }
            },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                PlanOnly = true
            });

        Assert.True(result.Success);
        Assert.NotNull(result.DotNetToolPlan);
        Assert.Equal("0.1.5", result.DotNetToolPlan!.MsBuildProperties["Version"]);
        Assert.Equal("0.1.5", result.DotNetToolPlan.MsBuildProperties["PackageVersion"]);
        Assert.Equal("0.1.5", result.DotNetToolPlan.MsBuildProperties["AssemblyVersion"]);
        Assert.Equal("0.1.5", result.DotNetToolPlan.MsBuildProperties["FileVersion"]);
        Assert.Equal("0.1.5", result.DotNetToolPlan.MsBuildProperties["InformationalVersion"]);
    }

    [Fact]
    public void Execute_PublishesDotNetPublishAssetsToGitHub_PrefersResolvedPackageVersionOverProjectVersion()
    {
        var root = CreateSandbox();
        var zip = Path.Combine(root, "tray-portable.zip");
        var projectPath = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(zip, "zip", new UTF8Encoding(false));
        File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>9.9.9</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    var release = new DotNetRepositoryReleaseResult
                    {
                        Success = true,
                        ResolvedVersion = "0.1.5"
                    };
                    release.ResolvedVersionsByProject["IntelligenceX"] = "0.1.5";

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = projectPath,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            ZipPath = zip
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release",
                        ReusedExistingRelease = true
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        GitHubPrimaryProject = "IntelligenceX"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec(),
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "IntelligenceX",
                            Token = "token",
                            TagTemplate = "{Target}-v{Version}",
                            ReleaseNameTemplate = "{Target} {Version}"
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json")
                });

            Assert.True(result.Success);
            var publish = Assert.Single(publishCalls);
            Assert.Equal("IntelligenceX.Tray-v0.1.5", publish.TagName);
            Assert.Equal("IntelligenceX.Tray 0.1.5", publish.ReleaseName);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_PublishesDotNetPublishPreviewAssetsToStablePreviewTag()
    {
        var root = CreateSandbox();
        var zip = Path.GetTempFileName();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Web.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (
                    new DotNetPublishSpec
                    {
                        Targets = new[]
                        {
                            new DotNetPublishTarget
                            {
                                Name = "PowerForgeWeb",
                                ProjectPath = projectPath,
                                Publish = new DotNetPublishPublishOptions
                                {
                                    Framework = "net10.0",
                                    Runtimes = new[] { "win-x64" },
                                    Style = DotNetPublishStyle.PortableCompat,
                                    Zip = true
                                }
                            }
                        }
                    },
                    configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = Path.GetTempPath(),
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForgeWeb",
                            ProjectPath = projectPath,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Target = "PowerForgeWeb",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = Path.GetTempPath(),
                            ZipPath = zip
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release",
                        ReusedExistingRelease = true
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec(),
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "PSPublishModule",
                            Token = "token",
                            TagTemplate = "{Target}-v{Version}-preview",
                            ReleaseNameTemplate = "{Target} {Version} Preview"
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);

            var publish = Assert.Single(publishCalls);
            Assert.Equal("PowerForgeWeb-v1.2.3-preview", publish.TagName);
            Assert.Equal("PowerForgeWeb 1.2.3 Preview", publish.ReleaseName);
            Assert.Single(publish.AssetFilePaths);

            var release = Assert.Single(result.ToolGitHubReleases);
            Assert.Equal("PowerForgeWeb-v1.2.3-preview", release.TagName);
            Assert.Equal("PowerForgeWeb 1.2.3 Preview", release.ReleaseName);
            Assert.True(release.ReusedExistingRelease);
        }
        finally
        {
            TryDelete(root);
            TryDelete(zip);
        }
    }

    [Fact]
    public void Execute_WritesUnifiedReleaseManifestAndChecksums()
    {
        var root = CreateSandbox();
        var zip = Path.Combine(root, "PowerForge-win-x64.zip");
        var manifest = Path.Combine(root, "dotnet-manifest.json");
        var checksums = Path.Combine(root, "dotnet-checksums.txt");
        File.WriteAllText(zip, "zip", new UTF8Encoding(false));
        File.WriteAllText(manifest, "{}", new UTF8Encoding(false));
        File.WriteAllText(checksums, "abc", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (
                    new DotNetPublishSpec
                    {
                        Targets = new[]
                        {
                            new DotNetPublishTarget
                            {
                                Name = "PowerForge",
                                ProjectPath = "PowerForge.Cli.csproj",
                                Publish = new DotNetPublishPublishOptions
                                {
                                    Framework = "net10.0",
                                    Runtimes = new[] { "win-x64" },
                                    Style = DotNetPublishStyle.PortableCompat,
                                    Zip = true
                                }
                            }
                        }
                    },
                    configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    ManifestJsonPath = manifest,
                    ChecksumsPath = checksums,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Target = "PowerForge",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            ZipPath = zip
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var summaryManifest = Path.Combine(root, "release-manifest.json");
            var summaryChecksums = Path.Combine(root, "SHA256SUMS.txt");

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        ManifestJsonPath = summaryManifest,
                        ChecksumsPath = summaryChecksums
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            Assert.Equal(summaryManifest, result.ReleaseManifestPath);
            Assert.Equal(summaryChecksums, result.ReleaseChecksumsPath);
            Assert.Contains(zip, result.ReleaseAssets);
            Assert.Contains(result.ReleaseAssetEntries, entry => string.Equals(entry.Path, zip, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(manifest, result.ReleaseAssets);
            Assert.Contains(checksums, result.ReleaseAssets);
            Assert.DoesNotContain(root, result.ReleaseAssets);
            Assert.True(File.Exists(summaryManifest));
            Assert.True(File.Exists(summaryChecksums));

            var manifestJson = File.ReadAllText(summaryManifest);
            Assert.Contains("PowerForge-win-x64.zip", manifestJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dotnet-manifest.json", manifestJson, StringComparison.OrdinalIgnoreCase);

            var checksumText = File.ReadAllText(summaryChecksums);
            Assert.Contains("PowerForge-win-x64.zip", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("release-manifest.json", checksumText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_StageRoot_CopiesCategorizedAssetsAndWritesSummaryFiles()
    {
        var root = CreateSandbox();
        var package = Path.Combine(root, "IntelligenceX.Tools.Common.0.1.0.nupkg");
        var bundleZip = Path.Combine(root, "IntelligenceX.Chat-Portable-win-x64.zip");
        var msi = Path.Combine(root, "IntelligenceX.Chat.Installer.msi");
        var storeUpload = Path.Combine(root, "IntelligenceX.Chat.Store.msixupload");
        var dotNetManifest = Path.Combine(root, "dotnet-manifest.json");
        File.WriteAllText(package, "pkg", new UTF8Encoding(false));
        File.WriteAllText(bundleZip, "zip", new UTF8Encoding(false));
        File.WriteAllText(msi, "msi", new UTF8Encoding(false));
        File.WriteAllText(storeUpload, "upload", new UTF8Encoding(false));
        File.WriteAllText(dotNetManifest, "{}", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    var release = new DotNetRepositoryReleaseResult();
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "IntelligenceX.Tools.Common",
                        PackageId = "IntelligenceX.Tools.Common",
                        IsPackable = true
                    };
                    project.Packages.Add(package);
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = "release.json",
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Chat.App",
                            ProjectPath = Path.Combine(root, "IntelligenceX.Chat.App.csproj"),
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net8.0-windows10.0.26100.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net8.0-windows10.0.26100.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    ManifestJsonPath = dotNetManifest,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Chat.App",
                            BundleId = "portable",
                            Framework = "net8.0-windows10.0.26100.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = bundleZip
                        }
                    },
                    MsiBuilds = new[]
                    {
                        new DotNetPublishMsiBuildResult
                        {
                            InstallerId = "IntelligenceX.Chat.Installer",
                            Target = "IntelligenceX.Chat.App",
                            Framework = "net8.0-windows10.0.26100.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputFiles = new[] { msi }
                        }
                    },
                    StorePackages = new[]
                    {
                        new DotNetPublishStorePackageResult
                        {
                            StorePackageId = "IntelligenceX.Chat.Store",
                            Target = "IntelligenceX.Chat.App",
                            Framework = "net8.0-windows10.0.26100.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            UploadFiles = new[] { storeUpload }
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var stageRoot = Path.Combine(root, "release");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        Configuration = "Release"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = stageRoot
                });

            Assert.True(result.Success);
            Assert.Equal(Path.Combine(stageRoot, "release-manifest.json"), result.ReleaseManifestPath);
            Assert.Equal(Path.Combine(stageRoot, "SHA256SUMS.txt"), result.ReleaseChecksumsPath);
            Assert.True(File.Exists(Path.Combine(stageRoot, "nuget", Path.GetFileName(package))));
            Assert.True(File.Exists(Path.Combine(stageRoot, "portable", Path.GetFileName(bundleZip))));
            Assert.True(File.Exists(Path.Combine(stageRoot, "installer", Path.GetFileName(msi))));
            Assert.True(File.Exists(Path.Combine(stageRoot, "store", Path.GetFileName(storeUpload))));
            Assert.True(File.Exists(Path.Combine(stageRoot, "metadata", Path.GetFileName(dotNetManifest))));
            Assert.Contains(Path.Combine(stageRoot, "nuget", Path.GetFileName(package)), result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(stageRoot, "portable", Path.GetFileName(bundleZip)), result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);

            var stagedPackage = Assert.Single(
                result.ReleaseAssetEntries,
                entry => string.Equals(entry.Path, package, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("nuget/" + Path.GetFileName(package), stagedPackage.RelativeStagePath!.Replace('\\', '/'));

            var checksumText = File.ReadAllText(Path.Combine(stageRoot, "SHA256SUMS.txt"));
            Assert.Contains("nuget/" + Path.GetFileName(package), checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("portable/" + Path.GetFileName(bundleZip), checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(package.Replace('\\', '/'), checksumText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_StageRoot_AppliesNameTemplatesToStagedAssets()
    {
        var root = CreateSandbox();
        var package = Path.Combine(root, "IntelligenceX.0.1.0.nupkg");
        var bundleZip = Path.Combine(root, "raw-portable.zip");
        var chatProject = Path.Combine(root, "IntelligenceX.Chat.App.csproj");
        File.WriteAllText(package, "pkg", new UTF8Encoding(false));
        File.WriteAllText(bundleZip, "zip", new UTF8Encoding(false));
        File.WriteAllText(chatProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.26100.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    var release = new DotNetRepositoryReleaseResult();
                    var project = new DotNetRepositoryProjectResult
                    {
                        ProjectName = "IntelligenceX",
                        PackageId = "IntelligenceX",
                        IsPackable = true,
                        NewVersion = "0.1.0"
                    };
                    project.Packages.Add(package);
                    release.Projects.Add(project);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        ConfigPath = "release.json",
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Chat.App",
                            ProjectPath = chatProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net8.0-windows10.0.26100.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net8.0-windows10.0.26100.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Chat.App",
                            BundleId = "portable",
                            Framework = "net8.0-windows10.0.26100.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = bundleZip
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var stageRoot = Path.Combine(root, "upload-ready");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        RootPath = ".",
                        Configuration = "Release"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            PackagesPath = "NuGet",
                            PortablePath = "GitHub",
                            PackagesNameTemplate = "{PackageId}.{Version}{Extension}",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = stageRoot
                });

            Assert.True(result.Success);

            var stagedPackagePath = Path.Combine(stageRoot, "NuGet", "IntelligenceX.0.1.0.nupkg");
            var stagedPortablePath = Path.Combine(stageRoot, "GitHub", "IntelligenceX.Chat.App-1.0.0-win-x64-portable.zip");
            Assert.True(File.Exists(stagedPackagePath));
            Assert.True(File.Exists(stagedPortablePath));
            Assert.Contains(stagedPackagePath, result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(stagedPortablePath, result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);

            var checksumText = File.ReadAllText(Path.Combine(stageRoot, "SHA256SUMS.txt"));
            Assert.Contains("NuGet/IntelligenceX.0.1.0.nupkg", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("GitHub/IntelligenceX.Chat.App-1.0.0-win-x64-portable.zip", checksumText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw-portable.zip", checksumText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_GeneratesManifestFromStagedPortableAssets()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray-x64.zip");
        var trayArm64 = Path.Combine(root, "tray-arm64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayArm64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            PowerForgeWingetSubmissionPlan? capturedWingetSubmissionPlan = null;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64", "win-arm64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                },
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-arm64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        },
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-arm64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayArm64
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                submitWinget: plan =>
                {
                    capturedWingetSubmissionPlan = plan;
                    return new PowerForgeWingetSubmissionResult { Succeeded = true };
                });

            var stageRoot = Path.Combine(root, "upload-ready");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            RootPath = "Artifacts/UploadReady",
                            PortablePath = "GitHub",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        Submit = true,
                        OutputPath = "Artifacts/UploadReady/Winget",
                        InstallerUrlTemplate = "https://github.com/EvotecIT/IntelligenceX/releases/download/v{PackageVersion}/{FileName}",
                        Submission = new PowerForgeReleaseWingetSubmissionOptions
                        {
                            Token = "secret-token",
                            PullRequestTitle = "Submit {PackageIdentifier} {PackageVersion}"
                        },
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    },
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-arm64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    StageRoot = stageRoot
                });

            Assert.True(result.Success);
            var manifestPath = Assert.Single(result.WingetManifestPaths);
            Assert.True(File.Exists(manifestPath));

            var yaml = File.ReadAllText(manifestPath);
            Assert.Contains("PackageIdentifier: EvotecIT.IntelligenceX.Tray", yaml, StringComparison.Ordinal);
            Assert.Contains("InstallerType: zip", yaml, StringComparison.Ordinal);
            Assert.Contains("NestedInstallerType: portable", yaml, StringComparison.Ordinal);
            Assert.Contains("IntelligenceX.Tray-1.0.0-win-x64-portable.zip", yaml, StringComparison.Ordinal);
            Assert.Contains("IntelligenceX.Tray-1.0.0-win-arm64-portable.zip", yaml, StringComparison.Ordinal);
            Assert.Contains("RelativeFilePath: IntelligenceX.Tray.exe", yaml, StringComparison.Ordinal);
            Assert.Contains("Architecture: x64", yaml, StringComparison.Ordinal);
            Assert.Contains("Architecture: arm64", yaml, StringComparison.Ordinal);
            var manifest = Assert.Single(result.WingetManifests);
            Assert.Equal("EvotecIT.IntelligenceX.Tray", manifest.PackageIdentifier);
            Assert.Equal(2, manifest.InstallerUrls.Length);
            Assert.NotNull(result.WingetSubmissionPlan);
            Assert.Same(capturedWingetSubmissionPlan, result.WingetSubmissionPlan);
            var submitEntry = Assert.Single(result.WingetSubmissionPlan!.Entries);
            Assert.Equal("submit", submitEntry.RedactedArguments[0]);
            Assert.Contains("***", submitEntry.RedactedArguments);
            Assert.DoesNotContain("secret-token", submitEntry.RedactedArguments);
            Assert.Contains("Submit EvotecIT.IntelligenceX.Tray 1.0.0", submitEntry.RedactedArguments);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_SubmitWinget_ThrowsWhenWingetConfigIsMissing()
    {
        var root = CreateSandbox();
        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = Array.Empty<DotNetPublishTargetPlan>()
                },
                runDotNetTools: _ => new DotNetPublishResult { Succeeded = true },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."),
                submitWinget: _ => throw new InvalidOperationException("Winget should not run."));

            var blocked = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    SubmitWinget = true
                }));

            Assert.Contains("does not define a Winget section", blocked.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_PrefersResolvedPackageVersionOverPortableTargetVersion()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>9.9.9</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) =>
                {
                    var release = new DotNetRepositoryReleaseResult
                    {
                        Success = true,
                        ResolvedVersion = "0.1.5"
                    };
                    release.ResolvedVersionsByProject["IntelligenceX"] = "0.1.5";

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        GitHubPrimaryProject = "IntelligenceX"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            RootPath = "Artifacts/UploadReady",
                            PortablePath = "GitHub",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Artifacts/UploadReady/Winget",
                        InstallerUrlTemplate = "https://github.com/EvotecIT/IntelligenceX/releases/download/v{PackageVersion}/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    StageRoot = Path.Combine(root, "upload-ready")
                });

            Assert.True(result.Success);
            var manifestPath = Assert.Single(result.WingetManifestPaths);
            var yaml = File.ReadAllText(manifestPath);
            Assert.Contains("PackageVersion: 0.1.5", yaml, StringComparison.Ordinal);
            Assert.Contains("IntelligenceX.Tray-0.1.5-win-x64-portable.zip", yaml, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_UsesPublishedToolGitHubReleaseWhenUrlTemplateIsMissing()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        }
                    }
                },
                publishGitHubRelease: _ => new GitHubReleasePublishResult
                {
                    Succeeded = true,
                    ReleaseCreationSucceeded = true,
                    HtmlUrl = "https://github.com/EvotecIT/IntelligenceX/releases/tag/IntelligenceX.Tray+v1.0.0",
                    UploadUrl = "https://uploads.github.com/repos/EvotecIT/IntelligenceX/releases/1/assets{?name,label}"
                });

            var stageRoot = Path.Combine(root, "upload-ready");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec(),
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "IntelligenceX",
                            Token = "token",
                            TagTemplate = "{Target}+v{Version}"
                        }
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            RootPath = "Artifacts/UploadReady",
                            PortablePath = "GitHub",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Artifacts/UploadReady/Winget",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            var manifestPath = Assert.Single(result.WingetManifestPaths);
            var yaml = File.ReadAllText(manifestPath);
            Assert.Contains("https://github.com/EvotecIT/IntelligenceX/releases/download/IntelligenceX.Tray%2Bv1.0.0/IntelligenceX.Tray-1.0.0-win-x64-portable.zip", yaml, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_RelativeOutputPath_UsesResolvedStageRoot_AndIsIncludedInReleaseAssets()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var stageRoot = Path.Combine(root, "upload-ready");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            RootPath = "Artifacts/UploadReady",
                            PortablePath = "GitHub",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        InstallerUrlTemplate = "https://github.com/EvotecIT/IntelligenceX/releases/download/v{PackageVersion}/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    StageRoot = stageRoot
                });

            Assert.True(result.Success);
            var manifestPath = Assert.Single(result.WingetManifestPaths);
            Assert.StartsWith(Path.Combine(stageRoot, "Winget"), manifestPath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(manifestPath, result.ReleaseAssets, StringComparer.OrdinalIgnoreCase);
            Assert.NotNull(result.ReleaseChecksumsPath);
            var releaseChecksums = result.ReleaseChecksumsPath;
            var checksumText = File.ReadAllText(releaseChecksums!);
            Assert.Contains("Winget/", checksumText.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_UnifiedGitHubPublish_UsesStagedReleaseAssets_AndSuppressesPackageGitHubPublish()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        var packagePath = Path.Combine(root, "IntelligenceX.0.1.0.nupkg");
        var releaseZipPath = Path.Combine(root, "IntelligenceX.0.1.0.zip");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(packagePath, "nupkg", new UTF8Encoding(false));
        File.WriteAllText(releaseZipPath, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>9.9.9</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            bool? capturedPackagePublishGitHub = null;
            GitHubReleasePublishRequest? capturedGitHubRequest = null;

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (request, _, _) =>
                {
                    capturedPackagePublishGitHub = request.PublishGitHub;

                    var release = new DotNetRepositoryReleaseResult
                    {
                        Success = true,
                        ResolvedVersion = "0.1.0"
                    };
                    release.ResolvedVersionsByProject["IntelligenceX"] = "0.1.0";
                    release.Projects.Add(new DotNetRepositoryProjectResult
                    {
                        ProjectName = "IntelligenceX",
                        PackageId = "IntelligenceX",
                        IsPackable = true,
                        NewVersion = "0.1.0",
                        ReleaseZipPath = releaseZipPath
                    });
                    release.Projects[0].Packages.Add(packagePath);

                    return new ProjectBuildHostExecutionResult
                    {
                        Success = true,
                        Result = new ProjectBuildResult
                        {
                            Success = true,
                            Release = release
                        }
                    };
                },
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    capturedGitHubRequest = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReleaseCreationSucceeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/IntelligenceX/releases/tag/v0.1.0"
                    };
                });

            var stageRoot = Path.Combine(root, "upload-ready");
            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Packages = new ProjectBuildConfiguration
                    {
                        GitHubUsername = "EvotecIT",
                        GitHubRepositoryName = "IntelligenceX"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        Staging = new PowerForgeReleaseStagingOptions
                        {
                            RootPath = "Artifacts/UploadReady",
                            PackagesPath = "NuGet",
                            PortablePath = "GitHub",
                            PortableNameTemplate = "{Target}-{Version}-{Runtime}-portable{Extension}"
                        }
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        Token = "token"
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        InstallerUrlTemplate = "https://github.com/EvotecIT/IntelligenceX/releases/download/v{PackageVersion}/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = "IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PublishProjectGitHub = true,
                    StageRoot = stageRoot
                });

            Assert.True(result.Success);
            Assert.False(capturedPackagePublishGitHub ?? true);
            Assert.NotNull(result.UnifiedGitHubRelease);
            var unified = result.UnifiedGitHubRelease;
            Assert.True(unified!.Success);
            Assert.Equal("v0.1.0", unified.TagName);
            Assert.NotNull(capturedGitHubRequest);
            Assert.Equal("EvotecIT", capturedGitHubRequest!.Owner);
            Assert.Equal("IntelligenceX", capturedGitHubRequest.Repository);
            Assert.Contains(Path.Combine(stageRoot, "NuGet", "IntelligenceX.0.1.0.nupkg"), capturedGitHubRequest.AssetFilePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(stageRoot, "GitHub", "IntelligenceX.Tray-0.1.0-win-x64-portable.zip"), capturedGitHubRequest.AssetFilePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(stageRoot, "Winget", "EvotecIT.IntelligenceX.Tray.yaml"), capturedGitHubRequest.AssetFilePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(stageRoot, "release-manifest.json"), capturedGitHubRequest.AssetFilePaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(stageRoot, "SHA256SUMS.txt"), capturedGitHubRequest.AssetFilePaths, StringComparer.OrdinalIgnoreCase);
            var manifestText = File.ReadAllText(Path.Combine(stageRoot, "release-manifest.json"));
            Assert.Contains("unifiedGithubRelease", manifestText, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_UsesRawAssetPathWhenStagingDisabled_EncodesUrlAndEscapesYaml()
    {
        var root = CreateSandbox();
        var trayX64 = Path.Combine(root, "tray raw.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayX64, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayX64
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        InstallerUrlTemplate = "https://example.test/downloads/{PackageIdentifier}/{PackageVersion}/{Runtime}/{Framework}/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip",
                                        NestedInstallerType = "portable",
                                        RelativeFilePath = @"IntelligenceX Tray\IntelligenceX.Tray.exe"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            var manifestPath = Assert.Single(result.WingetManifestPaths);
            var yaml = File.ReadAllText(manifestPath);
            Assert.Contains("InstallerUrl: \"https://example.test/downloads/EvotecIT.IntelligenceX.Tray/1.0.0/win-x64/net10.0-windows10.0.19041.0/tray%20raw.zip\"", yaml, StringComparison.Ordinal);
            Assert.Contains("RelativeFilePath: \"IntelligenceX Tray\\\\IntelligenceX.Tray.exe\"", yaml, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_ThrowsWhenPackageIdentifierWouldOverwriteExistingManifest()
    {
        var root = CreateSandbox();
        var trayArchive = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayArchive, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var outputPath = Path.Combine(root, "Winget");
            Directory.CreateDirectory(outputPath);
            File.WriteAllText(Path.Combine(outputPath, "EvotecIT.IntelligenceX.Tray.yaml"), "# existing", new UTF8Encoding(false));

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayArchive
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var blocked = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        InstallerUrlTemplate = "https://example.test/downloads/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                }));

            Assert.Contains("already written", blocked.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_ThrowsWhenRuntimeArchitectureCannotBeInferred()
    {
        var root = CreateSandbox();
        var trayArchive = Path.Combine(root, "tray-unknown.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayArchive, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-custom" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-custom",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-custom",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayArchive
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var blocked = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        InstallerUrlTemplate = "https://example.test/downloads/{FileName}",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-custom",
                                        InstallerType = "zip"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                }));

            Assert.Contains("Could not infer Winget architecture", blocked.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Winget_ThrowsWhenNoInstallerUrlSourceCanBeResolved()
    {
        var root = CreateSandbox();
        var trayArchive = Path.Combine(root, "tray-x64.zip");
        var trayProject = Path.Combine(root, "IntelligenceX.Tray.csproj");
        File.WriteAllText(trayArchive, "zip", new UTF8Encoding(false));
        File.WriteAllText(trayProject, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new DotNetPublishTargetPlan
                        {
                            Name = "IntelligenceX.Tray",
                            ProjectPath = trayProject,
                            Publish = new DotNetPublishPublishOptions
                            {
                                Framework = "net10.0-windows10.0.19041.0",
                                Runtimes = new[] { "win-x64" },
                                Style = DotNetPublishStyle.PortableCompat,
                                Zip = true
                            },
                            Combinations = new[]
                            {
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0-windows10.0.19041.0",
                                    Runtime = "win-x64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            }
                        }
                    }
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Tray",
                            Framework = "net10.0-windows10.0.19041.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            PublishDir = root,
                            ZipPath = trayArchive
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var blocked = Assert.Throws<InvalidOperationException>(() => service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    },
                    Winget = new PowerForgeReleaseWingetOptions
                    {
                        Enabled = true,
                        OutputPath = "Winget",
                        Packages = new[]
                        {
                            new PowerForgeReleaseWingetPackage
                            {
                                PackageIdentifier = "EvotecIT.IntelligenceX.Tray",
                                PackageVersion = "1.0.0",
                                Publisher = "Evotec",
                                PackageName = "IntelligenceX Tray",
                                License = "MIT",
                                ShortDescription = "Windows tray app for IntelligenceX.",
                                Installers = new[]
                                {
                                    new PowerForgeReleaseWingetInstaller
                                    {
                                        Category = PowerForgeReleaseAssetCategory.Portable,
                                        Target = "IntelligenceX.Tray",
                                        Runtime = "win-x64",
                                        InstallerType = "zip"
                                    }
                                }
                            }
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                }));

            Assert.Contains("requires InstallerUrlTemplate", blocked.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ToolOutputSelection_InstallerOnlyKeepsRequiredBundlesButDisablesPortableZips()
    {
        var root = CreateSandbox();
        try
        {
            var spec = new DotNetPublishSpec
            {
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "IntelligenceX.Chat.App",
                        ProjectPath = Path.Combine(root, "IntelligenceX.Chat.App.csproj"),
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = DotNetPublishStyle.PortableCompat,
                            Zip = true
                        }
                    }
                },
                Bundles = new[]
                {
                    new DotNetPublishBundle
                    {
                        Id = "portable",
                        PrepareFromTarget = "IntelligenceX.Chat.App",
                        Zip = true
                    },
                    new DotNetPublishBundle
                    {
                        Id = "unused",
                        PrepareFromTarget = "IntelligenceX.Chat.App",
                        Zip = true
                    }
                },
                Installers = new[]
                {
                    new DotNetPublishInstaller
                    {
                        Id = "IntelligenceX.Chat.Installer",
                        PrepareFromTarget = "IntelligenceX.Chat.App",
                        PrepareFromBundleId = "portable",
                        InstallerProjectPath = Path.Combine(root, "Installer.wixproj")
                    }
                },
                StorePackages = new[]
                {
                    new DotNetPublishStorePackage
                    {
                        Id = "IntelligenceX.Chat.Store",
                        PrepareFromTarget = "IntelligenceX.Chat.App",
                        PackagingProjectPath = Path.Combine(root, "Store.wapproj")
                    }
                }
            };

            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"),
                ToolOutputs = new[] { PowerForgeReleaseToolOutputKind.Installer }
            };

            var selectedOutputs = PowerForgeReleaseService.ResolveSelectedToolOutputs(request);
            PowerForgeReleaseService.ApplyDotNetToolOutputSelection(spec, selectedOutputs);

            var bundle = Assert.Single(spec.Bundles ?? Array.Empty<DotNetPublishBundle>());
            Assert.Equal("portable", bundle.Id);
            Assert.False(bundle.Zip);
            Assert.False(spec.Targets![0].Publish!.Zip);
            Assert.Single(spec.Installers ?? Array.Empty<DotNetPublishInstaller>());
            Assert.Empty(spec.StorePackages ?? Array.Empty<DotNetPublishStorePackage>());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ToolOutputSelection_FiltersReturnedDotNetAssetsToRequestedKinds()
    {
        var root = CreateSandbox();
        var toolZip = Path.Combine(root, "PowerForge-win-x64.zip");
        var bundleZip = Path.Combine(root, "IntelligenceX.Chat-Portable-win-x64.zip");
        var installer = Path.Combine(root, "IntelligenceX.Chat.Installer.msi");
        var storeUpload = Path.Combine(root, "IntelligenceX.Chat.Store.msixupload");
        File.WriteAllText(toolZip, "tool", new UTF8Encoding(false));
        File.WriteAllText(bundleZip, "bundle", new UTF8Encoding(false));
        File.WriteAllText(installer, "installer", new UTF8Encoding(false));
        File.WriteAllText(storeUpload, "store", new UTF8Encoding(false));

        try
        {
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools should not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools should not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Configuration = "Release"
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts = new[]
                    {
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Publish,
                            Target = "PowerForge",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            ZipPath = toolZip
                        },
                        new DotNetPublishArtefactResult
                        {
                            Category = DotNetPublishArtefactCategory.Bundle,
                            Target = "IntelligenceX.Chat.App",
                            BundleId = "portable",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            ZipPath = bundleZip
                        }
                    },
                    MsiBuilds = new[]
                    {
                        new DotNetPublishMsiBuildResult
                        {
                            InstallerId = "IntelligenceX.Chat.Installer",
                            Target = "IntelligenceX.Chat.App",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputFiles = new[] { installer }
                        }
                    },
                    StorePackages = new[]
                    {
                        new DotNetPublishStorePackageResult
                        {
                            StorePackageId = "IntelligenceX.Chat.Store",
                            Target = "IntelligenceX.Chat.App",
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            UploadFiles = new[] { storeUpload }
                        }
                    }
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        DotNetPublish = new DotNetPublishSpec()
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    ToolOutputs = new[] { PowerForgeReleaseToolOutputKind.Installer }
                });

            Assert.True(result.Success);
            Assert.NotNull(result.DotNetTools);
            Assert.Empty(result.DotNetTools!.Artefacts);
            Assert.Single(result.DotNetTools.MsiBuilds);
            Assert.Empty(result.DotNetTools.StorePackages);
            Assert.Contains(installer, result.ReleaseAssets);
            Assert.DoesNotContain(toolZip, result.ReleaseAssets);
            Assert.DoesNotContain(bundleZip, result.ReleaseAssets);
            Assert.DoesNotContain(storeUpload, result.ReleaseAssets);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ToolReleaseRunProcess_CapturesStdOutAndStdErrWithoutBlocking()
    {
        var method = typeof(PowerForgeToolReleaseService).GetMethod("RunProcess", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var isWindows = OperatingSystem.IsWindows();
        var tempScript = Path.Combine(Path.GetTempPath(), $"powerforge-toolrelease-{Guid.NewGuid():N}.{(isWindows ? "cmd" : "sh")}");
        try
        {
            File.WriteAllText(
                tempScript,
                isWindows
                    ? "@echo stdout-line\r\n@echo stderr-line 1>&2\r\n"
                    : "#!/bin/sh\necho stdout-line\necho stderr-line >&2\n",
                new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/sh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (isWindows)
                psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(tempScript);

            var result = method!.Invoke(null, new object?[] { psi });
            Assert.NotNull(result);

            var exitCode = (int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!;
            var stdOut = (string)result.GetType().GetProperty("StdOut")!.GetValue(result)!;
            var stdErr = (string)result.GetType().GetProperty("StdErr")!.GetValue(result)!;

            Assert.Equal(0, exitCode);
            Assert.Contains("stdout-line", stdOut, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("stderr-line", stdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempScript))
                File.Delete(tempScript);
        }
    }

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateXcodeProject(
        string root,
        string name,
        string marketingVersion = "1.0.0",
        string buildNumber = "1")
    {
        var xcodeproj = Path.Combine(root, name);
        Directory.CreateDirectory(xcodeproj);
        File.WriteAllText(
            Path.Combine(xcodeproj, "project.pbxproj"),
            $"""
                MARKETING_VERSION = {marketingVersion};
                CURRENT_PROJECT_VERSION = {buildNumber};
                """,
            new UTF8Encoding(false));

        return xcodeproj;
    }

    private static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}

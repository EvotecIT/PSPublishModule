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
                    SignOnFailure = DotNetPublishPolicyMode.Skip
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

            var installer = Assert.Single(result.DotNetToolPlan.Installers);
            Assert.NotNull(installer.Sign);
            Assert.True(installer.Sign!.Enabled);
            Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", installer.Sign.Thumbprint);
            Assert.Equal("PowerForge", installer.Sign.Description);
            Assert.Equal(DotNetPublishPolicyMode.Fail, installer.Sign.OnMissingTool);
            Assert.Equal(DotNetPublishPolicyMode.Skip, installer.Sign.OnSignFailure);
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
                planDotNetTools: (_, _, _) => new DotNetPublishPlan
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
                planDotNetTools: (_, _, _) => new DotNetPublishPlan
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
                planDotNetTools: (_, _, _) => new DotNetPublishPlan
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
                planDotNetTools: (_, _, _) => new DotNetPublishPlan
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

            var stagedPackage = Assert.Single(
                result.ReleaseAssetEntries,
                entry => string.Equals(entry.Path, package, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("nuget/" + Path.GetFileName(package), stagedPackage.RelativeStagePath!.Replace('\\', '/'));
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
                planDotNetTools: (_, _, _) => new DotNetPublishPlan
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

        var tempScript = Path.Combine(Path.GetTempPath(), $"powerforge-toolrelease-{Guid.NewGuid():N}.cmd");
        try
        {
            File.WriteAllText(tempScript, "@echo stdout-line\r\n@echo stderr-line 1>&2\r\n", new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
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

    private static void TryDelete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }
}

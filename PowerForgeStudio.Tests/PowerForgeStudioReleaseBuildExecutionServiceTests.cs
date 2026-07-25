using PowerForge;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UsesSharedProjectBuildHostServiceForProjectBuilds()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LibraryRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("LibraryRepo", "Build"));
        var buildScriptPath = Path.Combine(buildDirectory, "Build-Project.ps1");
        var configPath = Path.Combine(buildDirectory, "project.build.json");
        var outputDirectory = Path.Combine(repositoryRoot, "artifacts", "packages");

        File.WriteAllText(buildScriptPath, "# test");
        File.WriteAllText(
            configPath,
            """
            {
              "RootPath": "..",
              "OutputPath": "artifacts/packages",
              "Build": true
            }
            """);

        var callIndex = 0;
        var projectBuildHostService = new ProjectBuildHostService(
            new NullLogger(),
            executeRelease: spec =>
            {
                callIndex++;
                if (callIndex == 1)
                {
                    Assert.True(spec.WhatIf);
                    return new DotNetRepositoryReleaseResult { Success = true };
                }

                Assert.False(spec.WhatIf);
                Directory.CreateDirectory(outputDirectory);
                var packagePath = Path.Combine(outputDirectory, "LibraryRepo.1.0.0.nupkg");
                File.WriteAllText(packagePath, "pkg");
                return new DotNetRepositoryReleaseResult {
                    Success = true,
                    Projects = {
                        new DotNetRepositoryProjectResult {
                            ProjectName = "LibraryRepo",
                            IsPackable = true,
                            NewVersion = "1.0.0",
                            Packages = { packagePath }
                        }
                    }
                };
            },
            publishGitHub: null,
            validateGitHubPreflight: null);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            projectBuildHostService,
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()));

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(2, callIndex);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ProjectBuild, adapter.AdapterKind);
        Assert.Contains(outputDirectory, adapter.ArtifactDirectories);
        Assert.Contains(adapter.ArtifactFiles, path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteAsync_UsesDiscoveredJsonModuleConfigWithoutLegacyScript()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("JsonModuleRepo");
        scope.CreateDirectory(Path.Combine("JsonModuleRepo", "Build"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var moduleRoot = scope.CreateDirectory(Path.Combine("JsonModuleRepo", "src", "JsonModuleRepo"));
        var configuredArtifactRoot = Path.Combine(moduleRoot, "out");
        var configuredArtifact = Path.Combine(configuredArtifactRoot, "package-v1.0.0");
        var staleArtifact = Directory.CreateDirectory(Path.Combine(configuredArtifactRoot, "package-v0.9.0")).FullName;
        File.WriteAllText(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), "stale");
        File.SetLastWriteTimeUtc(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(
            moduleConfig,
            """
            {
              "SchemaVersion": 1,
              "Build": { "Name": "JsonModuleRepo", "SourcePath": "src/JsonModuleRepo" },
              "Segments": [
                { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "out/package-<TagModuleVersionWithPreRelease>" } }
              ]
            }
            """);
        PowerShellRunRequest? captured = null;
        var moduleRunner = new CapturingPowerShellRunner(request =>
        {
            captured = request;
            Directory.CreateDirectory(configuredArtifact);
            File.WriteAllText(Path.Combine(configuredArtifact, "JsonModuleRepo.zip"), "artifact");
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(
                new NullLogger(),
                executeRelease: _ => new DotNetRepositoryReleaseResult { Success = true },
                publishGitHub: null,
                validateGitHubPreflight: null),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(moduleRunner));

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.ModuleBuildConfigSha256));
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ModuleBuild, adapter.AdapterKind);
        Assert.NotNull(captured);
        Assert.Contains($"ConfigPath = '{moduleConfig}'", captured!.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptPath =", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['BuildFramework'] = 'auto'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['RunMode'] = 'Build'", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$moduleBuildArguments['IncludeProjectPackages'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['SkipInstall'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['NoSign'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(captured.PreferPwsh);
        Assert.Equal(8, captured.RequiredRuntimeMajor);
        Assert.Contains(configuredArtifact, adapter.ArtifactDirectories);
        Assert.Contains(Path.Combine(configuredArtifact, "JsonModuleRepo.zip"), adapter.ArtifactFiles);
        Assert.DoesNotContain(Path.Combine(staleArtifact, "JsonModuleRepo.zip"), adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedRelease_UsesSharedEngineAndCapturesToolArtifacts()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnifiedToolRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("UnifiedToolRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Tools": {
                "ProjectRoot": "..",
                "Targets": []
              },
              "GitHub": {
                "Publish": true,
                "Owner": "EvotecIT",
                "Repository": "UnifiedToolRepo"
              }
            }
            """);
        var toolDirectory = scope.CreateDirectory(Path.Combine("UnifiedToolRepo", "Artifacts", "Tool"));
        var zipPath = Path.Combine(toolDirectory, "UnifiedToolRepo-win-x64.zip");
        File.WriteAllText(zipPath, "zip");
        string? capturedConfig = null;
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, request) =>
            {
                capturedConfig = configPath;
                Assert.True(request.SkipAppleApps);
                Assert.False(request.PublishNuget);
                Assert.False(request.PublishProjectGitHub);
                Assert.False(request.PublishToolGitHub);
                Assert.False(string.IsNullOrWhiteSpace(request.ModuleHostPath));
                Assert.True(request.ModuleSkipInstall);
                Assert.False(request.SubmitWinget);
                return new PowerForgeReleaseResult {
                    Success = true,
                    ToolPlan = new PowerForgeToolReleasePlan(),
                    Tools = new PowerForgeToolReleaseResult {
                        Success = true,
                        Artefacts = [
                            new PowerForgeToolReleaseArtifactResult {
                                Target = "UnifiedToolRepo",
                                OutputPath = toolDirectory,
                                ZipPath = zipPath
                            }
                        ]
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(releaseConfig, capturedConfig);
        Assert.NotNull(result.UnifiedReleaseStateJson);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ToolBuild, adapter.AdapterKind);
        Assert.Contains(toolDirectory, adapter.ArtifactDirectories);
        Assert.Contains(zipPath, adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedModuleCheckpointCapturesEverySignablePackageArtifact()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LargeModuleRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("LargeModuleRepo", "Build"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        File.WriteAllText(
            moduleConfig,
            """{ "Build": { "Name": "LargeModuleRepo", "SourcePath": "." } }""");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """{ "Module": { "RepositoryRoot": "..", "ConfigPath": "powerforge.json" } }""");
        var artifactDirectory = scope.CreateDirectory(Path.Combine("LargeModuleRepo", "Artifacts", "Packages"));
        var packages = Enumerable.Range(1, 55)
            .Select(index => Path.Combine(artifactDirectory, $"LargeModuleRepo.Dependency{index}.1.0.0.nupkg"))
            .ToArray();
        foreach (var package in packages)
            File.WriteAllText(package, "package");

        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => new PowerForgeReleaseResult
            {
                Success = true,
                ModulePlan = new PowerForgeModuleReleasePlanSummary
                {
                    ModuleName = "LargeModuleRepo",
                    ConfigPath = moduleConfig,
                    ArtifactPaths = [artifactDirectory]
                },
                Module = new ModuleBuildHostExecutionResult { ExitCode = 0 },
                ModuleAssets = [artifactDirectory]
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(55, adapter.ArtifactFiles.Count(path =>
            path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)));
        Assert.All(packages, package => Assert.Contains(package, adapter.ArtifactFiles));
    }

    [Fact]
    public async Task ExecuteAsync_ModuleReleaseContract_UsesUnifiedEngineAndPreservesOverrides()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnifiedModuleRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("UnifiedModuleRepo", "Build"));
        scope.CreateDirectory(Path.Combine("UnifiedModuleRepo", "src", "UnifiedModuleRepo"));
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        File.WriteAllText(
            moduleConfig,
            """{ "Build": { "Name": "UnifiedModuleRepo", "SourcePath": "src/UnifiedModuleRepo" } }""");
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Module": {
                "RepositoryRoot": "..",
                "ConfigPath": "powerforge.json",
                "IncludesPackages": true,
                "Framework": "net10.0",
                "NoDotnetBuild": false,
                "ModuleVersion": "4.2.0",
                "PreReleaseTag": "preview7"
              }
            }
            """);
        var unifiedCalls = 0;
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, request) =>
            {
                unifiedCalls++;
                Assert.Equal(releaseConfig, configPath);
                Assert.Equal(ConfigurationGateMode.Build, request.ModuleRunMode);
                Assert.True(request.ModuleSkipInstall);
                Assert.True(request.ModuleNoSign);
                Assert.False(request.PublishNuget);
                Assert.False(string.IsNullOrWhiteSpace(request.ModuleHostPath));

                var spec = PowerForgeReleaseService.LoadConfiguration(configPath);
                Assert.True(spec.Module!.IncludesPackages);
                Assert.Equal("net10.0", spec.Module.Framework);
                Assert.False(spec.Module.NoDotnetBuild);
                Assert.Equal("4.2.0", spec.Module.ModuleVersion);
                Assert.Equal("preview7", spec.Module.PreReleaseTag);
                return new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = configPath,
                    ModulePlan = new PowerForgeModuleReleasePlanSummary {
                        ConfigPath = moduleConfig,
                        IncludesPackages = true
                    },
                    Module = new ModuleBuildHostExecutionResult {
                        ExitCode = 0,
                        Executable = "pwsh"
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(1, unifiedCalls);
        Assert.False(string.IsNullOrWhiteSpace(result.UnifiedReleaseConfigSha256));
        Assert.Equal(ReleaseBuildAdapterKind.ModuleBuild, Assert.Single(result.AdapterResults).AdapterKind);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedModuleUsesCheckpointedResolvedArtifacts()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ResolvedModuleRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("ResolvedModuleRepo", "Build"));
        var moduleRoot = scope.CreateDirectory(Path.Combine("ResolvedModuleRepo", "src", "ResolvedModuleRepo"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        var moduleConfig = Path.Combine(repositoryRoot, "powerforge.json");
        var resolvedArtifact = scope.CreateDirectory(Path.Combine("ResolvedModuleRepo", "src", "ResolvedModuleRepo", "out", "package-v2.0.0"));
        var staleArtifact = scope.CreateDirectory(Path.Combine("ResolvedModuleRepo", "src", "ResolvedModuleRepo", "out", "package-v1.0.0"));
        File.WriteAllText(releaseConfig, """{ "Module": { "ConfigPath": "../powerforge.json" } }""");
        File.WriteAllText(
            moduleConfig,
            """
            {
              "Build": { "Name": "ResolvedModuleRepo", "SourcePath": "src/ResolvedModuleRepo" },
              "Segments": [
                { "Type": "Packed", "Configuration": { "Enabled": true, "Path": "out/package-v<TagModuleVersionWithPreRelease>" } }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(resolvedArtifact, "ResolvedModuleRepo.zip"), "current");
        File.WriteAllText(Path.Combine(staleArtifact, "ResolvedModuleRepo.zip"), "stale");
        Directory.SetLastWriteTimeUtc(staleArtifact, DateTime.UtcNow.AddMinutes(5));
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => new PowerForgeReleaseResult {
                Success = true,
                ModulePlan = new PowerForgeModuleReleasePlanSummary {
                    ConfigPath = moduleConfig
                },
                Module = new ModuleBuildHostExecutionResult {
                    ExitCode = 0,
                    Executable = "pwsh"
                },
                ModuleAssets = [resolvedArtifact]
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.Equal(ReleaseBuildAdapterKind.ModuleBuild, adapter.AdapterKind);
        Assert.Contains(resolvedArtifact, adapter.ArtifactDirectories);
        Assert.DoesNotContain(staleArtifact, adapter.ArtifactDirectories);
        Assert.Contains(Path.Combine(resolvedArtifact, "ResolvedModuleRepo.zip"), adapter.ArtifactFiles);
        Assert.DoesNotContain(Path.Combine(staleArtifact, "ResolvedModuleRepo.zip"), adapter.ArtifactFiles);
    }

    [Fact]
    public async Task ExecuteAsync_AppleOnlyRelease_CheckpointsPlanWithoutExecutingActions()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AppleOnlyRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("AppleOnlyRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "AppleApps": {
                "Archive": true,
                "Apps": [
                  { "Name": "Sample iOS", "Enabled": true, "BundleId": "com.evotecit.sample" }
                ]
              }
            }
            """);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, request) =>
            {
                Assert.Equal(releaseConfig, configPath);
                Assert.True(request.PlanOnly);
                Assert.False(request.SkipAppleApps);
                return new PowerForgeReleaseResult {
                    Success = true,
                    ConfigPath = configPath,
                    AppleAppPlan = new PowerForgeAppleReleasePlan {
                        Archive = true
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.UnifiedReleaseStateJson));
        Assert.Equal(ReleaseBuildAdapterKind.AppleBuild, Assert.Single(result.AdapterResults).AdapterKind);
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceValidationContract_UsesUnifiedReleaseEngine()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ValidatedRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("ValidatedRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "Packages": {
                "RootPath": "..",
                "Build": true
              },
              "WorkspaceValidation": {
                "ConfigPath": "workspace.validation.json"
              }
            }
            """);
        var packagePath = Path.Combine(repositoryRoot, "Artifacts", "ValidatedRepo.1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        File.WriteAllText(packagePath, "package");
        var unifiedCalls = 0;
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (configPath, _) =>
            {
                unifiedCalls++;
                Assert.Equal(releaseConfig, configPath);
                return new PowerForgeReleaseResult {
                    Success = true,
                    WorkspaceValidation = new WorkspaceValidationResult {
                        Succeeded = true
                    },
                    Packages = new ProjectBuildHostExecutionResult {
                        Success = true,
                        Result = new ProjectBuildResult {
                            Success = true,
                            Release = new DotNetRepositoryReleaseResult {
                                Success = true,
                                Projects = {
                                    new DotNetRepositoryProjectResult {
                                        ProjectName = "ValidatedRepo",
                                        Packages = { packagePath }
                                    }
                                }
                            }
                        }
                    }
                };
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(1, unifiedCalls);
        Assert.Equal(ReleaseBuildAdapterKind.ProjectBuild, Assert.Single(result.AdapterResults).AdapterKind);
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceOnlyResultCreatesSuccessfulCheckpointAdapter()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("WorkspaceOnlyRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("WorkspaceOnlyRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(
            releaseConfig,
            """
            {
              "WorkspaceValidation": {
                "ConfigPath": "workspace.validation.json"
              }
            }
            """);
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => new PowerForgeReleaseResult {
                Success = true,
                WorkspaceValidation = new WorkspaceValidationResult {
                    Succeeded = true
                }
            });

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        var adapter = Assert.Single(result.AdapterResults);
        Assert.True(adapter.Succeeded);
        Assert.Equal(ReleaseBuildAdapterKind.ProjectBuild, adapter.AdapterKind);
        Assert.Equal("Unified workspace validation completed.", adapter.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_UnifiedCheckpointRedactsToolPlanSecretsWithoutMutatingExecutionResult()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SecretToolRepo");
        var buildDirectory = scope.CreateDirectory(Path.Combine("SecretToolRepo", "Build"));
        var releaseConfig = Path.Combine(buildDirectory, "release.json");
        File.WriteAllText(releaseConfig, """{ "Tools": { "Targets": [] } }""");
        var unified = new PowerForgeReleaseResult {
            Success = true,
            DotNetToolPlan = new DotNetPublishPlan {
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
                    ["PACKAGE_TOKEN"] = "plain-token"
                },
                Steps = [
                    new DotNetPublishStep {
                        HookEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                            ["HOOK_TOKEN"] = "plain-hook-token"
                        }
                    }
                ]
            },
            DotNetTools = new DotNetPublishResult {
                Succeeded = true
            }
        };
        var service = new ReleaseBuildExecutionService(
            new RepositoryCatalogScanner(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(new ThrowingPowerShellRunner()),
            new ModuleBuildHostService(new ThrowingPowerShellRunner()),
            (_, _) => unified);

        var result = await service.ExecuteAsync(repositoryRoot);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.UnifiedReleaseStateJson);
        Assert.DoesNotContain("plain-token", result.UnifiedReleaseStateJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-hook-token", result.UnifiedReleaseStateJson!, StringComparison.Ordinal);
        var checkpoint = System.Text.Json.JsonSerializer.Deserialize<PowerForgeReleaseResult>(result.UnifiedReleaseStateJson!);
        Assert.NotNull(checkpoint?.DotNetToolPlan);
        Assert.Equal("<redacted>", checkpoint!.DotNetToolPlan!.EnvironmentVariables["PACKAGE_TOKEN"]);
        Assert.Equal("<redacted>", checkpoint.DotNetToolPlan.Steps[0].HookEnvironment["HOOK_TOKEN"]);
        Assert.Equal("plain-token", unified.DotNetToolPlan.EnvironmentVariables["PACKAGE_TOKEN"]);
        Assert.Equal("plain-hook-token", unified.DotNetToolPlan.Steps[0].HookEnvironment["HOOK_TOKEN"]);
    }

    private sealed class TemporaryDirectoryScope : IDisposable
    {
        public TemporaryDirectoryScope()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PowerForgeStudioTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
        }
    }

    private sealed class ThrowingPowerShellRunner : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request)
            => throw new InvalidOperationException("PowerShell should not be used for project builds when shared host service is available.");
    }

    private sealed class CapturingPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => execute(request);
    }
}

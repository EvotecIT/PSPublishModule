namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_PublishesModuleRegistriesBeforePerToolGitHubRelease()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var moduleConfigPath = Path.Combine(root, "module.json");
            var projectConfigPath = Path.Combine(root, "project.build.json");
            var projectPath = Path.Combine(root, "Sample.Package.csproj");
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root, "Module")).FullName;
            var projectBuildRoot = Path.Combine(root, "project-build");
            var packagePath = Path.Combine(projectBuildRoot, "packages", "Sample.Package.1.2.3.nupkg");
            var moduleZip = Path.Combine(root, "SampleModule.v1.2.3.zip");
            var toolZip = Path.Combine(root, "SampleTool-1.2.3.zip");
            var toolExecutable = Path.Combine(root, "SampleTool.exe");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var feedPath = Path.Combine(root, "feed");
            Directory.CreateDirectory(feedPath);
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(Path.Combine(moduleRoot, "SampleModule.psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(moduleRoot, "SampleModule.psd1"),
                "@{ RootModule = 'SampleModule.psm1'; ModuleVersion = '1.2.3' }");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Version>1.2.3</Version>
                    <PackageId>Sample.Package</PackageId>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                projectConfigPath,
                $$"""
                {
                  "RootPath": ".",
                  "ExpectedVersionMap": { "Sample.Package": "1.2.3" },
                  "ExpectedVersionMapAsInclude": true,
                  "ExpectedVersionMapUseWildcards": false,
                  "StagingPath": "{{projectBuildRoot.Replace("\\", "\\\\")}}",
                  "UpdateVersions": false,
                  "Build": false,
                  "PublishNuget": true,
                  "PublishSource": "{{feedPath.Replace("\\", "\\\\")}}",
                  "PublishApiKey": "test-key",
                  "SkipDuplicate": false
                }
                """);
            File.WriteAllText(
                moduleConfigPath,
                """
                {
                  "Build": {
                    "Name": "SampleModule",
                    "SourcePath": "Module",
                    "Version": "1.2.3"
                  },
                  "Install": { "Enabled": false },
                  "Segments": [
                    {
                      "Type": "ProjectBuild",
                      "Configuration": {
                        "Name": "Sample.Package",
                        "ConfigPath": "project.build.json",
                        "Enabled": true,
                        "BuildBeforeModule": true,
                        "PublishNuget": true
                      }
                    }
                  ]
                }
                """);
            var events = new List<string>();
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "SampleTool",
                            Version = "1.2.3",
                            ExecutablePath = toolExecutable,
                            ZipPath = toolZip
                        }
                    ]
                },
                onModuleExecution: request =>
                {
                    if (request.RunMode == ConfigurationGateMode.Build)
                    {
                        events.Add("module-build");
                        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
                        using (var archive = System.IO.Compression.ZipFile.Open(
                                   packagePath,
                                   System.IO.Compression.ZipArchiveMode.Create))
                        {
                            var nuspec = archive.CreateEntry("Sample.Package.nuspec");
                            using var writer = new StreamWriter(nuspec.Open());
                            writer.Write("<package><metadata><id>Sample.Package</id><version>1.2.3</version></metadata></package>");
                        }
                        File.WriteAllText(moduleZip, "module archive");
                        File.WriteAllText(toolZip, "tool archive");
                        File.WriteAllText(toolExecutable, "tool executable");
                    }
                    else
                    {
                        events.Add("module-publish");
                    }
                },
                publishGitHubRelease: _ =>
                {
                    events.Add("tool-github");
                    Assert.True(Directory.Exists(feedPath));
                    Assert.Contains(
                        Directory.EnumerateFiles(feedPath, "*.nupkg", SearchOption.AllDirectories),
                        path => Path.GetFileName(path).Equals("Sample.Package.1.2.3.nupkg", StringComparison.OrdinalIgnoreCase));
                    Assert.Equal(ConfigurationGateMode.Publish, moduleCalls[^1].RunMode);
                    return new GitHubReleasePublishResult { Succeeded = true };
                },
                onToolExecution: () => events.Add("tool-build"),
                runReleaseValidation: (_, _, _, _) =>
                {
                    events.Add("validation");
                    return new PowerForgeReleaseValidationResult
                    {
                        Name = "release",
                        Succeeded = true,
                        ExitCode = 0
                    };
                });
            var spec = CreateReleaseSpec(root, Path.Combine(root, "unused-Build-Module.ps1"));
            spec.Module = new PowerForgeModuleReleaseOptions
            {
                RepositoryRoot = root,
                ConfigPath = moduleConfigPath,
                ModulePath = typeof(PowerForgeReleaseService).Assembly.Location,
                ModuleName = "SampleModule",
                ModuleVersion = "1.2.3",
                IncludesPackages = true,
                ArtifactPaths = [moduleZip, packagePath]
            };
            spec.Tools!.GitHub = new PowerForgeToolReleaseGitHubOptions
            {
                Publish = true,
                Owner = "EvotecIT",
                Repository = "Sample",
                TokenEnvName = "PATH",
                TagTemplate = "{Target}-v{Version}",
                ReleaseNameTemplate = "{Target} {Version}"
            };
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging =
                [
                    new PowerForgeReleaseValidationAction
                    {
                        Name = "release",
                        FilePath = validationPath
                    }
                ]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    PublishNuget = true,
                    StageRoot = Path.Combine(root, "staged")
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(
                new[] { "module-build", "tool-build", "validation", "module-publish", "tool-github" },
                events);
            Assert.Single(result.ModulePackagePublications);
            Assert.NotNull(result.ModulePublication);
            Assert.Single(result.ToolGitHubReleases);
        }
        finally
        {
            TryDelete(root);
        }
    }
}

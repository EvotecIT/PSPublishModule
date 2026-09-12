namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_can_defer_after_staging_validation_until_the_signed_checkpoint()
    {
        var root = CreateSandbox();
        try
        {
            var releasePath = Path.Combine(root, "release.json");
            var validationPath = Path.Combine(root, "validation.json");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(
                validationPath,
                """
                {
                  "Commands": [
                    { "Name": "Signed checkpoint", "FileName": "powerforge-missing-validation-probe" }
                  ]
                }
                """);
            var spec = new PowerForgeReleaseSpec
            {
                Tools = new PowerForgeToolReleaseSpec
                {
                    Targets = [new PowerForgeToolReleaseTarget { Name = "Fixture" }]
                },
                Outputs = new PowerForgeReleaseOutputsOptions
                {
                    Staging = new PowerForgeReleaseStagingOptions
                    {
                        RootPath = Path.Combine(root, "staging")
                    }
                },
                Validation = new PowerForgeReleaseValidationOptions
                {
                    AfterStaging =
                    [
                        new PowerForgeReleaseValidationAction
                        {
                            Name = "Signed checkpoint",
                            ConfigPath = validationPath
                        }
                    ]
                }
            };
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = releasePath,
                ResolvedReleaseVersion = "1.2.3",
                DeferAfterStagingValidation = true
            };
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan
                {
                    Targets = [new PowerForgeToolReleaseTargetPlan { Name = "Fixture" }]
                },
                runTools: _ => new PowerForgeToolReleaseResult { Success = true },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub should not run."));

            var result = service.Execute(spec, request);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Empty(result.ReleaseValidations);
            Assert.False(service.ValidateBuiltReleaseOutputs(spec, request, result));
            Assert.Contains("Signed checkpoint", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ExplicitDisabledSigningOverridesConfiguredToolAndInstallerProfiles()
    {
        var root = CreateSandbox();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Tool.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Version>1.0.0</Version>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Installer.wixproj"), "<Project />");
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
                            Targets =
                            [
                                new DotNetPublishTarget
                                {
                                    Name = "Tool",
                                    ProjectPath = "Tool.csproj",
                                    Publish = new DotNetPublishPublishOptions
                                    {
                                        Framework = "net10.0",
                                        Runtimes = ["win-x64"],
                                        Sign = new DotNetPublishSignOptions
                                        {
                                            Enabled = true,
                                            Thumbprint = "configured"
                                        }
                                    }
                                }
                            ],
                            Installers =
                            [
                                new DotNetPublishInstaller
                                {
                                    Id = "Tool.Msi",
                                    PrepareFromTarget = "Tool",
                                    InstallerProjectPath = "Installer.wixproj",
                                    Sign = new DotNetPublishSignOptions
                                    {
                                        Enabled = true,
                                        Thumbprint = "configured"
                                    }
                                }
                            ]
                        }
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    PlanOnly = true,
                    ToolsOnly = true,
                    EnableSigning = false
                });

            Assert.True(result.Success);
            Assert.False(Assert.Single(result.DotNetToolPlan!.Targets).Publish.Sign!.Enabled);
            Assert.False(Assert.Single(result.DotNetToolPlan.Installers).Sign!.Enabled);
        }
        finally
        {
            TryDelete(root);
        }
    }
}

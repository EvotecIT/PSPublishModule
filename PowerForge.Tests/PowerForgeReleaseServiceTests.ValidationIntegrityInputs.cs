namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData("ScriptPath")]
    [InlineData("ConfigPath")]
    [InlineData("ModulePath")]
    [InlineData("BeforePublishAction")]
    [InlineData("PublishApiKeyFile")]
    public void Execute_validation_cannot_change_a_deferred_module_publication_input(string mutationTarget)
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var configPath = Path.Combine(root, "powerforge.json");
            var modulePath = Path.Combine(root, "PSPublishModule.psd1");
            var actionPath = Path.Combine(root, "BeforePublish.ps1");
            var apiKeyPath = Path.Combine(root, "gallery.key");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(modulePath, "@{ ModuleVersion = '1.2.3' }");
            File.WriteAllText(actionPath, "# before publish");
            File.WriteAllText(apiKeyPath, "secret");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(releasePath, "{}");
            File.WriteAllText(configPath, """
                {
                  "Build": { "Name": "SampleModule", "SourcePath": ".", "Version": "1.2.3" },
                  "Segments": [
                    {
                      "Type": "Execute",
                      "Configuration": {
                        "Enabled": true,
                        "At": "BeforePublish",
                        "FilePath": "BeforePublish.ps1"
                      }
                    },
                    {
                      "Type": "GalleryNuget",
                      "Configuration": {
                        "Destination": "PowerShellGallery",
                        "Enabled": true,
                        "ApiKeyFilePath": "gallery.key"
                      }
                    }
                  ]
                }
                """);

            var targetPath = mutationTarget switch
            {
                "ScriptPath" => scriptPath,
                "ConfigPath" => configPath,
                "ModulePath" => modulePath,
                "BeforePublishAction" => actionPath,
                "PublishApiKeyFile" => apiKeyPath,
                _ => throw new InvalidOperationException($"Unknown mutation target: {mutationTarget}")
            };
            var useScript = mutationTarget == "ScriptPath";
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                runReleaseValidation: (_, _, _, _) =>
                {
                    File.AppendAllText(targetPath, Environment.NewLine + "mutated after validation");
                    return new PowerForgeReleaseValidationResult
                    {
                        Name = "release",
                        Succeeded = true,
                        ExitCode = 0
                    };
                });
            var spec = CreateReleaseSpec(root, scriptPath);
            if (!useScript)
            {
                spec.Module!.ConfigPath = configPath;
                spec.Module.ScriptPath = null;
            }
            spec.Module!.ModulePath = modulePath;
            spec.Module.ModuleVersion = "1.2.3";
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging = [new() { Name = "release", FilePath = validationPath }]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    ModuleVersion = "1.2.3",
                    StageRoot = Path.Combine(root, "staged")
                });

            Assert.False(result.Success);
            Assert.Contains("changed a release input", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(moduleCalls);
            Assert.False(Assert.Single(result.ReleaseValidations).Succeeded);
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Deferred_publication_boundary_rejects_a_delayed_validation_mutation()
    {
        var root = CreateSandbox();
        try
        {
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var modulePath = Path.Combine(root, "PSPublishModule.psd1");
            var validationPath = Path.Combine(root, "Test-Release.ps1");
            var releasePath = Path.Combine(root, "release.json");
            File.WriteAllText(scriptPath, "# module build");
            File.WriteAllText(modulePath, "@{ ModuleVersion = '1.2.3' }");
            File.WriteAllText(validationPath, "# validation");
            File.WriteAllText(releasePath, "{}");
            var moduleCalls = new List<ModuleExecutionSnapshot>();
            var service = CreateReleaseService(
                root,
                moduleCalls,
                new PowerForgeToolReleaseResult { Success = true },
                runReleaseValidation: (_, _, _, _) => new PowerForgeReleaseValidationResult
                {
                    Name = "release",
                    Succeeded = true,
                    ExitCode = 0
                });
            var spec = CreateReleaseSpec(root, scriptPath);
            spec.Module!.ModulePath = modulePath;
            spec.Module.ModuleVersion = "1.2.3";
            spec.Validation = new PowerForgeReleaseValidationOptions
            {
                AfterStaging = [new() { Name = "release", FilePath = validationPath }]
            };

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = releasePath,
                    ModuleRunMode = ConfigurationGateMode.Publish,
                    ModuleVersion = "1.2.3",
                    StageRoot = Path.Combine(root, "staged"),
                    Progress = new MutationAfterValidationProgress(modulePath)
                });

            Assert.False(result.Success);
            Assert.Contains("changed a release input", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Single(moduleCalls);
            Assert.True(Assert.Single(result.ReleaseValidations).Succeeded);
            Assert.Null(result.ModulePublication);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private sealed class MutationAfterValidationProgress(string path) : IPowerForgeReleaseProgressReporter
    {
        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null)
        {
        }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
            if (phase == PowerForgeReleaseProgressPhase.Validation)
                File.AppendAllText(path, Environment.NewLine + "delayed mutation");
        }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null)
        {
        }
    }
}

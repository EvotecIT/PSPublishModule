using PowerForge;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModuleBuildHostServiceTests
{
    [Fact]
    public async Task ExecuteBuildAsync_InvokesJsonConfigDirectly()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = @"C:\repo",
            ConfigPath = @"C:\repo\powerforge.json",
            ModulePath = @"C:\repo\PSPublishModule\bin\Release\net8.0\PSPublishModule.dll",
            Configuration = "Release",
            Framework = "auto",
            RunMode = ConfigurationGateMode.Build,
            ModuleVersion = "3.1.0",
            NoDotnetBuild = true,
            NoSign = true,
            IncludeProjectPackages = false
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(captured);
        Assert.Contains("Invoke-ModuleBuild", captured!.CommandText!, StringComparison.Ordinal);
        Assert.Contains("ConfigPath = 'C:\\repo\\powerforge.json'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['BuildConfiguration'] = 'Release'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['BuildFramework'] = 'auto'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['RunMode'] = 'Build'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['ModuleVersion'] = '3.1.0'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['NoDotnetBuild'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['NoSign'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$moduleBuildArguments['IncludeProjectPackages'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptPath", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$LASTEXITCODE", captured.CommandText!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ImportsConfiguredLocalAssemblyWithoutInstalledModuleFallback()
    {
        var modulePath = Path.GetTempFileName();
        try
        {
            PowerShellRunRequest? captured = null;
            var runner = new StubPowerShellRunner(request => {
                captured = request;
                return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
            });

            var result = await new ModuleBuildHostService(runner).ExecuteBuildAsync(new ModuleBuildHostBuildRequest
            {
                RepositoryRoot = Path.GetTempPath(),
                ConfigPath = Path.Combine(Path.GetTempPath(), "powerforge.json"),
                ModulePath = modulePath,
                Framework = "auto"
            });

            Assert.True(result.Succeeded);
            Assert.NotNull(captured);
            Assert.Contains($"Import-Module '{modulePath.Replace("'", "''")}' -Force -ErrorAction Stop", captured!.CommandText!, StringComparison.Ordinal);
            Assert.DoesNotContain("catch { Import-Module PSPublishModule", captured.CommandText!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(modulePath);
        }
    }

    [Fact]
    public async Task ExecuteBuildAsync_JsonFailurePreservesCmdletProcessExitCode()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-module-host-exit-" + Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "SampleModule";
            var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
            File.WriteAllText(Path.Combine(moduleRoot.FullName, moduleName + ".psm1"), string.Empty);
            File.WriteAllText(
                Path.Combine(moduleRoot.FullName, moduleName + ".psd1"),
                $"@{{ RootModule = '{moduleName}.psm1'; ModuleVersion = '1.0.0' }}");
            var configPath = Path.Combine(root.FullName, "powerforge.json");
            File.WriteAllText(
                configPath,
                $$"""
                {
                  "SchemaVersion": 1,
                  "Build": {
                    "Name": "{{moduleName}}",
                    "SourcePath": "Module",
                    "CsprojPath": "Missing.csproj",
                    "Version": "1.0.0",
                    "Frameworks": [ "net8.0" ],
                    "ExportAssemblies": [ "{{moduleName}}.dll" ]
                  },
                  "Install": { "Enabled": false },
                  "Segments": []
                }
                """);
            var repoRoot = RepoRootLocator.Find();
            var modulePath = Path.Combine(repoRoot, "PSPublishModule", "bin", "Release", "net8.0", "PSPublishModule.dll");
            Assert.True(File.Exists(modulePath), $"Built net8.0 module was not found: {modulePath}");

            var result = await new ModuleBuildHostService().ExecuteBuildAsync(new ModuleBuildHostBuildRequest
            {
                RepositoryRoot = root.FullName,
                ConfigPath = configPath,
                ModulePath = modulePath,
                Framework = "net8.0",
                NoDotnetBuild = true,
                Timeout = TimeSpan.FromMinutes(1)
            });

            Assert.False(result.Succeeded);
            Assert.Equal(1, result.ExitCode);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ExportPipelineJsonAsync_UsesSharedModuleWrapperAndWorkingDirectory()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            OutputPath = @"C:\repo\artifacts\powerforge.json"
        });

        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(!FrameworkCompatibility.IsWindows(), captured.PreferPwsh);
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Contains("JsonOnly = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("JsonPath = $targetJson", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath Alias:Build-Module -Force -ErrorAction SilentlyContinue", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath Alias:Invoke-ModuleBuilder -Force -ErrorAction SilentlyContinue", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("Set-Alias -Name Invoke-ModuleBuilder -Value Invoke-ModuleBuild -Scope Local", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('RunMode')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments = @{}", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['RunMode'] = 'Build'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['ConfigurationGateMode'] = 'Build'", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptArguments += @('-RunMode', 'Build')", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("'-RunMode', 'Publish'", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("'-ConfigurationGateMode', 'Publish'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("function Import-Module", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("& $cmd @args", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains(". $buildScriptPath @buildScriptArguments", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsSigningFlags()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            NoSign = true
        });

        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Contains(". $buildScriptPath @buildScriptArguments", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['NoSign'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("function New-ConfigurationBuild", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsOptionalFlagsOnlyWhenScriptSupportsThem()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            NoDotnetBuild = true,
            SignModule = true
        });

        Assert.NotNull(captured);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('NoDotnetBuild')", captured!.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['NoDotnetBuild'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('SignModule')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['SignModule'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptArguments += '-SignModule'", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsRunModeAndUnifiedReleaseStage()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            Framework = "net10.0",
            RunMode = ConfigurationGateMode.Publish,
            PowerForgeReleaseStage = true,
            UnifiedGitHubRelease = true
        });

        Assert.NotNull(captured);
        Assert.True(captured!.PreferPwsh);
        Assert.Equal(10, captured.RequiredRuntimeMajor);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('Framework')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['Framework'] = 'net10.0'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('RunMode')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['RunMode'] = 'Publish'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('PowerForgeReleaseStage')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['PowerForgeReleaseStage'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('PowerForgeUnifiedGitHubRelease')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['PowerForgeUnifiedGitHubRelease'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ReleaseStageWithoutUnifiedGitHubKeepsLegacyPublisherAvailable()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            RunMode = ConfigurationGateMode.Publish,
            PowerForgeReleaseStage = true,
            UnifiedGitHubRelease = false
        });

        Assert.NotNull(captured);
        Assert.Contains("$buildScriptArguments['PowerForgeReleaseStage'] = $true", captured!.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptArguments['PowerForgeUnifiedGitHubRelease'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("net472", false, 0)]
    [InlineData("netcoreapp3.1", true, 3)]
    [InlineData("net8.0", true, 8)]
    [InlineData("net10.0-windows", true, 10)]
    [InlineData("auto", true, 8)]
    public async Task ExecuteBuildAsync_SelectsHostCompatibleWithTargetFramework(string framework, bool modernDotNet, int requiredRuntimeMajor)
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            Framework = framework
        });

        Assert.NotNull(captured);
        Assert.Equal(!FrameworkCompatibility.IsWindows() || modernDotNet, captured!.PreferPwsh);
        Assert.Equal(requiredRuntimeMajor, captured.RequiredRuntimeMajor);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsReleaseScopeOverridesAndTimeout()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            IncludeProjectPackages = false,
            Timeout = TimeSpan.FromHours(3),
            CertificateThumbprint = "ABC123",
            SignIncludeBinaries = true,
            SignIncludeInternals = false,
            SignIncludeExe = true,
            DiagnosticsBaselinePath = @".powerforge\diagnostics.json",
            GenerateDiagnosticsBaseline = false,
            UpdateDiagnosticsBaseline = true,
            FailOnNewDiagnostics = true,
            FailOnDiagnosticsSeverity = "Error"
        });

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.FromHours(3), captured!.Timeout);
        Assert.Equal(!FrameworkCompatibility.IsWindows(), captured.PreferPwsh);
        Assert.Contains("$buildScriptArguments['IncludeProjectPackages'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['CertificateThumbprint'] = 'ABC123'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['SignIncludeBinaries'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['SignIncludeInternals'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['SignIncludeExe'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['DiagnosticsBaselinePath'] = '.powerforge\\diagnostics.json'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['GenerateDiagnosticsBaseline'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['UpdateDiagnosticsBaseline'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['FailOnNewDiagnostics'] = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['FailOnDiagnosticsSeverity'] = 'Error'", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_DoesNotForwardSigningFlags_WhenUnset()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1"
        });

        Assert.NotNull(captured);
        Assert.DoesNotContain("-NoSign", captured!.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("-SignModule", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsConfigurationOnlyWhenScriptSupportsParameter()
    {
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            Configuration = "Release"
        });

        Assert.NotNull(captured);
        Assert.Contains("$buildScriptPath = (Get-Item -LiteralPath", captured!.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand = Get-Command -Name $buildScriptPath -CommandType ExternalScript", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptCommand.Parameters.ContainsKey('Configuration')", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$buildScriptArguments['Configuration'] = 'Release'", captured.CommandText!, StringComparison.Ordinal);
        Assert.DoesNotContain("$buildScriptArguments += @('-Configuration', 'Release')", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_ForwardsStructuredModuleProgressFromChildHost()
    {
        var progress = new RecordingReleaseProgress();
        PowerShellRunRequest? captured = null;
        var runner = new StubPowerShellRunner(request =>
        {
            captured = request;
            var item = new PowerForgeReleaseProgressItem
            {
                Phase = PowerForgeReleaseProgressPhase.Module,
                Key = "build:stage",
                Title = "Stage to staging",
                Kind = ModulePipelineStepKind.Build.ToString(),
                Position = 1,
                Total = 1
            };
            var planned = JsonSerializer.Serialize(new ModulePipelineProgressProtocolMessage
            {
                Items = new[] { item }
            });
            var completed = JsonSerializer.Serialize(new ModulePipelineProgressProtocolMessage
            {
                Item = item,
                State = PowerForgeReleaseProgressItemState.Completed
            });
            request.OutputLineReceived!(
                "##powerforge-module-progress-v1##" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(planned)));
            request.OutputLineReceived!(
                "##powerforge-module-progress-v1##" +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(completed)));
            return new PowerShellRunResult(0, "ok", string.Empty, "pwsh");
        });
        var service = new ModuleBuildHostService(runner);

        var result = await service.ExecuteBuildAsync(new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = @"C:\repo",
            ScriptPath = @"C:\repo\Build\Build-Module.ps1",
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            Framework = "auto",
            Progress = progress
        });

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("##powerforge-module-progress", result.StandardOutput, StringComparison.Ordinal);
        Assert.NotNull(captured);
        Assert.Equal("1", captured!.EnvironmentVariables![ModulePipelineProgressProtocol.EnvironmentVariable]);
        Assert.NotNull(captured.OutputLineReceived);
        var plannedItem = Assert.Single(progress.Planned);
        Assert.Equal("build:stage", plannedItem.Key);
        var update = Assert.Single(progress.Updates);
        Assert.Equal(PowerForgeReleaseProgressItemState.Completed, update.State);
    }

    private sealed class RecordingReleaseProgress : IPowerForgeReleaseProgressReporterV2
    {
        public List<PowerForgeReleaseProgressItem> Planned { get; } = new();

        public List<(PowerForgeReleaseProgressItem Item, PowerForgeReleaseProgressItemState State)> Updates { get; } = new();

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null) { }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
            => Planned.AddRange(items);

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
            => Updates.Add((item, state));
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }
}

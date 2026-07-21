using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBuildHostServiceTests
{
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

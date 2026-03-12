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
        Assert.Equal(@"C:\repo", captured.WorkingDirectory);
        Assert.Contains("JsonOnly = $true", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("JsonPath = $targetJson", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains(@". 'C:\repo\Build\Build-Module.ps1'", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ExecuteBuildAsync_UsesSharedSigningOverrideWrapper()
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
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Contains("function New-ConfigurationBuild", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("$params['SignModule'] = $false", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("-SignModule:$false", captured.CommandText!, StringComparison.Ordinal);
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

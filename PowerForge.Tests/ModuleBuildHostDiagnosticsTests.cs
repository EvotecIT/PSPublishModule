using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModuleBuildHostDiagnosticsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ChildDiagnosticsReachTheOwnerWithoutLeakingOrDuplicatingProgress(int exitCode)
    {
        const string warning = "WARNING: Complete certificate-provider recovery instructions.";
        const string error = "A complete child-host diagnostic.";
        var protocol = "##powerforge-module-progress-v1##" + Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new ModulePipelineProgressProtocolMessage
            {
                Item = new PowerForgeReleaseProgressItem { Key = "sign", Title = "Sign module" },
                State = exitCode == 0 ? PowerForgeReleaseProgressItemState.Completed : PowerForgeReleaseProgressItemState.Failed,
                Detail = "Signing result"
            })));
        var logger = new BufferedLogger();
        var host = new ModuleBuildHostService(new Runner(request =>
        {
            request.OutputLineReceived!(protocol);
            request.OutputLineReceived!(warning);
            Assert.Null(request.ErrorLineReceived);
            return new PowerShellRunResult(exitCode, protocol + "\n" + warning, error, "pwsh");
        }), logger);

        var result = await host.ExecuteBuildAsync(new ModuleBuildHostBuildRequest
        {
            RepositoryRoot = Path.GetTempPath(), ScriptPath = "Build.ps1", ModulePath = "PSPublishModule", Progress = new Progress()
        });

        Assert.Equal(exitCode == 0, result.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("info", entry.Level);
        Assert.Equal(warning, entry.Message);
        Assert.Equal(warning, result.StandardOutput);
        Assert.Equal(error, result.StandardError);
        if (exitCode != 0) Assert.Equal("Signing result", result.FailureMessage);
    }

    private sealed class Runner(Func<PowerShellRunRequest, PowerShellRunResult> run) : IPowerShellRunner
    {
        public PowerShellRunResult Run(PowerShellRunRequest request) => run(request);
    }

    private sealed class Progress : IPowerForgeReleaseProgressReporterV2
    {
        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null) { }
        public void PhaseUpdated(PowerForgeReleaseProgressPhase phase, int completedItems, int totalItems, string? detail = null) { }
        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null) { }
        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null) { }
        public void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items) { }
        public void ItemUpdated(PowerForgeReleaseProgressItem item, PowerForgeReleaseProgressItemState state, string? detail = null) { }
    }
}

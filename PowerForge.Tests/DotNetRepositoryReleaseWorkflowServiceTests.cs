using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetRepositoryReleaseWorkflowServiceTests
{
    [Fact]
    public void Execute_returns_plan_when_execution_is_skipped()
    {
        var calls = 0;
        var plan = new DotNetRepositoryReleaseResult { Success = true };
        var service = new DotNetRepositoryReleaseWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                calls++;
                Assert.True(spec.WhatIf);
                return plan;
            });

        var result = service.Execute(
            new DotNetRepositoryReleasePreparedContext
            {
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: false);

        Assert.Equal(1, calls);
        Assert.Same(plan, result);
    }

    [Fact]
    public void Execute_runs_plan_then_release_when_execution_is_requested()
    {
        var calls = 0;
        var service = new DotNetRepositoryReleaseWorkflowService(
            new NullLogger(),
            executeRelease: spec =>
            {
                calls++;
                return new DotNetRepositoryReleaseResult
                {
                    Success = true,
                    ErrorMessage = spec.WhatIf ? "plan" : "release"
                };
            });

        var result = service.Execute(
            new DotNetRepositoryReleasePreparedContext
            {
                RootPath = Directory.GetCurrentDirectory(),
                Spec = new DotNetRepositoryReleaseSpec { RootPath = Directory.GetCurrentDirectory() }
            },
            executeBuild: true);

        Assert.Equal(2, calls);
        Assert.Equal("release", result.ErrorMessage);
    }
}

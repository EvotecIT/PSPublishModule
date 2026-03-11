using PowerForge;

namespace PowerForge.Tests;

public sealed class DotNetPublishWorkflowServiceTests
{
    [Fact]
    public void Execute_writes_json_and_returns_path_for_json_only()
    {
        DotNetPublishSpec? capturedSpec = null;
        string? capturedPath = null;
        var service = new DotNetPublishWorkflowService(
            new NullLogger(),
            writeSpecJson: (spec, path) =>
            {
                capturedSpec = spec;
                capturedPath = path;
            });

        var spec = new DotNetPublishSpec();
        var result = service.Execute(new DotNetPublishPreparedContext
        {
            Spec = spec,
            SourceLabel = "settings",
            JsonOnly = true,
            JsonOutputPath = "publish.json"
        });

        Assert.Same(spec, capturedSpec);
        Assert.Equal("publish.json", capturedPath);
        Assert.Equal("publish.json", result.JsonOutputPath);
        Assert.Null(result.Plan);
        Assert.Null(result.Result);
    }

    [Fact]
    public void Execute_returns_plan_and_logs_success_for_validate()
    {
        var logger = new CollectingLogger();
        var expectedPlan = new DotNetPublishPlan
        {
            Steps = new[] { new DotNetPublishStep() },
            Targets = new[] { new DotNetPublishTargetPlan { Name = "App" } }
        };
        var service = new DotNetPublishWorkflowService(
            logger,
            planPublish: (spec, sourceLabel) =>
            {
                Assert.Equal("config.json", sourceLabel);
                return expectedPlan;
            });

        var result = service.Execute(new DotNetPublishPreparedContext
        {
            Spec = new DotNetPublishSpec(),
            SourceLabel = "config.json",
            ValidateOnly = true
        });

        Assert.Same(expectedPlan, result.Plan);
        Assert.Contains(logger.SuccessMessages, message => message.Contains("valid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_runs_publish_when_plan_only_flags_are_not_set()
    {
        var expectedResult = new DotNetPublishResult { Succeeded = true };
        var service = new DotNetPublishWorkflowService(
            new NullLogger(),
            planPublish: (_, _) => new DotNetPublishPlan(),
            runPublish: (plan, progress) =>
            {
                Assert.NotNull(plan);
                Assert.Null(progress);
                return expectedResult;
            });

        var result = service.Execute(new DotNetPublishPreparedContext
        {
            Spec = new DotNetPublishSpec(),
            SourceLabel = "settings"
        });

        Assert.Same(expectedResult, result.Result);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> SuccessMessages { get; } = new();

        public void Info(string message) { }

        public void Success(string message) => SuccessMessages.Add(message);

        public void Warn(string message) { }

        public void Error(string message) { }

        public void Verbose(string message) { }

        public bool IsVerbose => false;
    }
}

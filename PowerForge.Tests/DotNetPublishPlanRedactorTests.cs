namespace PowerForge.Tests;

public sealed class DotNetPublishPlanRedactorTests
{
    [Fact]
    public void RedactInPlace_ReplacesResolvedEnvironmentVariableValues()
    {
        var plan = new DotNetPublishPlan
        {
            EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["LICENSING_PACKAGES_TOKEN"] = "token-value",
                ["LICENSING_PACKAGES_USERNAME"] = "EvotecIT",
                ["OPTIONAL_EMPTY"] = null
            }
        };

        var redacted = DotNetPublishPlanRedactor.RedactInPlace(plan);

        Assert.Same(plan, redacted);
        Assert.Equal("<redacted>", plan.EnvironmentVariables["LICENSING_PACKAGES_TOKEN"]);
        Assert.Equal("<redacted>", plan.EnvironmentVariables["LICENSING_PACKAGES_USERNAME"]);
        Assert.Null(plan.EnvironmentVariables["OPTIONAL_EMPTY"]);
    }

    [Fact]
    public void RedactInPlace_ReplacesHookEnvironmentValues()
    {
        var plan = new DotNetPublishPlan
        {
            Steps = new[]
            {
                new DotNetPublishStep
                {
                    HookEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["HOOK_TOKEN"] = "secret"
                    }
                }
            }
        };

        DotNetPublishPlanRedactor.RedactInPlace(plan);

        Assert.Equal("<redacted>", plan.Steps[0].HookEnvironment["HOOK_TOKEN"]);
    }
}

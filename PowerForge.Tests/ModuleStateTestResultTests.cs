using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStateTestResultTests
{
    [Fact]
    public void FromPlan_ReturnsCompliantWhenNoActionsOrErrorsExist()
    {
        var result = ModuleStateTestResult.FromPlan(new ModuleStatePlanResult
        {
            Actions = Array.Empty<ModuleStatePlanActionResult>(),
            Findings = Array.Empty<ModuleStateConflictFindingResult>()
        });

        Assert.True(result.IsCompliant);
        Assert.Equal(0, result.RequiredActionCount);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public void FromPlan_ReturnsNonCompliantWhenActionsOrErrorsExist()
    {
        var result = ModuleStateTestResult.FromPlan(new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult { Kind = "Update", ModuleName = "Company.Tools" }
            },
            Findings = new[]
            {
                new ModuleStateConflictFindingResult { Severity = "Error", Code = "ModuleState.FamilyVersionMismatch" }
            }
        });

        Assert.False(result.IsCompliant);
        Assert.Equal(1, result.RequiredActionCount);
        Assert.Equal(1, result.ErrorCount);
    }
}

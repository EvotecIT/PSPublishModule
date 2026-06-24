using PSPublishModule;

namespace PowerForge.Tests;

public sealed class ModuleStatePlanResultMapperTests
{
    [Fact]
    public void ToCorePlan_RestoresActionsAndFindingsFromArtifactResult()
    {
        var result = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "Update",
                    ModuleName = "Company.Tools",
                    InstalledVersion = "1.0.0",
                    VersionPolicy = "=1.2.0",
                    Reason = "approved plan",
                    IsRepair = true,
                    TargetScope = "CurrentUser",
                    TargetPath = "C:/Modules/Company.Tools/1.0.0",
                    TargetRepository = "CompanyModules"
                }
            },
            Findings = new[]
            {
                new ModuleStateConflictFindingResult
                {
                    Severity = "Error",
                    Code = "ModuleState.FamilyVersionMismatch",
                    Message = "Mismatch.",
                    FamilyName = "MicrosoftGraph",
                    ModuleNames = new[] { "Company.Tools" },
                    Versions = new[] { "1.0.0", "1.2.0" }
                }
            }
        };

        var plan = ModuleStatePlanResultMapper.ToCorePlan(result);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(ModuleStatePlanActionKind.Update, action.Kind);
        Assert.Equal("Company.Tools", action.ModuleName);
        Assert.Equal("=1.2.0", action.VersionPolicy);
        Assert.True(action.IsRepair);
        Assert.Equal("CurrentUser", action.TargetScope);
        Assert.Equal("C:/Modules/Company.Tools/1.0.0", action.TargetPath);
        Assert.Equal("CompanyModules", action.TargetRepository);

        var finding = Assert.Single(plan.Findings);
        Assert.Equal(ModuleStateConflictSeverity.Error, finding.Severity);
        Assert.Equal("ModuleState.FamilyVersionMismatch", finding.Code);
        Assert.Equal("MicrosoftGraph", finding.FamilyName);
    }

    [Fact]
    public void ToCorePlan_RejectsUnknownActionKind()
    {
        var result = new ModuleStatePlanResult
        {
            Actions = new[]
            {
                new ModuleStatePlanActionResult
                {
                    Kind = "RestartEverything",
                    ModuleName = "Company.Tools",
                    VersionPolicy = ">=1.0.0",
                    Reason = "invalid"
                }
            }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => ModuleStatePlanResultMapper.ToCorePlan(result));

        Assert.Contains("unsupported Kind", exception.Message);
    }
}

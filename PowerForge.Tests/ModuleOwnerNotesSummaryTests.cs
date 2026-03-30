using PowerForge.ConsoleShared;

namespace PowerForge.Tests;

public sealed class ModuleOwnerNotesSummaryTests
{
    [Fact]
    public void BuildMergeExecutionOwnerDetails_IncludesModuleListsAndInlineCounts()
    {
        var details = ModulePipelineRunner.BuildMergeExecutionOwnerDetails(
            new[]
            {
                new RequiredModuleReference("PSSharedGoods", moduleVersion: "0.0.313.1"),
                new RequiredModuleReference("PSWriteColor", moduleVersion: "1.0.3")
            },
            new[] { "PSSharedGoods", "PSWriteColor" },
            new[] { "Microsoft.Graph.Authentication" },
            topLevelInlinedFunctions: 10,
            totalInlinedFunctions: 12);

        Assert.Contains("Required modules (2):", details[0], StringComparison.Ordinal);
        Assert.Contains("PSSharedGoods", string.Join(Environment.NewLine, details), StringComparison.Ordinal);
        Assert.Contains("Approved modules (2): PSSharedGoods, PSWriteColor", details, StringComparer.Ordinal);
        Assert.Contains("Dependent modules (1): Microsoft.Graph.Authentication", details, StringComparer.Ordinal);
        Assert.Contains("Functions inlined during merge: 10 top-level function(s) inlined (total 12 including dependencies).", details, StringComparer.Ordinal);
    }

    [Fact]
    public void BuildMergeExecutionOwnerDetails_ReturnsEmptyArrayForNullInputsAndZeroCounts()
    {
        var details = ModulePipelineRunner.BuildMergeExecutionOwnerDetails(
            requiredModules: null,
            approvedModules: null,
            dependentModules: null,
            topLevelInlinedFunctions: 0,
            totalInlinedFunctions: 0);

        Assert.Empty(details);
    }

    [Fact]
    public void ShouldRenderOwnerNoteAsPanel_ReturnsTrueWhenNoteHasMeaningfulDetails()
    {
        var note = new ModuleOwnerNote(
            "Module Entry Script",
            ModuleOwnerNoteSeverity.Info,
            summary: "Build wrote a merged .psm1 entry script from 92 script source file(s).",
            details: new[]
            {
                "Required modules (2): PSSharedGoods, PSWriteColor",
                "Functions inlined during merge: 10 top-level function(s) inlined (total 12 including dependencies)."
            });

        Assert.True(SpectrePipelineSummaryWriter.ShouldRenderOwnerNoteAsPanel(note));
    }

    [Fact]
    public void ShouldRenderOwnerNoteAsPanel_ReturnsFalseForSimpleSummaryOnlyNote()
    {
        var note = new ModuleOwnerNote(
            "Manifest",
            ModuleOwnerNoteSeverity.Info,
            summary: "Build: refreshed project-root manifest from source manifest inputs.");

        Assert.False(SpectrePipelineSummaryWriter.ShouldRenderOwnerNoteAsPanel(note));
    }
}

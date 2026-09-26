using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    // The full matrix is available in DotNetPublishDeepTests. Keep the PR gate
    // representative so its hosted runner finishes within the job timeout.
    [Theory]
    [InlineData("single-context")]
    [InlineData("two-contexts")]
    [InlineData("custom-props")]
    [InlineData("target-frameworks-context")]
    [InlineData("removed-output-excludes")]
    [InlineData("late-path-mutation")]
    [InlineData("apostrophe-project-path")]
    [InlineData("inactive-pack-path-target")]
    [InlineData("second-context-activated-import")]
    [InlineData("empty-global-default")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PrGateSharedMultiTargetReference(string scenario)
        => ReadSourceProvenance_RestoresEverySelectedFrameworkForSharedMultiTargetReference(scenario);
}

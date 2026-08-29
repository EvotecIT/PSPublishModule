namespace PowerForge.Tests;

public sealed class AppleReleaseSourceTrustExecutionMetadataTests
{
    [Theory]
    [InlineData("PBXShellScriptBuildPhase", "shell-script build phases")]
    [InlineData("PBXBuildRule", "custom build rules")]
    [InlineData("PBXLegacyTarget", "legacy targets")]
    public void BuildExecution_rejects_unproven_execution_metadata(string isa, string expectedMessage)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AppleReleaseSourceTrustService.EnsureExecutionMetadataAccepted(
                isa,
                "/repo/App.xcodeproj/project.pbxproj",
                AppleReleaseSourceTrustValidationScope.BuildExecution));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PBXShellScriptBuildPhase")]
    [InlineData("PBXBuildRule")]
    [InlineData("PBXLegacyTarget")]
    public void SourceInspection_accepts_execution_metadata_that_will_not_run(string isa)
    {
        AppleReleaseSourceTrustService.EnsureExecutionMetadataAccepted(
            isa,
            "/repo/App.xcodeproj/project.pbxproj",
            AppleReleaseSourceTrustValidationScope.SourceInspection);
    }
}

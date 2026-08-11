namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_SourceBoundMetadataMutationWithoutPlanHash_CapturesApprovedInputBytes()
    {
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var metadataPath = Path.Combine(root, "metadata.json");
            var metadata = """
                {
                  "appId": "6778025328",
                  "versionString": "1.2.0",
                  "platform": "iOS",
                  "locale": "en-US",
                  "metadata": { "description": "approved" }
                }
                """;
            File.WriteAllText(metadataPath, metadata);
            var spec = CreateAppleAutomationSpec(root, keyPath);
            spec.AppleApps!.SyncMetadata = true;
            spec.AppleApps.MetadataConfigPath = "metadata.json";

            var result = CreateAppleAutomationService(
                    request => CreateReleaseState(request, "VALID"),
                    prepareAppleDistribution: CreateSuccessfulPreparation)
                .Execute(spec, new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.Prepare,
                    AppleSourceCommit = sourceCommit
                });

            Assert.True(result.Success, result.ErrorMessage);
            var plan = Assert.IsType<PowerForgeAppleReleasePlan>(result.AppleAppPlan);
            Assert.Equal(metadata, PowerForgeReleaseService.ReadApprovedMutationInputText(plan, metadataPath));
            Assert.Contains("metadata.json", plan.ApprovedMutationInputFilesSha256.Keys);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.UploadExisting)]
    [InlineData(PowerForgeAppleReleaseAction.Prepare)]
    public void Execute_NonArchiveAppleAction_RejectsSourceCommitThatIsNotCurrentHead(
        PowerForgeAppleReleaseAction action)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "build/\n");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"));

            var result = service.Execute(
                CreateAppleAutomationSpec(root, keyPath),
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = action,
                    AppleSourceCommit = "0000000000000000000000000000000000000000",
                    RequireImmutableAppleSourceSnapshot = true
                });

            Assert.False(result.Success);
            Assert.Contains("instead of the approved commit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}

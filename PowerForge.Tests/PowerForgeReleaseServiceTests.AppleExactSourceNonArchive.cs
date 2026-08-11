namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
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

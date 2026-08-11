namespace PowerForge.Tests;

public sealed partial class AppleNotarizationServiceTests
{
    [Fact]
    public async Task NotarizeAsync_rejects_private_app_root_replaced_after_acceptance()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var app = Directory.CreateDirectory(Path.Combine(root.FullName, "Accepted.app"));
            await File.WriteAllTextAsync(Path.Combine(app.FullName, "payload"), "approved-app");
            var runner = new AcceptedArtifactMutationRunner();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = app.FullName,
                    KeychainProfile = "powerforge-notary",
                    Assess = false,
                    AcceptedCheckpoint = _ =>
                    {
                        var privateApp = runner.PrivateArtifactPath!;
                        Directory.Move(privateApp, privateApp + ".replaced");
                        Directory.CreateDirectory(privateApp);
                        File.WriteAllText(Path.Combine(privateApp, "payload"), "replacement-app");
                    }
                }));

            Assert.Contains("changed before stapling", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(runner.Commands, command => command.StartsWith("stapler staple", StringComparison.Ordinal));
            Assert.Equal("approved-app", await File.ReadAllTextAsync(Path.Combine(app.FullName, "payload")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task NotarizeAsync_rejects_private_artifact_changed_after_acceptance_before_stapling()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.NotaryTests",
            Guid.NewGuid().ToString("N")));
        try
        {
            var package = Path.Combine(root.FullName, "Accepted.pkg");
            await File.WriteAllTextAsync(package, "approved-package");
            var runner = new AcceptedArtifactMutationRunner();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleNotarizationService(runner).NotarizeAsync(new AppleNotarizationRequest
                {
                    ArtifactPath = package,
                    KeychainProfile = "powerforge-notary",
                    Assess = false,
                    AcceptedCheckpoint = _ => File.WriteAllText(
                        runner.PrivateArtifactPath!,
                        "replacement-after-acceptance")
                }));

            Assert.Contains("changed before stapling", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(runner.Commands, command => command.StartsWith("stapler staple", StringComparison.Ordinal));
            Assert.Equal("approved-package", await File.ReadAllTextAsync(package));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class AcceptedArtifactMutationRunner : IProcessRunner
    {
        internal List<string> Commands { get; } = [];
        internal string? PrivateArtifactPath { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Commands.Add(string.Join(" ", request.Arguments));
            if (request.Arguments.Count == 5 && request.Arguments[0] == "-c")
                File.WriteAllText(request.Arguments[4], "exact-private-app-zip");
            var isSubmission = request.Arguments.Count > 2 && request.Arguments[0] == "notarytool";
            if (isSubmission)
                PrivateArtifactPath = Directory.EnumerateDirectories(request.WorkingDirectory, "*.app").FirstOrDefault() ??
                                      request.Arguments[2];
            var result = new ProcessRunResult(
                0,
                isSubmission ? "{\"id\":\"accepted-boundary\",\"status\":\"Accepted\"}" : "ok",
                string.Empty,
                request.FileName,
                TimeSpan.Zero,
                false);
            request.InvokeCompletionBoundary(result);
            return Task.FromResult(result);
        }
    }
}

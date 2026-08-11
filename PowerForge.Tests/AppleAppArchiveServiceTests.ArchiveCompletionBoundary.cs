namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task CreateArchiveAsync_rejects_archive_replaced_after_xcodebuild_completion_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var archivePath = Path.Combine(root.FullName, "App.xcarchive");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(new PostCompletionArchiveReplacementRunner()).CreateArchiveAsync(
                    new AppleAppArchiveRequest
                    {
                        ProjectPath = project.FullName,
                        Scheme = "App",
                        ArchivePath = archivePath,
                        XcodeBuildExecutable = "xcodebuild-test"
                    }));

            Assert.Contains("changed after xcodebuild completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_rejects_direct_export_replaced_after_xcodebuild_completion_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(new PostCompletionDirectExportReplacementRunner()).UploadArchiveAsync(
                    new AppleAppArchiveUploadRequest
                    {
                        ArchivePath = archive.FullName,
                        ExportPath = Path.Combine(root.FullName, "export"),
                        Destination = "export",
                        Method = "developer-id",
                        XcodeBuildExecutable = "xcodebuild-test"
                    }));

            Assert.Contains("changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Developer ID export", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private sealed class PostCompletionArchiveReplacementRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var archiveIndex = request.Arguments.ToList().IndexOf("-archivePath");
            var archive = Directory.CreateDirectory(request.Arguments[archiveIndex + 1]);
            var payload = Path.Combine(archive.FullName, "payload");
            File.WriteAllText(payload, "archive produced by xcodebuild");
            var result = new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, false);
            request.InvokeCompletionBoundary(result);
            File.WriteAllText(payload, "concurrent replacement");
            return Task.FromResult(result);
        }
    }

    private sealed class PostCompletionDirectExportReplacementRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var exportIndex = request.Arguments.ToList().IndexOf("-exportPath");
            var artifact = Directory.CreateDirectory(Path.Combine(request.Arguments[exportIndex + 1], "App.app"));
            var payload = Path.Combine(artifact.FullName, "payload");
            File.WriteAllText(payload, "export produced by xcodebuild");
            var result = new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.Zero, false);
            request.InvokeCompletionBoundary(result);
            File.WriteAllText(payload, "concurrent replacement");
            return Task.FromResult(result);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task CreateArchiveAsync_builds_xcodebuild_archive_command()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                ArchivePath = Path.Combine(root.FullName, "Tactra.xcarchive"),
                Platform = ApplePlatform.iPadOS,
                XcodeBuildExecutable = "xcodebuild-test"
            });

            Assert.True(result.Succeeded);
            Assert.Equal("generic/platform=iOS", result.Destination);
            Assert.Single(runner.Requests);
            var request = runner.Requests[0];
            Assert.Equal("xcodebuild-test", request.FileName);
            Assert.Contains("-project", request.Arguments);
            Assert.Contains(project.FullName, request.Arguments);
            Assert.Contains("-scheme", request.Arguments);
            Assert.Contains("Tactra", request.Arguments);
            Assert.Contains("-destination", request.Arguments);
            Assert.Contains("generic/platform=iOS", request.Arguments);
            Assert.Contains("-archivePath", request.Arguments);
            Assert.Contains(result.ArchivePath, request.Arguments);
            Assert.Contains("archive", request.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_writes_export_options_and_runs_export_archive()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var plistPath = Path.Combine(root.FullName, "ExportOptions.plist");
            var exportPath = Path.Combine(root.FullName, "export");
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = exportPath,
                ExportOptionsPlistPath = plistPath,
                TeamId = "8ZPGZ79T7J",
                XcodeBuildExecutable = "xcodebuild-test"
            });

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(plistPath));
            var plist = File.ReadAllText(plistPath);
            Assert.Contains("<key>destination</key>", plist, StringComparison.Ordinal);
            Assert.Contains("<string>upload</string>", plist, StringComparison.Ordinal);
            Assert.Contains("<key>method</key>", plist, StringComparison.Ordinal);
            Assert.Contains("<string>app-store-connect</string>", plist, StringComparison.Ordinal);
            Assert.Contains("<key>teamID</key>", plist, StringComparison.Ordinal);
            Assert.Contains("<string>8ZPGZ79T7J</string>", plist, StringComparison.Ordinal);
            Assert.Contains("<key>uploadSymbols</key>", plist, StringComparison.Ordinal);
            Assert.Contains("<true/>", plist, StringComparison.Ordinal);

            Assert.Single(runner.Requests);
            var request = runner.Requests[0];
            Assert.Equal("xcodebuild-test", request.FileName);
            Assert.Equal(new[]
            {
                "-exportArchive",
                "-archivePath",
                archive.FullName,
                "-exportPath",
                exportPath,
                "-exportOptionsPlist",
                plistPath,
                "-allowProvisioningUpdates"
            }, request.Arguments.ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "ok", string.Empty, request.FileName, TimeSpan.FromMilliseconds(1), false));
        }
    }
}

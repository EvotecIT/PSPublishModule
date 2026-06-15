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
    public async Task CreateArchiveAsync_generates_unique_default_archive_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var request = new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                ArchiveRoot = root.FullName,
                Platform = ApplePlatform.iOS,
                XcodeBuildExecutable = "xcodebuild-test"
            };

            var first = await service.CreateArchiveAsync(request);
            var second = await service.CreateArchiveAsync(request);

            Assert.NotEqual(first.ArchivePath, second.ArchivePath);
            Assert.StartsWith(root.FullName, first.ArchivePath, StringComparison.Ordinal);
            Assert.StartsWith(root.FullName, second.ArchivePath, StringComparison.Ordinal);
            Assert.Equal(".xcarchive", Path.GetExtension(first.ArchivePath));
            Assert.Equal(".xcarchive", Path.GetExtension(second.ArchivePath));
            Assert.Equal(2, runner.Requests.Count);
            Assert.Contains(first.ArchivePath, runner.Requests[0].Arguments);
            Assert.Contains(second.ArchivePath, runner.Requests[1].Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_passes_app_store_connect_api_key_arguments()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var keyPath = Path.Combine(root.FullName, "AuthKey_ABC123DEFG.p8");
            await File.WriteAllTextAsync(keyPath, "private-key");
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                ArchivePath = Path.Combine(root.FullName, "Tactra.xcarchive"),
                AppStoreConnectApiKeyPath = keyPath,
                AppStoreConnectApiKeyId = "ABC123DEFG",
                AppStoreConnectApiIssuerId = "issuer-id"
            });

            Assert.True(result.Succeeded);
            var request = Assert.Single(runner.Requests);
            Assert.Contains("-authenticationKeyPath", request.Arguments);
            Assert.Contains(keyPath, request.Arguments);
            Assert.Contains("-authenticationKeyID", request.Arguments);
            Assert.Contains("ABC123DEFG", request.Arguments);
            Assert.Contains("-authenticationKeyIssuerID", request.Arguments);
            Assert.Contains("issuer-id", request.Arguments);
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

    [Fact]
    public async Task UploadArchiveAsync_omits_allow_provisioning_updates_when_disabled()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                AllowProvisioningUpdates = false
            });

            Assert.True(result.Succeeded);
            var request = Assert.Single(runner.Requests);
            Assert.DoesNotContain("-allowProvisioningUpdates", request.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_passes_app_store_connect_api_key_arguments()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var keyPath = Path.Combine(root.FullName, "AuthKey_ABC123DEFG.p8");
            await File.WriteAllTextAsync(keyPath, "private-key");
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                AppStoreConnectApiKeyPath = keyPath,
                AppStoreConnectApiKeyId = "ABC123DEFG",
                AppStoreConnectApiIssuerId = "issuer-id"
            });

            Assert.True(result.Succeeded);
            var request = Assert.Single(runner.Requests);
            Assert.Contains("-authenticationKeyPath", request.Arguments);
            Assert.Contains(keyPath, request.Arguments);
            Assert.Contains("-authenticationKeyID", request.Arguments);
            Assert.Contains("ABC123DEFG", request.Arguments);
            Assert.Contains("-authenticationKeyIssuerID", request.Arguments);
            Assert.Contains("issuer-id", request.Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_rejects_partial_app_store_connect_api_key_configuration()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var service = new AppleAppArchiveService(new CapturingProcessRunner());

            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                AppStoreConnectApiKeyId = "ABC123DEFG"
            }));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_defaults_export_options_plist_inside_export_path()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var exportPath = Path.Combine(root.FullName, "export");
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = exportPath,
                TeamId = "8ZPGZ79T7J",
                XcodeBuildExecutable = "xcodebuild-test"
            });

            var expectedPlistPath = Path.Combine(exportPath, "ExportOptions.plist");
            Assert.True(result.Succeeded);
            Assert.Equal(expectedPlistPath, result.ExportOptionsPlistPath);
            Assert.True(File.Exists(expectedPlistPath));
            Assert.Single(runner.Requests);
            Assert.Contains(expectedPlistPath, runner.Requests[0].Arguments);
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

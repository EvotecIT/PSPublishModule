using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task CreateArchiveAsync_resolves_default_xcodebuild_to_system_binary_on_macOS()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            await service.CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "App",
                ArchivePath = Path.Combine(root.FullName, "App.xcarchive")
            });

            Assert.Equal("/usr/bin/xcodebuild", Assert.Single(runner.Requests).FileName);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

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
    public async Task CreateArchiveAsync_uses_mac_catalyst_destination()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new CapturingProcessRunner();
            var service = new AppleAppArchiveService(runner);

            var result = await service.CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "CasaRay",
                ArchivePath = Path.Combine(root.FullName, "CasaRay.xcarchive"),
                Platform = ApplePlatform.macOS,
                ArchiveVariant = AppleArchiveVariant.MacCatalyst
            });

            Assert.True(result.Succeeded);
            Assert.Equal("generic/platform=macOS,variant=Mac Catalyst", result.Destination);
            Assert.Contains(result.Destination, Assert.Single(runner.Requests).Arguments);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void GetGenericDestination_rejects_mac_catalyst_without_macos_store_platform()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            AppleAppArchiveService.GetGenericDestination(ApplePlatform.iOS, AppleArchiveVariant.MacCatalyst));

        Assert.Contains("Platform macOS", exception.Message, StringComparison.Ordinal);
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
    public async Task CreateArchiveAsync_rejects_api_key_auth_without_provisioning_updates()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var keyPath = Path.Combine(root.FullName, "AuthKey_ABC123DEFG.p8");
            await File.WriteAllTextAsync(keyPath, "private-key");
            var service = new AppleAppArchiveService(new CapturingProcessRunner());

            await Assert.ThrowsAsync<ArgumentException>(() => service.CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "Tactra",
                ArchivePath = Path.Combine(root.FullName, "Tactra.xcarchive"),
                AllowProvisioningUpdates = false,
                AppStoreConnectApiKeyPath = keyPath,
                AppStoreConnectApiKeyId = "ABC123DEFG",
                AppStoreConnectApiIssuerId = "issuer-id"
            }));
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
    public async Task UploadArchiveAsync_captures_direct_export_identity_at_process_boundary()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcarchive"));
            var exportPath = Path.Combine(root.FullName, "export");
            var runner = new CapturingProcessRunner(beforeResult: request =>
            {
                var exportIndex = request.Arguments.ToList().IndexOf("-exportPath");
                var artifact = Directory.CreateDirectory(Path.Combine(request.Arguments[exportIndex + 1], "App.app"));
                File.WriteAllText(Path.Combine(artifact.FullName, "payload"), "signed export");
            });

            var result = await new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = exportPath,
                Destination = "export",
                Method = "developer-id"
            });

            Assert.True(result.Succeeded);
            Assert.Equal(Path.Combine(exportPath, "App.app"), result.ExportArtifactPath);
            Assert.Equal(
                AppleNotarizationService.ComputeArtifactSha256(result.ExportArtifactPath!),
                result.ExportArtifactSha256);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_captures_build_upload_id_from_distribution_log()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var distributionLogs = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcdistributionlogs"));
            var buildUploadId = Guid.NewGuid().ToString();
            File.WriteAllText(
                Path.Combine(distributionLogs.FullName, "ContentDelivery.log"),
                $"UPLOAD SUCCEEDED with no errors{Environment.NewLine}Delivery UUID: {buildUploadId}");
            var runner = new CapturingProcessRunner(new ProcessRunResult(
                0,
                $"Created bundle at path \"{distributionLogs.FullName}\"",
                string.Empty,
                "xcodebuild",
                TimeSpan.FromSeconds(1),
                false));
            var service = new AppleAppArchiveService(runner);

            var result = await service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export")
            });

            Assert.True(result.Succeeded);
            Assert.Equal(distributionLogs.FullName, result.DistributionLogPath);
            Assert.Equal(buildUploadId, result.BuildUploadId);
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
    public async Task UploadArchiveAsync_rejects_api_key_auth_without_provisioning_updates()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcarchive"));
            var keyPath = Path.Combine(root.FullName, "AuthKey_ABC123DEFG.p8");
            await File.WriteAllTextAsync(keyPath, "private-key");
            var service = new AppleAppArchiveService(new CapturingProcessRunner());

            await Assert.ThrowsAsync<ArgumentException>(() => service.UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                AllowProvisioningUpdates = false,
                AppStoreConnectApiKeyPath = keyPath,
                AppStoreConnectApiKeyId = "ABC123DEFG",
                AppStoreConnectApiIssuerId = "issuer-id"
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadArchiveAsync_validates_required_privacy_purpose_strings_in_final_archive(bool macBundleLayout)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcarchive"));
            var app = Directory.CreateDirectory(Path.Combine(archive.FullName, "Products", "Applications", "CasaRay.app"));
            var bundleRoot = macBundleLayout
                ? Directory.CreateDirectory(Path.Combine(app.FullName, "Contents")).FullName
                : app.FullName;
            File.WriteAllText(Path.Combine(bundleRoot, "Info.plist"), "fixture");
            var runner = new PrivacyProcessRunner(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CFBundleIdentifier"] = "com.evotecit.casaray",
                ["NSCameraUsageDescription"] = "CasaRay does not capture from this device's camera."
            });

            var result = await new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
            {
                ArchivePath = archive.FullName,
                ExportPath = Path.Combine(root.FullName, "export"),
                BundleId = "com.evotecit.casaray",
                RequiredPrivacyUsageDescriptionKeys = ["NSCameraUsageDescription"]
            });

            Assert.True(result.Succeeded);
            Assert.Equal(3, runner.Requests.Count);
            Assert.EndsWith("xcodebuild", runner.Requests[^1].FileName, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UploadArchiveAsync_blocks_upload_when_required_privacy_purpose_string_is_missing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var archive = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcarchive"));
            var app = Directory.CreateDirectory(Path.Combine(archive.FullName, "Products", "Applications", "CasaRay.app"));
            File.WriteAllText(Path.Combine(app.FullName, "Info.plist"), "fixture");
            var runner = new PrivacyProcessRunner(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CFBundleIdentifier"] = "com.evotecit.casaray"
            });

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).UploadArchiveAsync(new AppleAppArchiveUploadRequest
                {
                    ArchivePath = archive.FullName,
                    ExportPath = Path.Combine(root.FullName, "export"),
                    BundleId = "com.evotecit.casaray",
                    RequiredPrivacyUsageDescriptionKeys = ["NSCameraUsageDescription"]
                }));

            Assert.Contains("NSCameraUsageDescription", exception.Message, StringComparison.Ordinal);
            Assert.Contains("blocked before App Store Connect delivery", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(runner.Requests, request => request.FileName.EndsWith("xcodebuild", StringComparison.Ordinal));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        private readonly ProcessRunResult _result;
        private readonly Action<ProcessRunRequest>? _beforeResult;

        public CapturingProcessRunner(ProcessRunResult? result = null, Action<ProcessRunRequest>? beforeResult = null)
        {
            _result = result ?? new ProcessRunResult(0, "ok", string.Empty, "xcodebuild", TimeSpan.FromMilliseconds(1), false);
            _beforeResult = beforeResult;
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            request.InvokeStartBoundary();
            _beforeResult?.Invoke(request);
            if (_result.Succeeded)
            {
                for (var index = 0; index + 1 < request.Arguments.Count; index++)
                {
                    if (!request.Arguments[index].Equals("-archivePath", StringComparison.Ordinal) ||
                        !request.Arguments.Contains("archive"))
                    {
                        continue;
                    }

                    var archive = Directory.CreateDirectory(request.Arguments[index + 1]);
                    File.WriteAllText(Path.Combine(archive.FullName, "archive.bin"), "archive");
                    break;
                }
            }
            return Task.FromResult(_result);
        }
    }

    private sealed class PrivacyProcessRunner : IProcessRunner
    {
        private readonly IReadOnlyDictionary<string, string> _plistValues;

        public PrivacyProcessRunner(IReadOnlyDictionary<string, string> plistValues)
        {
            _plistValues = plistValues;
        }

        public List<ProcessRunRequest> Requests { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.FileName.EndsWith("plutil", StringComparison.Ordinal))
            {
                var key = request.Arguments[1];
                var found = _plistValues.TryGetValue(key, out var value);
                return Task.FromResult(new ProcessRunResult(
                    found ? 0 : 1,
                    found ? value! : string.Empty,
                    found ? string.Empty : $"Missing {key}",
                    request.FileName,
                    TimeSpan.FromMilliseconds(1),
                    false));
            }

            return Task.FromResult(new ProcessRunResult(
                0,
                "ok",
                string.Empty,
                request.FileName,
                TimeSpan.FromMilliseconds(1),
                false));
        }
    }
}

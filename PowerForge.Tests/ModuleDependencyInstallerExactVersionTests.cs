using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleDependencyInstallerExactVersionTests
{
    private const string InstalledVersionsMarker = "PFMOD::ITEM::";
    private const string ExactVersionProbeMarker = "PFMODLOC::FOUND::";
    private const string PsResourceInstallMarker = "PFPSRG::INSTALL::OK";
    private const string PowerShellGetInstallMarker = "PFMOD::INSTALL::OK";
    private const string LocatorErrorMarker = "PFMODLOC::ERROR::";

    [Fact]
    public void EnsureInstalled_DoesNotInstall_WhenExactRequiredVersionAlreadyExistsBesideNewerVersion()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = new HashSet<string>(new[] { "0.25.0" }, StringComparer.OrdinalIgnoreCase)
            });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureInstalled(new[]
        {
            new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0")
        });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Satisfied, result.Status);
        Assert.Equal("0.26.0", result.InstalledVersion);
        Assert.Equal("Exact required version 0.25.0 already present (latest installed: 0.26.0)", result.Message);
        Assert.Equal(0, runner.InstallCalls);
    }

    [Fact]
    public void EnsureUpdated_InstallsExactRequiredVersion_WhenLatestInstalledVersionDiffers()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(new[]
        {
            new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0")
        });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("0.26.0", result.InstalledVersion);
        Assert.Equal("0.25.0", result.RequestedVersion);
        Assert.Equal("Exact version required: 0.25.0 (installed: 0.26.0)", result.Message);
        Assert.Equal(1, runner.ExactProbeCalls);
        Assert.Equal(1, runner.InstallCalls);
    }

    [Fact]
    public void EnsureUpdated_DoesNotInstall_WhenExactRequiredVersionAlreadyExistsBesideNewerVersion()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = new HashSet<string>(new[] { "0.25.0" }, StringComparer.OrdinalIgnoreCase)
            });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(new[]
        {
            new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0")
        });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Satisfied, result.Status);
        Assert.Equal("0.26.0", result.InstalledVersion);
        Assert.Equal("0.25.0", result.RequestedVersion);
        Assert.Equal("Exact required version 0.25.0 already present (latest installed: 0.26.0)", result.Message);
        Assert.Equal(1, runner.ExactProbeCalls);
        Assert.Equal(0, runner.InstallCalls);
    }

    [Fact]
    public void EnsureUpdated_InstallsExactRequiredVersion_WhenOnlyDifferentScopeHasRequiredVersion()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = new HashSet<string>(new[] { "0.25.0" }, StringComparer.OrdinalIgnoreCase)
            },
            installedExactScopes: new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["0.25.0"] = "AllUsers"
                }
            });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureUpdated(new[]
        {
            new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0", installScope: "CurrentUser")
        });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("0.25.0", result.RequestedVersion);
        Assert.Equal(1, runner.ExactProbeCalls);
        Assert.Equal(1, runner.InstallCalls);
        Assert.NotNull(runner.LastExactProbeArguments);
        Assert.Equal("CurrentUser", runner.LastExactProbeArguments![4]);
        Assert.NotNull(runner.LastInstallArguments);
        Assert.Equal("CurrentUser", runner.LastInstallArguments![3]);
    }

    [Fact]
    public void EnsureInstalled_ReportsProbeFailureForCurrentDependency_AndContinuesProcessing()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Preview.Module"] = "2.0.0",
                ["Stable.Module"] = "1.0.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            failingExactVersions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Preview.Module"] = "1.0.0-preview"
            });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureInstalled(new[]
        {
            new ModuleDependency("Preview.Module", requiredVersion: "1.0.0-preview"),
            new ModuleDependency("Stable.Module", requiredVersion: "1.0.0")
        });

        Assert.Equal(2, results.Count);

        var failed = Assert.Single(results, r => string.Equals(r.Name, "Preview.Module", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ModuleDependencyInstallStatus.Failed, failed.Status);
        Assert.Contains("Get-Module -ListAvailable failed", failed.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var satisfied = Assert.Single(results, r => string.Equals(r.Name, "Stable.Module", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ModuleDependencyInstallStatus.Satisfied, satisfied.Status);
        Assert.Equal("Exact version already installed", satisfied.Message);
    }

    [Fact]
    public void EnsureInstalled_ForceBypassesExactVersionProbe()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
            failingExactVersions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.25.0"
            });
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureInstalled(
            new[] { new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0") },
            force: true);

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal(0, runner.ExactProbeCalls);
        Assert.Equal(1, runner.InstallCalls);
    }

    [Fact]
    public void EnsureInstalled_DoesNotProbeExactVersion_WhenModuleIsNotInstalled()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureInstalled(
            new[] { new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0") });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Installed, result.Status);
        Assert.Equal(0, runner.ExactProbeCalls);
        Assert.Equal(1, runner.InstallCalls);
    }

    [Fact]
    public void EnsureInstalled_PassesInstallScopeToPSResourceGet()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        installer.EnsureInstalled(
            new[] { new ModuleDependency("PSSharedGoods", requiredVersion: "0.25.0", installScope: "AllUsers") },
            repository: "Company");

        Assert.Equal(1, runner.InstallCalls);
        Assert.NotNull(runner.LastInstallArguments);
        Assert.Equal("PSSharedGoods", runner.LastInstallArguments![0]);
        Assert.Equal("0.25.0", runner.LastInstallArguments[1]);
        Assert.Equal("Company", runner.LastInstallArguments[2]);
        Assert.Equal("AllUsers", runner.LastInstallArguments[3]);
    }

    [Fact]
    public void EnsureInstalled_UsesOpenLowerBoundForMaxOnlyPSResourceRange()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        installer.EnsureInstalled(
            new[]
            {
                new ModuleDependency(
                    "PSSharedGoods",
                    maximumVersion: "2.0.0",
                    maximumVersionInclusive: false)
            },
            repository: "Company");

        Assert.Equal(1, runner.InstallCalls);
        Assert.NotNull(runner.LastInstallArguments);
        Assert.Equal("(, 2.0.0)", runner.LastInstallArguments![1]);
    }

    [Fact]
    public void EnsureInstalled_InstallsWhenOnlyAnotherScopeSatisfiesAnyVersionPolicy()
    {
        var runner = new StubPowerShellRunner(
            latestInstalledVersions: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PSSharedGoods"] = "0.26.0"
            },
            installedExactVersions: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));
        var installer = new ModuleDependencyInstaller(runner, new NullLogger());

        var results = installer.EnsureInstalled(
            new[] { new ModuleDependency("PSSharedGoods", installScope: "AllUsers") });

        var result = Assert.Single(results);
        Assert.Equal(ModuleDependencyInstallStatus.Updated, result.Status);
        Assert.Equal("Module is not installed in requested scope", result.Message);
        Assert.Equal(1, runner.ExactProbeCalls);
        Assert.Equal(1, runner.InstallCalls);
        Assert.NotNull(runner.LastExactProbeArguments);
        Assert.Equal("AllUsers", runner.LastExactProbeArguments![4]);
        Assert.NotNull(runner.LastInstallArguments);
        Assert.Equal("AllUsers", runner.LastInstallArguments![3]);
    }

    [Fact]
    public void EnsureInstalled_BootstrapsPSResourceGetDirectly_WhenRepositoryClientsAreUnavailable()
    {
        var moduleRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            Uri? downloadedUri = null;
            var runner = new ToolUnavailablePowerShellRunner();
            var installer = new ModuleDependencyInstaller(
                runner,
                new NullLogger(),
                galleryVersionResolver: (packageId, includePrerelease, timeout) => new[]
                {
                    new PowerShellGalleryPackageVersion("1.1.0", isListed: true, isPrerelease: false),
                    new PowerShellGalleryPackageVersion("1.2.0", isListed: true, isPrerelease: false)
                },
                directPackageDownloader: (uri, destination, timeout) =>
                {
                    downloadedUri = uri;
                    CreatePSResourceGetPackage(destination, "1.2.0");
                },
                currentUserModuleRootResolver: () => moduleRoot.FullName);

            var results = installer.EnsureInstalled(new[]
            {
                new ModuleDependency("Microsoft.PowerShell.PSResourceGet", minimumVersion: "1.1.1")
            });

            var result = Assert.Single(results);
            Assert.Equal(ModuleDependencyInstallStatus.Installed, result.Status);
            Assert.Equal("DirectPowerShellGallery", result.Installer);
            Assert.NotNull(downloadedUri);
            Assert.Contains("/Microsoft.PowerShell.PSResourceGet/1.2.0", downloadedUri!.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(
                moduleRoot.FullName,
                "Microsoft.PowerShell.PSResourceGet",
                "1.2.0",
                "Microsoft.PowerShell.PSResourceGet.psd1")));
            Assert.Equal(1, runner.PSResourceGetInstallCalls);
            Assert.Equal(1, runner.PowerShellGetInstallCalls);
        }
        finally
        {
            try { moduleRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string?> _latestInstalledVersions;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _installedExactVersions;
        private readonly IReadOnlyDictionary<string, Dictionary<string, string>> _installedExactScopes;
        private readonly IReadOnlyDictionary<string, string> _failingExactVersions;

        public int ExactProbeCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public IReadOnlyList<string>? LastInstallArguments { get; private set; }
        public IReadOnlyList<string>? LastExactProbeArguments { get; private set; }

        public StubPowerShellRunner(
            IReadOnlyDictionary<string, string?> latestInstalledVersions,
            IReadOnlyDictionary<string, HashSet<string>> installedExactVersions,
            IReadOnlyDictionary<string, Dictionary<string, string>>? installedExactScopes = null,
            IReadOnlyDictionary<string, string>? failingExactVersions = null)
        {
            _latestInstalledVersions = latestInstalledVersions;
            _installedExactVersions = installedExactVersions;
            _installedExactScopes = installedExactScopes ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            _failingExactVersions = failingExactVersions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Assert.NotNull(request.ScriptPath);
            var script = File.ReadAllText(request.ScriptPath!);

            if (script.Contains(InstalledVersionsMarker, StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names.Select(name =>
                {
                    _latestInstalledVersions.TryGetValue(name, out var version);
                    return InstalledVersionsMarker + Encode(name) + "::" + Encode(version ?? string.Empty);
                });

                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            if (script.Contains(ExactVersionProbeMarker, StringComparison.Ordinal))
            {
                ExactProbeCalls++;
                LastExactProbeArguments = request.Arguments;
                var name = request.Arguments[0];
                var requiredVersion = request.Arguments[1];
                if (_failingExactVersions.TryGetValue(name, out var failingVersion) &&
                    string.Equals(failingVersion, requiredVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new PowerShellRunResult(1, LocatorErrorMarker + Encode("Cannot parse required version"), string.Empty, "pwsh.exe");
                }

                var found = _installedExactVersions.TryGetValue(name, out var versions) && versions.Contains(requiredVersion);
                if (found &&
                    _installedExactScopes.TryGetValue(name, out var scopedVersions) &&
                    scopedVersions.TryGetValue(requiredVersion, out var installedScope) &&
                    request.Arguments.Count > 4 &&
                    !string.IsNullOrWhiteSpace(request.Arguments[4]) &&
                    !string.Equals(installedScope, request.Arguments[4], StringComparison.OrdinalIgnoreCase))
                {
                    found = false;
                }
                if (!found)
                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");

                var modulePath = Path.Combine(@"C:\Modules", name, requiredVersion);
                var stdout = ExactVersionProbeMarker + Encode(requiredVersion) + "::" + Encode(modulePath);
                return new PowerShellRunResult(0, stdout, string.Empty, "pwsh.exe");
            }

            if (script.Contains(PsResourceInstallMarker, StringComparison.Ordinal) ||
                script.Contains(PowerShellGetInstallMarker, StringComparison.Ordinal))
            {
                InstallCalls++;
                LastInstallArguments = request.Arguments;
                return new PowerShellRunResult(0, PsResourceInstallMarker, string.Empty, "pwsh.exe");
            }

            throw new InvalidOperationException("Unexpected script invocation in test.");
        }

        private static string[] DecodeLines(string value)
        {
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0)
                .ToArray();
        }

        private static string Encode(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private sealed class ToolUnavailablePowerShellRunner : IPowerShellRunner
    {
        public int PSResourceGetInstallCalls { get; private set; }
        public int PowerShellGetInstallCalls { get; private set; }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            Assert.NotNull(request.ScriptPath);
            var script = File.ReadAllText(request.ScriptPath!);

            if (script.Contains(InstalledVersionsMarker, StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names.Select(name => InstalledVersionsMarker + Encode(name) + "::" + Encode(string.Empty));
                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            if (script.Contains(PsResourceInstallMarker, StringComparison.Ordinal))
            {
                PSResourceGetInstallCalls++;
                return new PowerShellRunResult(3, "PFPSRG::ERROR::" + Encode("PSResourceGet unavailable"), string.Empty, "pwsh.exe");
            }

            if (script.Contains(PowerShellGetInstallMarker, StringComparison.Ordinal))
            {
                PowerShellGetInstallCalls++;
                return new PowerShellRunResult(3, "PFMOD::ERROR::" + Encode("PowerShellGet unavailable"), string.Empty, "pwsh.exe");
            }

            throw new InvalidOperationException("Unexpected script invocation in test.");
        }
    }

    private static void CreatePSResourceGetPackage(string destinationPath, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var archive = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        AddEntry(archive, "Microsoft.PowerShell.PSResourceGet.psd1", "@{ ModuleVersion = '" + version + "' }");
        AddEntry(archive, "Microsoft.PowerShell.PSResourceGet.psm1", string.Empty);
        AddEntry(archive, "Microsoft.PowerShell.PSResourceGet.nuspec", "<package />");
        AddEntry(archive, "package/services/metadata/core-properties/metadata.psmdcp", string.Empty);
        AddEntry(archive, "[Content_Types].xml", string.Empty);

        static void AddEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static string[] DecodeLines(string value)
    {
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(value));
        return text
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
}

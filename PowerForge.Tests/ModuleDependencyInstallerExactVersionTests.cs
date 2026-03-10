using System;
using System.Collections.Generic;
using System.IO;
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

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string?> _latestInstalledVersions;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _installedExactVersions;
        private readonly IReadOnlyDictionary<string, string> _failingExactVersions;

        public int InstallCalls { get; private set; }

        public StubPowerShellRunner(
            IReadOnlyDictionary<string, string?> latestInstalledVersions,
            IReadOnlyDictionary<string, HashSet<string>> installedExactVersions,
            IReadOnlyDictionary<string, string>? failingExactVersions = null)
        {
            _latestInstalledVersions = latestInstalledVersions;
            _installedExactVersions = installedExactVersions;
            _failingExactVersions = failingExactVersions ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var script = File.ReadAllText(request.ScriptPath);

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
                var name = request.Arguments[0];
                var requiredVersion = request.Arguments[1];
                if (_failingExactVersions.TryGetValue(name, out var failingVersion) &&
                    string.Equals(failingVersion, requiredVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return new PowerShellRunResult(1, LocatorErrorMarker + Encode("Cannot parse required version"), string.Empty, "pwsh.exe");
                }

                var found = _installedExactVersions.TryGetValue(name, out var versions) && versions.Contains(requiredVersion);
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
}

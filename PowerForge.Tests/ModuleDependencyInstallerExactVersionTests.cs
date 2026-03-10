using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleDependencyInstallerExactVersionTests
{
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
        Assert.Equal("Exact version already installed", result.Message);
        Assert.Equal(0, runner.InstallCalls);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly IReadOnlyDictionary<string, string?> _latestInstalledVersions;
        private readonly IReadOnlyDictionary<string, HashSet<string>> _installedExactVersions;

        public int InstallCalls { get; private set; }

        public StubPowerShellRunner(
            IReadOnlyDictionary<string, string?> latestInstalledVersions,
            IReadOnlyDictionary<string, HashSet<string>> installedExactVersions)
        {
            _latestInstalledVersions = latestInstalledVersions;
            _installedExactVersions = installedExactVersions;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var script = File.ReadAllText(request.ScriptPath);

            if (script.Contains("PFMOD::ITEM::", StringComparison.Ordinal))
            {
                var names = DecodeLines(request.Arguments[0]);
                var lines = names.Select(name =>
                {
                    _latestInstalledVersions.TryGetValue(name, out var version);
                    return "PFMOD::ITEM::" + Encode(name) + "::" + Encode(version ?? string.Empty);
                });

                return new PowerShellRunResult(0, string.Join(Environment.NewLine, lines), string.Empty, "pwsh.exe");
            }

            if (script.Contains("PFMODLOC::FOUND::", StringComparison.Ordinal))
            {
                var name = request.Arguments[0];
                var requiredVersion = request.Arguments[1];
                var found = _installedExactVersions.TryGetValue(name, out var versions) && versions.Contains(requiredVersion);
                if (!found)
                    return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh.exe");

                var modulePath = Path.Combine(@"C:\Modules", name, requiredVersion);
                var stdout = "PFMODLOC::FOUND::" + Encode(requiredVersion) + "::" + Encode(modulePath);
                return new PowerShellRunResult(0, stdout, string.Empty, "pwsh.exe");
            }

            if (script.Contains("PFPSRG::INSTALL::OK", StringComparison.Ordinal) ||
                script.Contains("PFMOD::INSTALL::OK", StringComparison.Ordinal))
            {
                InstallCalls++;
                return new PowerShellRunResult(0, "PFPSRG::INSTALL::OK", string.Empty, "pwsh.exe");
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

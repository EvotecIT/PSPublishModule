using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationEnvironmentCaseTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.EnvironmentCase.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationEnvironmentCaseTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Public_configuration_models_preserve_case_distinct_environment_entries() {
        var command = new ReleaseCommandValidation();
        var action = new PowerForgeReleaseValidationAction();
        foreach (var target in new[] { command.Environment, action.Environment }) {
            Populate(target);
            Assert.Equal(6, target.Count);
            Assert.Equal("first", target["PF_CASE"]);
            Assert.Equal("second", target["pf_case"]);
            Assert.Null(target["PF_REMOVE"]);
            Assert.Equal("retained", target["pf_remove"]);
            Assert.Equal("removed-on-windows", target["PF_CLEAR"]);
            Assert.Null(target["pf_clear"]);
        }
    }

    [Fact]
    public async Task Public_command_delivers_platform_environment_case_semantics_to_a_real_child() {
        var command = new ReleaseCommandValidation {
            FileName = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? ["/d", "/c", "echo %PF_CASE%/%pf_case% & if defined PF_CLEAR (echo clear-present) else (echo clear-absent)"]
                : ["-c", "printf '%s/%s\\n' \"$PF_CASE\" \"$pf_case\"; if [ \"${pf_clear+x}\" = x ]; then printf 'clear-present\\n'; else printf 'clear-absent\\n'; fi"],
            TimeoutSeconds = 20
        };
        Populate(command.Environment);

        var result = await new ReleaseValidationService().RunCommandAsync(command);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal(OperatingSystem.IsWindows() ? new[] { "second/second", "clear-absent" } : new[] { "first/second", "clear-absent" },
            result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void Staged_script_preserves_case_and_removals_while_context_variables_remain_authoritative() {
        var script = Path.Combine(_root, "probe.ps1");
        File.WriteAllText(script, "'ok'");
        var action = new PowerForgeReleaseValidationAction { FilePath = script };
        Populate(action.Environment);
        action.Environment["POWERFORGE_CONTEXT"] = "untrusted-context";
        action.Environment["powerforge_context"] = null;
        action.Environment["POWERFORGE_RELEASE_VERSION"] = "untrusted-version";
        action.Environment["powerforge_release_version"] = null;
        string? contextPath = null;
        var runner = new Runner(request => {
            AssertEnvironment(request.EnvironmentVariables!);
            contextPath = request.EnvironmentVariables!["POWERFORGE_CONTEXT"];
            Assert.NotNull(contextPath);
            using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
            Assert.Equal("1.2.3", document.RootElement.GetProperty("ResolvedVersion").GetString());
            Assert.Equal("1.2.3", request.EnvironmentVariables["POWERFORGE_RELEASE_VERSION"]);
            Assert.Equal("AfterStaging", request.EnvironmentVariables["POWERFORGE_RELEASE_STAGE"]);
            if (!OperatingSystem.IsWindows()) {
                Assert.Null(request.EnvironmentVariables["powerforge_context"]);
                Assert.Null(request.EnvironmentVariables["powerforge_release_version"]);
            }
        });

        var result = new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(action,
            new() { ProjectRoot = _root, ResolvedVersion = "1.2.3" }, _root, CancellationToken.None);

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal(1, runner.Calls);
        Assert.NotNull(contextPath);
        Assert.False(File.Exists(contextPath));
    }

    [Fact]
    public async Task Tool_probes_preserve_case_and_nulls_after_both_installation_modes() {
        using (var zip = ZipFile.Open(Path.Combine(_root, "Example.Tool.nupkg"), ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(zip.CreateEntry("Example.Tool.nuspec").Open());
            writer.Write("<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var command = new ReleaseCommandValidation { FileName = "environment-probe" };
        Populate(command.Environment);
        var installs = 0;
        var probes = 0;
        var runner = new Runner(request => {
            if (request.Arguments.Contains("install")) {
                installs++;
                ReleaseValidationToolInstallFixture.Complete(request, "example");
            }
            if (request.FileName == "environment-probe") {
                probes++;
                AssertEnvironment(request.EnvironmentVariables!);
                Assert.Equal(Path.Combine(request.WorkingDirectory, "packages"), request.EnvironmentVariables!["NUGET_PACKAGES"]);
            }
        });

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Tools = [new() {
            PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example", IncludeManifestInstall = true,
            Commands = [command]
        }] }, request: new() { ProjectRoot = _root });

        Assert.True(report.Success, string.Join(Environment.NewLine, report.Errors));
        Assert.Equal(2, installs);
        Assert.Equal(2, probes);
    }

    private static void Populate(IDictionary<string, string?> environment) {
        environment["PF_CASE"] = "first";
        environment["pf_case"] = "second";
        environment["PF_REMOVE"] = null;
        environment["pf_remove"] = "retained";
        environment["PF_CLEAR"] = "removed-on-windows";
        environment["pf_clear"] = null;
    }

    private static void AssertEnvironment(IReadOnlyDictionary<string, string?> environment) {
        if (OperatingSystem.IsWindows()) {
            Assert.Equal("second", Assert.Single(environment, pair => pair.Key.Equals("PF_CASE", StringComparison.OrdinalIgnoreCase)).Value);
            Assert.Equal("retained", Assert.Single(environment, pair => pair.Key.Equals("PF_REMOVE", StringComparison.OrdinalIgnoreCase)).Value);
            Assert.Null(Assert.Single(environment, pair => pair.Key.Equals("PF_CLEAR", StringComparison.OrdinalIgnoreCase)).Value);
        } else {
            Assert.Equal("second", environment["pf_case"]);
            Assert.Equal("retained", environment["pf_remove"]);
            Assert.Null(environment["pf_clear"]);
            Assert.Equal("first", environment["PF_CASE"]);
            Assert.Null(environment["PF_REMOVE"]);
            Assert.Equal("removed-on-windows", environment["PF_CLEAR"]);
        }
    }

    private sealed class Runner(Action<ProcessRunRequest> inspect) : IProcessRunner {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) {
            Calls++;
            inspect(request);
            return Task.FromResult(new ProcessRunResult(0, "ok", "", request.FileName, TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

using System.Diagnostics;

namespace PowerForge.Tests;

public sealed partial class AppleAppArchiveServiceTests
{
    [Fact]
    public async Task CreateArchiveAsync_exact_source_builds_from_validated_private_package_checkouts()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new ExactPackageProcessRunner();

            var result = await new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
            {
                ProjectPath = project.FullName,
                Scheme = "App",
                ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                RequireExactPackageSnapshot = true
            });

            Assert.True(result.Succeeded);
            Assert.Equal(2, runner.Requests.Count);
            var resolve = runner.Requests[0];
            var archive = runner.Requests[1];
            Assert.Contains("-resolvePackageDependencies", resolve.Arguments);
            Assert.Contains("-clonedSourcePackagesDirPath", resolve.Arguments);
            Assert.Contains("-onlyUsePackageVersionsFromResolvedFile", resolve.Arguments);
            Assert.Equal("1", resolve.EnvironmentVariables!["GIT_CONFIG_NOSYSTEM"]);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", resolve.EnvironmentVariables["PATH"]);
            Assert.Contains("-clonedSourcePackagesDirPath", archive.Arguments);
            Assert.Equal("/usr/bin:/bin:/usr/sbin:/sbin", archive.EnvironmentVariables!["PATH"]);
            Assert.Equal(
                resolve.Arguments[Array.IndexOf(resolve.Arguments.ToArray(), "-clonedSourcePackagesDirPath") + 1],
                archive.Arguments[Array.IndexOf(archive.Arguments.ToArray(), "-clonedSourcePackagesDirPath") + 1]);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task CreateArchiveAsync_exact_source_rejects_transient_package_checkout_mutation()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "App.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var runner = new ExactPackageProcessRunner(mutateDuringArchive: true);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AppleAppArchiveService(runner).CreateArchiveAsync(new AppleAppArchiveRequest
                {
                    ProjectPath = project.FullName,
                    Scheme = "App",
                    ArchivePath = Path.Combine(root.FullName, "App.xcarchive"),
                    RequireExactPackageSnapshot = true
                }));

            Assert.Contains("materialized Swift package root changed", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(runner.SourcePackagesRoot));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class ExactPackageProcessRunner : IProcessRunner
    {
        private readonly bool _mutateDuringArchive;

        internal ExactPackageProcessRunner(bool mutateDuringArchive = false)
        {
            _mutateDuringArchive = mutateDuringArchive;
        }

        internal List<ProcessRunRequest> Requests { get; } = new();

        internal string SourcePackagesRoot { get; private set; } = string.Empty;

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.Contains("-resolvePackageDependencies"))
            {
                var index = Array.IndexOf(request.Arguments.ToArray(), "-clonedSourcePackagesDirPath");
                SourcePackagesRoot = request.Arguments[index + 1];
                var checkout = Directory.CreateDirectory(Path.Combine(SourcePackagesRoot, "checkouts", "Shared")).FullName;
                RunGit(checkout, "init", "--quiet");
                RunGit(checkout, "config", "user.name", "PowerForge Tests");
                RunGit(checkout, "config", "user.email", "powerforge-tests@example.invalid");
                File.WriteAllText(
                    Path.Combine(checkout, "Package.swift"),
                    "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\")\n");
                RunGit(checkout, "add", ".");
                RunGit(checkout, "commit", "--quiet", "-m", "Package fixture");
            }
            else if (_mutateDuringArchive)
            {
                var manifest = Path.Combine(SourcePackagesRoot, "checkouts", "Shared", "Package.swift");
                var original = File.ReadAllText(manifest);
                File.WriteAllText(manifest, original + "// injected\n");
                File.WriteAllText(manifest, original);
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

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git fixture process.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {output}{error}");
    }
}

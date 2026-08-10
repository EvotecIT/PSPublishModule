using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void Capture_revalidates_remote_package_after_failed_inspection()
    {
        using var scope = new TemporaryDirectoryScope();
        var remoteRoot = scope.CreateDirectory("RetryRemotePackage");
        RunGit(remoteRoot, "init", "--quiet");
        File.WriteAllText(
            Path.Combine(remoteRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Remote\")");
        var revision = CommitRepository(remoteRoot);

        var repositoryRoot = scope.CreateDirectory("RetryRemoteConsumer");
        var project = scope.CreateDirectory(Path.Combine("RetryRemoteConsumer", "Sample.xcodeproj"));
        const string remoteUrl = "https://example.invalid/RetryRemotePackage.git";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCRemoteSwiftPackageReference; repositoryURL = \"{remoteUrl}\"; requirement = {{ kind = revision; revision = {revision}; }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);
        var resolverCalls = 0;
        var service = new AppleReleaseSourceTrustService(
            remotePackageCheckoutResolver: (_, _) =>
            {
                if (++resolverCalls == 1)
                    throw new InvalidOperationException("transient remote inspection failure");
                return remoteRoot;
            });

        Assert.Throws<InvalidOperationException>(() => service.Capture(repositoryRoot, configPath));

        Assert.Equal(expected, service.ResolveExactCommit(repositoryRoot, configPath));
        Assert.Equal(2, resolverCalls);
    }

    [Fact]
    public void Capture_rejects_gitlinks_in_exact_remote_package_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var childRoot = scope.CreateDirectory("RemotePackageChild");
        RunGit(childRoot, "init", "--quiet");
        File.WriteAllText(Path.Combine(childRoot, "Payload.swift"), "public let payload = 42\n");
        var childRevision = CommitRepository(childRoot);

        var remoteRoot = scope.CreateDirectory("RemotePackageWithGitlink");
        RunGit(remoteRoot, "init", "--quiet");
        File.WriteAllText(
            Path.Combine(remoteRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Remote\")");
        _ = CommitRepository(remoteRoot);
        RunGit(remoteRoot, "update-index", "--add", "--cacheinfo", $"160000,{childRevision},Dependencies/Child");
        RunGit(remoteRoot, "commit", "-m", "Add remote package gitlink", "--quiet");
        Directory.CreateDirectory(Path.Combine(remoteRoot, "Dependencies", "Child"));
        var remoteRevision = ReadFixtureHead(remoteRoot);

        var repositoryRoot = scope.CreateDirectory("RemoteGitlinkConsumer");
        var project = scope.CreateDirectory(Path.Combine("RemoteGitlinkConsumer", "Sample.xcodeproj"));
        const string remoteUrl = "https://example.invalid/RemotePackageWithGitlink.git";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCRemoteSwiftPackageReference; repositoryURL = \"{remoteUrl}\"; requirement = {{ kind = revision; revision = {remoteRevision}; }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        var service = new AppleReleaseSourceTrustService(
            remotePackageCheckoutResolver: (_, _) => remoteRoot);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Capture(repositoryRoot, configPath));

        Assert.Contains("Git submodule", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dependencies/Child", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_checkout_path_literals_in_swift_manifests()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "ManifestFilePathRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let here = #filePath\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", swiftSettings: [.define(\"MANIFEST_PATH\", to: here)])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("#filePath", exception.Message, StringComparison.Ordinal);
        Assert.Contains("checkout or host state", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_absolute_linker_input_containing_rpath_text()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "EmbeddedRpathRepo",
            "OTHER_LDFLAGS = /tmp/@rpath/libInjected.a\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/@rpath/libInjected.a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Path-like token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_expands_forwarded_linker_response_files()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ForwardedResponseFileRepo");
        var project = scope.CreateDirectory(Path.Combine("ForwardedResponseFileRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { OTHER_LDFLAGS = -Wl,@Injected.rsp; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Injected.rsp"), "/tmp/Injected.a");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/Injected.a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_bare_positional_linker_inputs()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "BareLinkerInputRepo",
            "OTHER_LDFLAGS = Injected.a\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Injected.a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_forwarded_linker_runtime_paths()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ForwardedRuntimePathRepo",
            "OTHER_LDFLAGS = -Wl,-rpath,/tmp/InjectedFrameworks\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/InjectedFrameworks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_imacros_compiler_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "CompilerMacrosInputRepo",
            "OTHER_CFLAGS = -imacros Injected.h\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Injected.h", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_host_dependent_source_selection()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "HostSourceSelectionRepo",
            "EXCLUDED_SOURCE_FILE_NAMES = $(USER).swift\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("EXCLUDED_SOURCE_FILE_NAMES", exception.Message, StringComparison.Ordinal);
        Assert.Contains("different tracked sources", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_parses_semicolon_terminated_swift_imports()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "SemicolonImportRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport Foundation; import PackageDescription\n" +
            "let seed = NSData(contentsOfFile: \"/tmp/seed\")!.base64EncodedString()\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", swiftSettings: [.define(\"SEED\", to: seed)])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("imports 'Foundation'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_custom_xcodebuild_executable()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CustomXcodeBuildRepo");
        var project = scope.CreateDirectory(Path.Combine("CustomXcodeBuildRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(
                "\"AppleApps\": {",
                "\"AppleApps\": { \"XcodeBuildExecutable\": \"/tmp/fake-xcodebuild\","));
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/usr/bin/xcodebuild", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not trusted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("XcrunExecutable", "/usr/bin/xcrun")]
    [InlineData("DittoExecutable", "/usr/bin/ditto")]
    [InlineData("SpctlExecutable", "/usr/sbin/spctl")]
    public void ResolveExactAppleSourceCommit_rejects_custom_notarization_tool_executable(
        string propertyName,
        string trustedPath)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CustomAppleToolRepo");
        var project = scope.CreateDirectory(Path.Combine("CustomAppleToolRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(
                "\"AppleApps\": {",
                $"\"AppleApps\": {{ \"DirectDistribution\": {{ \"{propertyName}\": \"/tmp/hostile-tool\" }},"));
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(trustedPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("not trusted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_standard_library_execution_in_swift_manifest()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "RandomManifestRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let seed = String(Int.random(in: 0...999999))\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", swiftSettings: [.define(\"SEED\", to: seed)])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("non-declarative manifest call", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("String", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_host_reference_in_unclassified_build_setting()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("HostBundleIdentifierRepo");
        var project = scope.CreateDirectory(Path.Combine("HostBundleIdentifierRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { PRODUCT_BUNDLE_IDENTIFIER = com.example.$(USER); }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("PRODUCT_BUNDLE_IDENTIFIER", exception.Message, StringComparison.Ordinal);
        Assert.Contains("$(USER)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_escaping_source_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "EscapingIncludeRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\")\n");
        var sources = scope.CreateDirectory(Path.Combine("EscapingIncludeRepo", "Packages", "Shared", "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources, "Injected.c"), "#include \"../../../../../../tmp/injected.h\"\nint value = 1;\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("injected.h", exception.Message, StringComparison.Ordinal);
    }

    private static string ReadFixtureHead(string repositoryRoot)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to read fixture HEAD.");
        var sha = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git rev-parse HEAD failed: {error}");
        return sha;
    }
}

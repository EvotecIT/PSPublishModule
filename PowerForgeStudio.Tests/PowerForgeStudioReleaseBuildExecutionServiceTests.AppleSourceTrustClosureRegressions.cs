using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_backticked_unpinned_swift_package_url_label()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "BacktickedPackageUrlRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let package = Package(name: \"Shared\", dependencies: [.package(`url`: \"https://example.invalid/Mutable.git\", branch: \"main\")])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Mutable.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_does_not_treat_nested_revision_argument_as_exact_dependency_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "NestedRevisionArgumentRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "func choose(revision: String) -> Package.Dependency.Requirement { .branch(\"main\") }\n" +
            "let package = Package(name: \"Shared\", dependencies: [.package(url: \"https://example.invalid/Mutable.git\", branch: choose(revision: \"0123456789abcdef0123456789abcdef01234567\"))])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Mutable.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_executable_override_with_multiple_xcconfig_conditions()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ConditionalXcconfigOverrideRepo",
            "SWIFT_EXEC[sdk=macosx*][arch=arm64] = /tmp/custom-swiftc\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("SWIFT_EXEC", exception.Message, StringComparison.Ordinal);
        Assert.Contains("executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_version_specific_swift_package_manifests()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "VersionSpecificManifestRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 5.9\nimport PackageDescription\nlet package = Package(name: \"Shared\")");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package@swift-6.0.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", swiftSettings: [.unsafeFlags([\"-load-plugin-executable\", \"/tmp/injected\"])])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unsafeFlags", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_synchronized_build_file_exception_overrides()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SynchronizedBuildFileExceptionRepo");
        var project = scope.CreateDirectory(Path.Combine("SynchronizedBuildFileExceptionRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXFileSystemSynchronizedBuildFileExceptionSet; additionalCompilerFlagsByRelativePath = { App.swift = \"-fplugin=/tmp/injected.dylib\"; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("exception set", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compiler", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_custom_sdkroot_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CustomSdkRootRepo");
        var project = scope.CreateDirectory(Path.Combine("CustomSdkRootRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { SDKROOT = /tmp/Fake.sdk; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("SDKROOT", exception.Message, StringComparison.Ordinal);
        Assert.Contains("custom SDK", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("$(BUILT_PRODUCTS_DIR)/Injected.a")]
    [InlineData("$(CONFIGURATION_BUILD_DIR)/Injected.a")]
    [InlineData("$(TARGET_BUILD_DIR)/Injected.a")]
    public void ResolveExactAppleSourceCommit_rejects_unowned_build_output_inputs(string input)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnownedBuildOutputRepo");
        var project = scope.CreateDirectory(Path.Combine("UnownedBuildOutputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCBuildConfiguration; buildSettings = {{ OTHER_LDFLAGS = -force_load {input}; }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unowned build output", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_recursively_validates_nested_local_package_manifest()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "NestedLocalPackageRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\", dependencies: [.package(path: \"../Nested\")])");
        var nestedRoot = scope.CreateDirectory(Path.Combine("NestedLocalPackageRepo", "Packages", "Nested"));
        File.WriteAllText(
            Path.Combine(nestedRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Nested\", targets: [.target(name: \"Nested\", linkerSettings: [.unsafeFlags([\"-L/tmp\"])])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unsafeFlags", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Nested", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_allows_missing_optional_xcconfig_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "OptionalXcconfigIncludeRepo",
            "#include? \"LocalOverrides.xcconfig\"\nSDKROOT = macosx\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_quoted_pbx_shell_phase_identifier()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("QuotedPbxIdentifierRepo");
        var project = scope.CreateDirectory(Path.Combine("QuotedPbxIdentifierRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "\"000000000000000000000001\" = { isa = PBXShellScriptBuildPhase; shellScript = \"date > generated.txt\"; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("shell-script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_input_hidden_in_nested_response_file()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("NestedResponseFileRepo");
        var project = scope.CreateDirectory(Path.Combine("NestedResponseFileRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { OTHER_CFLAGS = @Flags.rsp; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Flags.rsp"), "@Nested.rsp");
        File.WriteAllText(Path.Combine(repositoryRoot, "Nested.rsp"), "-fplugin=/tmp/injected.dylib");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("OTHER_CFLAGS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp/injected.dylib", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_tracked_safe_compiler_response_file()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SafeResponseFileRepo");
        var project = scope.CreateDirectory(Path.Combine("SafeResponseFileRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { OTHER_CFLAGS = @Flags.rsp; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Flags.rsp"), "-DRELEASE_BUILD=1");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_nonargument_path_labels_in_swift_text()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "SwiftPathTextRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let help = \"use path: foo\"\nfunc explain(path: String) -> String { path }\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\")])");
        var sources = scope.CreateDirectory(Path.Combine("SwiftPathTextRepo", "Packages", "Shared", "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources, "Shared.swift"), "public struct Shared {}");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    private static (string RepositoryRoot, string ProjectRoot, string PackageRoot) CreateLocalPackageFixture(
        TemporaryDirectoryScope scope,
        string repositoryName)
    {
        var repositoryRoot = scope.CreateDirectory(repositoryName);
        var projectRoot = scope.CreateDirectory(Path.Combine(repositoryName, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(projectRoot, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var packageRoot = scope.CreateDirectory(Path.Combine(repositoryName, "Packages", "Shared"));
        return (repositoryRoot, projectRoot, packageRoot);
    }

    private static (string RepositoryRoot, string ConfigPath) CreateXcconfigFixture(
        TemporaryDirectoryScope scope,
        string repositoryName,
        string xcconfig)
    {
        var repositoryRoot = scope.CreateDirectory(repositoryName);
        var project = scope.CreateDirectory(Path.Combine(repositoryName, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXFileReference; path = Config.xcconfig; sourceTree = SOURCE_ROOT; }; " +
            "000000000000000000000002 = { isa = XCBuildConfiguration; baseConfigurationReference = 000000000000000000000001; buildSettings = {}; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Config.xcconfig"), xcconfig);
        return (repositoryRoot, WriteAppleReleaseConfig(repositoryRoot, projectRoot: "."));
    }
}

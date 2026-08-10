using PowerForge;
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
            "let package = Package(name: \"Shared\", dependencies: [.package(url: \"https://example.invalid/Mutable.git\", branch: .branch(revision: \"0123456789abcdef0123456789abcdef01234567\"))])");
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
    public void ResolveExactAppleSourceCommit_rejects_quoted_pbx_shell_phase_property()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("QuotedPbxPropertyRepo");
        var project = scope.CreateDirectory(Path.Combine("QuotedPbxPropertyRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { \"isa\" = PBXShellScriptBuildPhase; shellScript = \"date > generated.txt\"; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("shell-script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_quoted_pbx_build_settings_property()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("QuotedBuildSettingsRepo");
        var project = scope.CreateDirectory(Path.Combine("QuotedBuildSettingsRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; \"buildSettings\" = { SWIFT_EXEC = /tmp/custom-swiftc; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("SWIFT_EXEC", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_dynamic_remote_binary_target()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "DynamicBinaryTargetRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let binaryUrl = \"https://example.invalid/Tool.zip\"\n" +
            "let package = Package(name: \"Shared\", targets: [.binaryTarget(name: \"Tool\", url: binaryUrl, checksum: \"" + new string('a', 64) + "\")])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("binary target", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("literal URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_literal_checksum_bound_remote_binary_target()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "ChecksumBoundBinaryTargetRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let package = Package(name: \"Shared\", targets: [.binaryTarget(name: \"Tool\", url: \"https://example.invalid/Tool.zip\", checksum: \"" + new string('a', 64) + "\")])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
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

    [Theory]
    [InlineData("-DRELEASE_SEED=$(CI_PIPELINE_ID)")]
    [InlineData("-UFEATURE_$(CONFIGURATION)")]
    [InlineData("-Xcc=-DHOST=${BUILD_NUMBER}")]
    public void ResolveExactAppleSourceCommit_rejects_build_setting_references_in_preprocessor_flags(string flags)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DynamicPreprocessorFlagRepo");
        var project = scope.CreateDirectory(Path.Combine("DynamicPreprocessorFlagRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCBuildConfiguration; buildSettings = {{ OTHER_CFLAGS = \"{flags}\"; }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("preprocessor", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build-setting reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("import Foundation\nlet enabled = ProcessInfo.processInfo.environment[\"CI\"] != nil")]
    [InlineData("@preconcurrency import Darwin\nlet enabled = time(nil) > 0")]
    [InlineData("let enabled = CommandLine.arguments.contains(\"--ci\")")]
    [InlineData("let enabled = true\nif enabled { print(\"host branch\") }")]
    [InlineData("#if os(macOS)\nlet enabled = true\n#else\nlet enabled = false\n#endif")]
    public void ResolveExactAppleSourceCommit_rejects_host_dependent_or_imperative_package_manifests(string executableSyntax)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "HostDependentManifestRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" + executableSyntax +
            "\nlet package = Package(name: \"Shared\")");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact-source", exception.Message.Replace("exact source", "exact-source", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_nonargument_path_labels_in_swift_text()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "SwiftPathTextRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let help = \"use path: foo\"\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\")])");
        var sources = scope.CreateDirectory(Path.Combine("SwiftPathTextRepo", "Packages", "Shared", "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources, "Shared.swift"), "public struct Shared {}");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_per_file_compiler_flags()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("PerFileCompilerFlagsRepo");
        var project = scope.CreateDirectory(Path.Combine("PerFileCompilerFlagsRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; settings = { COMPILER_FLAGS = \"-fplugin=/tmp/injected.dylib\"; }; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = App.swift; sourceTree = SOURCE_ROOT; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "App.swift"), "struct App {}");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("COMPILER_FLAGS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp/injected.dylib", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GCC_PREPROCESSOR_DEFINITIONS", "RELEASE_SEED=$(HOME)")]
    [InlineData("SWIFT_ACTIVE_COMPILATION_CONDITIONS", "$(CI_FEATURE)")]
    public void ResolveExactAppleSourceCommit_rejects_dynamic_dedicated_definition_settings(string key, string value)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DedicatedDefinitionsRepo" + Guid.NewGuid().ToString("N"));
        var project = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Sample.xcodeproj")).FullName;
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCBuildConfiguration; buildSettings = {{ {key} = \"{value}\"; }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.Contains("build-setting reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_entry_inside_linker_file_list()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LinkerFileListRepo");
        var project = scope.CreateDirectory(Path.Combine("LinkerFileListRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { OTHER_LDFLAGS = \"-filelist Inputs.xcfilelist\"; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Inputs.xcfilelist"), "/tmp/injected.a\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("OTHER_LDFLAGS", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp/injected.a", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@_silgen_name(\"getpid\") func hostValue() -> Int32")]
    [InlineData("let enabled = true ? [] : [.define(\"HOST\")]")]
    public void ResolveExactAppleSourceCommit_rejects_native_or_expression_manifest_execution(string executableSyntax)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "ExecutableManifestRepo" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" + executableSyntax +
            "\nlet package = Package(name: \"Shared\")");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("executable manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capture_rejects_executable_behavior_in_exact_remote_package_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var remoteRoot = scope.CreateDirectory("RemoteUnsafePackage");
        RunGit(remoteRoot, "init", "--quiet");
        File.WriteAllText(Path.Combine(remoteRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Remote\", targets: [.target(name: \"Remote\", swiftSettings: [.unsafeFlags([\"-I/tmp\"])])])");
        var remoteRevision = CommitRepository(remoteRoot);

        var repositoryRoot = scope.CreateDirectory("RemotePackageConsumer");
        var project = scope.CreateDirectory(Path.Combine("RemotePackageConsumer", "Sample.xcodeproj"));
        const string remoteUrl = "https://example.invalid/RemoteUnsafePackage.git";
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCRemoteSwiftPackageReference; repositoryURL = \"{remoteUrl}\"; requirement = {{ kind = revision; revision = {remoteRevision}; }}; }};");
        WriteTrackedPackageResolutionLock(repositoryRoot, remoteUrl, remoteRevision);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        var service = new AppleReleaseSourceTrustService(
            remotePackageCheckoutResolver: (_, _) => remoteRoot);

        var exception = Assert.Throws<InvalidOperationException>(() => service.Capture(repositoryRoot, configPath));

        Assert.Contains("unsafeFlags", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_accepts_declarative_exact_remote_package_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var remoteRoot = scope.CreateDirectory("RemoteSafePackage");
        RunGit(remoteRoot, "init", "--quiet");
        File.WriteAllText(Path.Combine(remoteRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let package = Package(name: \"Remote\", targets: [" +
            ".target(name: \"Remote\", dependencies: [.target(name: \"HostFallback\", condition: .when(platforms: [.linux, .windows]))]), " +
            ".systemLibrary(name: \"HostFallback\", pkgConfig: \"host-fallback\", providers: [" +
            ".apt([\"host-fallback-dev\"]), .brew([\"host-fallback\"]), .yum([\"host-fallback-devel\"])])])");
        var sources = scope.CreateDirectory(Path.Combine("RemoteSafePackage", "Sources", "Remote"));
        File.WriteAllText(Path.Combine(sources, "Remote.swift"), "public struct Remote {}");
        var inactiveSystemLibrary = scope.CreateDirectory(Path.Combine("RemoteSafePackage", "Sources", "HostFallback"));
        File.WriteAllText(Path.Combine(inactiveSystemLibrary, "module.modulemap"), "module HostFallback [system] { link \"host-fallback\" export * }");
        File.WriteAllText(Path.Combine(inactiveSystemLibrary, "shim.h"), "#include <host-fallback.h>\n");
        var remoteRevision = CommitRepository(remoteRoot);

        var repositoryRoot = scope.CreateDirectory("SafeRemotePackageConsumer");
        var project = scope.CreateDirectory(Path.Combine("SafeRemotePackageConsumer", "Sample.xcodeproj"));
        const string remoteUrl = "https://example.invalid/RemoteSafePackage.git";
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = XCRemoteSwiftPackageReference; repositoryURL = \"{remoteUrl}\"; requirement = {{ kind = revision; revision = {remoteRevision}; }}; }};");
        WriteTrackedPackageResolutionLock(repositoryRoot, remoteUrl, remoteRevision);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);
        var service = new AppleReleaseSourceTrustService(
            remotePackageCheckoutResolver: (_, _) => remoteRoot);

        var actual = service.ResolveExactCommit(repositoryRoot, configPath);

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

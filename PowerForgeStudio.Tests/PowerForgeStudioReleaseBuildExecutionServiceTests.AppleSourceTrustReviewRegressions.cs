using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_commented_decoy_isa_before_shell_phase()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CommentedPbxIsaRepo");
        var project = scope.CreateDirectory(Path.Combine("CommentedPbxIsaRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { /* isa = PBXGroup; */ isa = PBXShellScriptBuildPhase; shellScript = \"date > generated.txt\"; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("shell-script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_same_line_shell_phase_object()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CompactPbxObjectRepo");
        var project = scope.CreateDirectory(Path.Combine("CompactPbxObjectRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """000000000000000000000001 = { isa = PBXGroup; }; 000000000000000000000002 = { isa = PBXShellScriptBuildPhase; shellScript = "date > generated.txt"; };""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("shell-script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SWIFT_EXEC", "/tmp/custom-swiftc")]
    [InlineData("CC", "/tmp/custom-clang")]
    [InlineData("LD", "custom-linker")]
    public void ResolveExactAppleSourceCommit_rejects_compiler_and_build_tool_overrides(
        string setting,
        string value)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildToolOverrideRepo");
        var project = scope.CreateDirectory(Path.Combine("BuildToolOverrideRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $$"""
            000000000000000000000001 = {
                isa = XCBuildConfiguration;
                buildSettings = { PRODUCT_NAME = Sample; {{setting}} = {{value}}; };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_legacy_external_build_target()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LegacyTargetRepo");
        var project = scope.CreateDirectory(Path.Combine("LegacyTargetRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXLegacyTarget; buildToolPath = /tmp/custom-build; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("legacy target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_missing_relative_build_file_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MissingBuildFileRepo");
        var project = scope.CreateDirectory(Path.Combine("MissingBuildFileRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            """000000000000000000000002 = { isa = PBXFileReference; path = Injected.swift; sourceTree = "<group>"; };""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Injected.swift", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_escaped_unpinned_swift_package_dependency()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EscapedPackageDependencyRepo");
        var project = scope.CreateDirectory(Path.Combine("EscapedPackageDependencyRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("EscapedPackageDependencyRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [Package.Dependency.`package`(url: "https://example.invalid/Shared.git", branch: "main")],
                targets: [.target(name: "Shared")])
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Shared.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_indirect_swift_package_dependency_factory()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("IndirectPackageDependencyRepo");
        var project = scope.CreateDirectory(Path.Combine("IndirectPackageDependencyRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("IndirectPackageDependencyRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let factory: (String, Package.Dependency.Requirement) -> Package.Dependency = Package.Dependency.package
            let dependency = factory("https://example.invalid/Shared.git", .branch("main"))
            let package = Package(
                name: "Shared",
                dependencies: [dependency],
                targets: [.target(name: "Shared")])
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("indirectly", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_commented_fake_remote_package_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CommentedPackageRevisionRepo");
        var project = scope.CreateDirectory(Path.Combine("CommentedPackageRevisionRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/Shared.git";
                requirement = {
                    /* kind = revision; revision = 0123456789abcdef0123456789abcdef01234567; */
                    kind = branch;
                    branch = main;
                };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("tracked Package.resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_fake_revision_inside_nested_swift_comment()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("NestedSwiftCommentRepo");
        var project = scope.CreateDirectory(Path.Combine("NestedSwiftCommentRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("NestedSwiftCommentRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [
                    .package(
                        url: "https://example.invalid/Shared.git",
                        /* outer /* inner */ revision: "0123456789abcdef0123456789abcdef01234567" */
                        branch: "main")
                ],
                targets: [.target(name: "Shared")])
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Shared.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_executable_swift_macro_target()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SwiftMacroTargetRepo");
        var project = scope.CreateDirectory(Path.Combine("SwiftMacroTargetRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("SwiftMacroTargetRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            import CompilerPluginSupport
            let package = Package(
                name: "Shared",
                targets: [
                    .macro(name: "GeneratedFeature", dependencies: [])
                ])
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("macro", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("executable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_build_setting_that_escapes_sdk_root()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EscapedSdkRootRepo");
        var project = scope.CreateDirectory(Path.Combine("EscapedSdkRootRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { OTHER_LDFLAGS = -force_load $(SDKROOT)/../../../../tmp/libInjected.a; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("escapes approved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SDKROOT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_reads_only_root_pbx_objects_dictionary()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("RootPbxObjectsRepo");
        var project = scope.CreateDirectory(Path.Combine("RootPbxObjectsRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            {
                classes = { objects = { 000000000000000000000001 = { isa = PBXGroup; }; }; };
                objects = {
                    000000000000000000000002 = { isa = PBXShellScriptBuildPhase; shellScript = "date > generated.txt"; };
                };
                rootObject = 000000000000000000000003;
            }
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("shell-script", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_executable_swift_string_interpolation()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SwiftInterpolationRepo");
        var project = scope.CreateDirectory(Path.Combine("SwiftInterpolationRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("SwiftInterpolationRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let hidden = "\(CSetting.unsafeFlags(["-include", "/tmp/injected.h"]))"
            let package = Package(name: "Shared", targets: [.target(name: "Shared")])
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("string interpolation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_decoy_package_lock_outside_effective_xcode_location()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DecoyPackageLockRepo");
        var project = scope.CreateDirectory(Path.Combine("DecoyPackageLockRepo", "Sample.xcodeproj"));
        const string dependencyUrl = "https://example.invalid/MutablePackage.git";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $$"""
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "{{dependencyUrl}}";
                requirement = { kind = branch; branch = main; };
            };
            """);
        var docs = scope.CreateDirectory(Path.Combine("DecoyPackageLockRepo", "docs"));
        File.WriteAllText(
            Path.Combine(docs, "Package.resolved"),
            $$"""{ "pins": [ { "location": "{{dependencyUrl}}", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tracked Package.resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_backticked_swift_path_argument()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EscapedSwiftPathRepo");
        var project = scope.CreateDirectory(Path.Combine("EscapedSwiftPathRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("EscapedSwiftPathRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", `path`: \"/tmp/injected\")])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("must resolve", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("--skip-worktree")]
    [InlineData("--assume-unchanged")]
    public void ResolveExactAppleSourceCommit_rejects_hidden_index_state_on_xcode_input(string indexFlag)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("HiddenIndexAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("HiddenIndexAppleInputRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        RunGit(repositoryRoot, "update-index", indexFlag, "Sample.xcodeproj/project.pbxproj");
        File.WriteAllText(projectFile, "// hidden replacement project");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("skip-worktree or assume-unchanged", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unlocked_remote_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("RemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("RemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/MutablePackage.git";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_lock_for_substring_package_identity()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SubstringPackageLockRepo");
        var project = scope.CreateDirectory(Path.Combine("SubstringPackageLockRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/foo.git";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "SubstringPackageLockRepo", "Sample.xcodeproj", "project.xcworkspace", "xcshareddata", "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            """{ "pins": [ { "identity": "foo-tools", "location": "https://example.invalid/foo-tools.git", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("https://example.invalid/foo.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unlocked_remote_dependency_in_local_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LocalRemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LocalRemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("LocalRemotePackageInputRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [
                    .package(url: "https://example.invalid/MutablePackage.git", from: "1.0.0")
                ]
            )
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unsafe_flags_in_local_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnsafeFlagsPackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                targets: [
                    .target(
                        name: "Shared",
                        cSettings: [.unsafeFlags(["-include", "/tmp/injected.h"])]
                    )
                ]
            )
            """);
        var sources = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Packages", "Shared", "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources, "shared.c"), "int shared(void) { return 1; }");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unsafeFlags", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("let rejected = CSetting.`unsafeFlags`([\"-include\", \"/tmp/injected.h\"])", "unsafeFlags")]
    [InlineData("let rejected = CSetting.unsafeFlags", "unsafeFlags")]
    [InlineData("let rejected = Target.`systemLibrary`", "systemLibrary")]
    public void ResolveExactAppleSourceCommit_rejects_any_executable_unsafe_manifest_identifier(
        string manifestSyntax,
        string expectedIdentifier)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnsafeManifestIdentifierRepo");
        var project = scope.CreateDirectory(Path.Combine("UnsafeManifestIdentifierRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("UnsafeManifestIdentifierRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\n{manifestSyntax}\nlet package = Package(name: \"Shared\")");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedIdentifier, exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_disallowed_manifest_tokens_in_comments_and_strings()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CommentedPackageSyntaxRepo");
        var project = scope.CreateDirectory(Path.Combine("CommentedPackageSyntaxRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("CommentedPackageSyntaxRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """"
            // swift-tools-version: 6.0
            import PackageDescription
            // Documentation example: .unsafeFlags(["-I/tmp"]) and .systemLibrary(name: "Host")
            let documentation = ".unsafeFlags( and .systemLibrary( are rejected when used as syntax"
            let rawDocumentation = #".plugin( and .macro( are rejected when used as syntax"#
            let escapedInterpolationDocumentation = "\\(literal documentation)"
            let rawInterpolationDocumentation = #"\(literal raw documentation)"#
            let multilineDocumentation = """
            Nested /* comment markers */ and .macro( remain inert inside a multiline string.
            """
            let package = Package(name: "Shared")
            """");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, resolved);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_local_system_library_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SystemLibraryPackageRepo");
        var project = scope.CreateDirectory(Path.Combine("SystemLibraryPackageRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("SystemLibraryPackageRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                targets: [.systemLibrary(name: "CLib", pkgConfig: "libfoo")]
            )
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("systemLibrary", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pkg-config", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("targets: [.plugin(name: \"Generator\", capability: .buildTool())]")]
    [InlineData("targets: [.target(name: \"App\", plugins: [.plugin(name: \"Generator\")])]")]
    public void ResolveExactAppleSourceCommit_rejects_local_swift_build_tool_plugins(string targets)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildToolPluginPackageRepo");
        var project = scope.CreateDirectory(Path.Combine("BuildToolPluginPackageRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("BuildToolPluginPackageRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\", {targets})");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("plugin", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_computed_local_package_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ComputedPackagePathRepo");
        var project = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet custom = \"Generated\"\nlet package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", path: custom)])");
        var generated = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Packages", "Shared", "Generated"));
        File.WriteAllText(Path.Combine(generated, "Injected.swift"), "struct Injected {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Packages/Shared/Generated/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("computed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_clean_smudge_filtered_worktree_bytes()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredAppleInputRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "*.pbxproj filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        File.Delete(projectFile);
        RunGit(repositoryRoot, "checkout", "--", "Sample.xcodeproj/project.pbxproj");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleSourceSnapshot_rejects_smudged_bytes_created_only_in_detached_checkout()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredSnapshotAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredSnapshotAppleRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "*.pbxproj filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);
        Assert.Equal("// committed project", File.ReadAllText(projectFile));

        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = repositoryRoot,
            Archive = true,
            SourceCommit = sourceCommit,
            RequireImmutableSourceSnapshot = true,
            ExactSourceConfigPath = configPath
        };
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = AppleReleaseSourceSnapshot.CreateIfRequired(plan);
        });

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_filtered_bytes_in_synchronized_tree()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredSynchronizedTreeRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredSynchronizedTreeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileSystemSynchronizedRootGroup;
                path = Sources;
                sourceTree = SOURCE_ROOT;
            };
            """);
        var sources = scope.CreateDirectory(Path.Combine("FilteredSynchronizedTreeRepo", "Sources"));
        var sourceFile = Path.Combine(sources, "Filtered.swift");
        File.WriteAllText(sourceFile, "struct committed {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "Sources/*.swift filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        File.Delete(sourceFile);
        RunGit(repositoryRoot, "checkout", "--", "Sources/Filtered.swift");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Filtered.swift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_locked_remote_package_when_source_cannot_be_inspected()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LockedLocalRemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LockedLocalRemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("LockedLocalRemotePackageInputRepo", "Packages", "Shared"));
        const string dependencyUrl = "https://example.invalid/MutablePackage.git";
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $$"""
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [
                    .package(url: "{{dependencyUrl}}", from: "1.0.0")
                ]
            )
            """);
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "LockedLocalRemotePackageInputRepo",
            "Sample.xcodeproj",
            "project.xcworkspace",
            "xcshareddata",
            "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            $$"""{ "pins": [ { "location": "{{dependencyUrl}}", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("fetch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dependencyUrl, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_workspace_locked_remote_package_when_source_cannot_be_inspected()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("WorkspacePackageLockRepo");
        var project = scope.CreateDirectory(Path.Combine("WorkspacePackageLockRepo", "Apps", "iOS", "App.xcodeproj"));
        const string dependencyUrl = "https://example.invalid/WorkspacePackage.git";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $$"""
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "{{dependencyUrl}}";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var workspace = scope.CreateDirectory(Path.Combine("WorkspacePackageLockRepo", "Main.xcworkspace"));
        File.WriteAllText(
            Path.Combine(workspace, "contents.xcworkspacedata"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Workspace version="1.0"><FileRef location="group:Apps/iOS/App.xcodeproj"/></Workspace>
            """);
        var schemeDirectory = scope.CreateDirectory(Path.Combine(
            "WorkspacePackageLockRepo", "Main.xcworkspace", "xcshareddata", "xcschemes"));
        File.WriteAllText(Path.Combine(schemeDirectory, "App.xcscheme"), "<Scheme/>");
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "WorkspacePackageLockRepo", "Main.xcworkspace", "xcshareddata", "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            $$"""{ "pins": [ { "location": "{{dependencyUrl}}", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = Path.Combine(repositoryRoot, "powerforge.release.json");
        File.WriteAllText(
            configPath,
            """
            {
              "AppleApps": {
                "ProjectRoot": ".",
                "Apps": [
                  {
                    "Name": "App",
                    "ProjectPath": "Main.xcworkspace",
                    "Scheme": "App"
                  }
                ]
              }
            }
            """);
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("fetch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dependencyUrl, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_git_replacement_refs()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ReplacementRefRepo");
        var project = scope.CreateDirectory(Path.Combine("ReplacementRefRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var originalHead = CommitRepository(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "replacement.txt"), "alternate source");
        RunGit(repositoryRoot, "add", "replacement.txt");
        RunGit(repositoryRoot, "commit", "--quiet", "-m", "Replacement source");
        RunGit(repositoryRoot, "replace", originalHead, "HEAD");
        RunGit(repositoryRoot, "reset", "--hard", originalHead);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("replacement refs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_sha256_repository_head()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("Sha256AppleSourceRepo");
        RunGit(repositoryRoot, "init", "--quiet", "--object-format=sha256");
        var project = scope.CreateDirectory(Path.Combine("Sha256AppleSourceRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// SHA-256 project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(64, sourceCommit.Length);
        Assert.Equal(sourceCommit, resolved);
    }

    private static string WriteAppleReleaseConfig(
        string repositoryRoot,
        string projectRoot,
        bool createSharedScheme = true)
    {
        var projectPath = Path.Combine(repositoryRoot, "Sample.xcodeproj");
        if (createSharedScheme && Directory.Exists(projectPath))
        {
            var schemes = Directory.CreateDirectory(Path.Combine(projectPath, "xcshareddata", "xcschemes"));
            File.WriteAllText(Path.Combine(schemes.FullName, "Sample.xcscheme"), "<Scheme/>");
        }
        var configPath = Path.Combine(repositoryRoot, "powerforge.release.json");
        File.WriteAllText(
            configPath,
            $$"""
            {
              "AppleApps": {
                "ProjectRoot": "{{projectRoot}}",
                "Apps": [
                  {
                    "Name": "Sample",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
        return configPath;
    }

    private static string CommitRepository(string repositoryRoot)
    {
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "user.name", "PowerForge Tests");
        RunGit(repositoryRoot, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(repositoryRoot, "add", ".");
        RunGit(repositoryRoot, "commit", "--quiet", "-m", "Apple source fixture");
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

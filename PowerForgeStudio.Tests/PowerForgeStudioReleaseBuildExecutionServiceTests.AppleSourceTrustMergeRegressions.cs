using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_remote_project_package_without_tracked_lock()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("RemotePackageWithoutLockRepo");
        var project = scope.CreateDirectory(Path.Combine("RemotePackageWithoutLockRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/Shared.git";
                requirement = { kind = revision; revision = 0123456789abcdef0123456789abcdef01234567; };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("tracked Package.resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same approved graph", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_host_reference_in_expanded_info_plist()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ExpandedInfoPlistRepo");
        var project = scope.CreateDirectory(Path.Combine("ExpandedInfoPlistRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_FILE = Info.plist; }; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Info.plist"),
            "<?xml version=\"1.0\"?><plist><dict><key>BuildHost</key><string>$(USER)</string></dict></plist>",
            System.Text.Encoding.Unicode);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_FILE contents", exception.Message, StringComparison.Ordinal);
        Assert.Contains("$(USER)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_deterministic_xcode_info_plist_references()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DeterministicInfoPlistRepo");
        var project = scope.CreateDirectory(Path.Combine("DeterministicInfoPlistRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_FILE = Info.plist; }; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Info.plist"),
            "<?xml version=\"1.0\"?><plist><dict>" +
            "<key>DevelopmentRegion</key><string>$(DEVELOPMENT_LANGUAGE)</string>" +
            "<key>Executable</key><string>$(EXECUTABLE_NAME)</string>" +
            "<key>Identifier</key><string>$(PRODUCT_BUNDLE_IDENTIFIER)</string>" +
            "<key>Name</key><string>$(PRODUCT_NAME)</string>" +
            "<key>Version</key><string>$(MARKETING_VERSION)</string>" +
            "<key>Build</key><string>$(CURRENT_PROJECT_VERSION)</string>" +
            "<key>Class</key><string>$(PRODUCT_MODULE_NAME).SceneDelegate</string>" +
            "</dict></plist>");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var commit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.NotEmpty(commit);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_binary_info_plist_before_substitution_validation()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BinaryInfoPlistRepo");
        var project = scope.CreateDirectory(Path.Combine("BinaryInfoPlistRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_FILE = Info.plist; }; };");
        File.WriteAllBytes(
            Path.Combine(repositoryRoot, "Info.plist"),
            Convert.FromBase64String("YnBsaXN0MDDRAQJZQnVpbGRIb3N0aADpACQAKABVAFMARQBSACkICxUAAAAAAAABAQAAAAAAAAADAAAAAAAAAAAAAAAAAAAAJg=="));
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("binary property-list", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("text property list", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_local_remote_dependency_without_tracked_lock_even_with_literal_revision()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "LocalRemotePackageWithoutLockRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let package = Package(name: \"Shared\", dependencies: [" +
            ".package(url: \"https://example.invalid/Remote.git\", revision: \"0123456789abcdef0123456789abcdef01234567\")])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("tracked Package.resolved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same approved graph", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-framework Injected")]
    [InlineData("-Wl,-weak_framework,Injected")]
    public void ResolveExactAppleSourceCommit_rejects_unbound_named_framework_linker_flags(string flags)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "NamedFrameworkFlagRepo" + Guid.NewGuid().ToString("N"),
            $"OTHER_LDFLAGS = {flags}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Named framework 'Injected'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_info_plist_prefix_header()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InfoPlistPrefixRepo");
        var project = scope.CreateDirectory(Path.Combine("InfoPlistPrefixRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_PREPROCESS = YES; INFOPLIST_PREFIX_HEADER = /tmp/Injected.h; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_PREFIX_HEADER", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Shader.metal")]
    [InlineData("Startup.S")]
    public void ResolveExactAppleSourceCommit_rejects_external_includes_in_all_preprocessed_sources(string sourceName)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryName = "PreprocessedSourceRepo" + Path.GetExtension(sourceName).Replace(".", string.Empty);
        var repositoryRoot = scope.CreateDirectory(repositoryName);
        var project = scope.CreateDirectory(Path.Combine(repositoryName, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = PBXBuildFile; fileRef = 000000000000000000000002; }}; " +
            $"000000000000000000000002 = {{ isa = PBXFileReference; path = {sourceName}; sourceTree = \"<group>\"; }};");
        File.WriteAllText(Path.Combine(repositoryRoot, sourceName), "#include \"/tmp/injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(sourceName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("absolute preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_line_spliced_external_source_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LineSplicedIncludeRepo");
        var project = scope.CreateDirectory(Path.Combine("LineSplicedIncludeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), "#inc\\\nlude \"/tmp/injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("absolute preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/tmp/injected.h", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_parses_xcode_reference_modifiers_before_host_classification()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildSettingModifierRepo");
        var project = scope.CreateDirectory(Path.Combine("BuildSettingModifierRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { PRODUCT_BUNDLE_IDENTIFIER = com.example.$(USER:rfc1034identifier); }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("$(USER:rfc1034identifier)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unapproved host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteTrackedPackageResolutionLock(string repositoryRoot, string url, string revision)
    {
        var project = Directory.EnumerateDirectories(repositoryRoot, "*.xcodeproj", SearchOption.AllDirectories).Single();
        var lockDirectory = Path.Combine(project, "project.xcworkspace", "xcshareddata", "swiftpm");
        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                pins = new[]
                {
                    new
                    {
                        identity = "remote-package",
                        kind = "remoteSourceControl",
                        location = url,
                        state = new { revision, version = "1.0.0" }
                    }
                },
                version = 3
            }));
    }
}

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
    public void ResolveExactAppleSourceCommit_rejects_absolute_clang_module_map_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ModuleMapInputRepo");
        var project = scope.CreateDirectory(Path.Combine("ModuleMapInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { MODULEMAP_FILE = Config.modulemap; }; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Config.modulemap"),
            "module Sample { private textual header \"/tmp/injected.h\" export * }");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("module map", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/tmp/injected.h", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_c_preprocessor_digraph_includes()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DigraphIncludeRepo");
        var project = scope.CreateDirectory(Path.Combine("DigraphIncludeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), "%:include \"/tmp/injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/injected.h", exception.Message, StringComparison.Ordinal);
        Assert.Contains("absolute preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Source.m", "const char *path = __FILE__;", "__FILE__")]
    [InlineData("Source.m", "const char *path = __BASE_FILE__;", "__BASE_FILE__")]
    [InlineData("Source.m", "const char *path = __builtin_FILE();", "__builtin_FILE")]
    [InlineData("Source.cpp", "auto location = std::source_location::current();", "source_location")]
    [InlineData("Source.m", "#define PATH_TOKEN __FI %:%: LE__", "__FILE__")]
    [InlineData("Source.swift", "let path = #filePath", "#filePath")]
    [InlineData("Source.swift", "let path = #file", "#file")]
    public void ResolveExactAppleSourceCommit_rejects_snapshot_path_compiler_identifiers(
        string sourceName,
        string source,
        string expectedIdentifier)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SnapshotPathIdentifierRepo" + sourceName.Length + expectedIdentifier.Length);
        var project = scope.CreateDirectory(Path.Combine(
            "SnapshotPathIdentifierRepo" + sourceName.Length + expectedIdentifier.Length,
            "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            $"000000000000000000000002 = {{ isa = PBXFileReference; path = {sourceName}; sourceTree = \"<group>\"; }}; " +
            "000000000000000000000003 = { isa = PBXSourcesBuildPhase; files = (000000000000000000000001,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (000000000000000000000003,); productType = \"com.apple.product-type.application\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, sourceName), source + "\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedIdentifier, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_allows_snapshot_path_literal_in_ui_test_only_source()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UiTestSnapshotPathLiteralRepo");
        var project = scope.CreateDirectory(Path.Combine("UiTestSnapshotPathLiteralRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = UiTests.swift; sourceTree = \"<group>\"; }; " +
            "000000000000000000000003 = { isa = PBXSourcesBuildPhase; files = (000000000000000000000001,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (000000000000000000000003,); productType = \"com.apple.product-type.bundle.ui-testing\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "UiTests.swift"), "let source = #filePath\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var sourceCommit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(ReadFixtureHead(repositoryRoot), sourceCommit);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_snapshot_path_literal_in_shipping_synchronized_source_root()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SynchronizedSnapshotPathLiteralRepo");
        var sourceRoot = scope.CreateDirectory(Path.Combine("SynchronizedSnapshotPathLiteralRepo", "Sources"));
        var project = scope.CreateDirectory(Path.Combine("SynchronizedSnapshotPathLiteralRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.swift; sourceTree = \"<group>\"; }; " +
            "000000000000000000000003 = { isa = PBXFileSystemSynchronizedRootGroup; path = Sources; sourceTree = \"<group>\"; children = (000000000000000000000002,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (); fileSystemSynchronizedGroups = (000000000000000000000003,); productType = \"com.apple.product-type.application\"; };");
        File.WriteAllText(Path.Combine(sourceRoot, "Source.swift"), "let source = #filePath\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("#filePath", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("__DATE__")]
    [InlineData("__TIME__")]
    [InlineData("__TIMESTAMP__")]
    [InlineData("__TI ## ME__")]
    public void ResolveExactAppleSourceCommit_rejects_nondeterministic_compiler_time_macros(string macro)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CompilerTimeMacroRepo" + macro.Length);
        var project = scope.CreateDirectory(Path.Combine("CompilerTimeMacroRepo" + macro.Length, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), $"const char *buildTime = {macro};\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("nondeterministic compiler macro", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_allows_time_macro_names_in_comments_and_literals()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CompilerTimeMacroLiteralRepo");
        var project = scope.CreateDirectory(Path.Combine("CompilerTimeMacroLiteralRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Source.m"),
            "// __DATE__ and __FILE__\nconst char *documentation = \"__TIME__, __TIMESTAMP__, __BASE_FILE__, and source_location\";\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var sourceCommit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(ReadFixtureHead(repositoryRoot), sourceCommit);
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

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_c_trigraph_preprocessor_directives()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("TrigraphIncludeRepo");
        var project = scope.CreateDirectory(Path.Combine("TrigraphIncludeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), "??=include \"/tmp/injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("trigraph", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("??=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_c23_embed_payloads()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("C23EmbedRepo");
        var project = scope.CreateDirectory(Path.Combine("C23EmbedRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Source.c"),
            "const unsigned char payload[] = {\n#embed \"/tmp/payload.bin\"\n};\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("C23 embed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".include")]
    [InlineData(".incbin")]
    public void ResolveExactAppleSourceCommit_rejects_absolute_assembler_inputs(string directive)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryName = "AssemblerInputRepo" + directive.Length;
        var repositoryRoot = scope.CreateDirectory(repositoryName);
        var project = scope.CreateDirectory(Path.Combine(repositoryName, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Startup.S; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Startup.S"), $"{directive} \"/tmp/injected.bin\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Assembler source input", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp/injected.bin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_absolute_input_in_nested_assembler_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("NestedAssemblerInputRepo");
        var project = scope.CreateDirectory(Path.Combine("NestedAssemblerInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Startup.S; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Startup.S"), ".include \"Nested.inc\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Nested.inc"), ".incbin \"/tmp/injected.bin\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Assembler source input", exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp/injected.bin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_host_reference_in_entitlements()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EntitlementsHostReferenceRepo");
        var project = scope.CreateDirectory(Path.Combine("EntitlementsHostReferenceRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { CODE_SIGN_ENTITLEMENTS = App.entitlements; }; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "App.entitlements"),
            "<?xml version=\"1.0\"?><plist><dict><key>application-identifier</key><string>$(USER).app</string></dict></plist>");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("CODE_SIGN_ENTITLEMENTS contents", exception.Message, StringComparison.Ordinal);
        Assert.Contains("$(USER)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_classifies_joined_prebuilt_module_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "PrebuiltModulePathRepo",
            "OTHER_CFLAGS = -fprebuilt-module-path=Modules\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Modules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-fsanitize-ignorelist=Ignorelist")]
    [InlineData("-fsanitize-blacklist=Ignorelist")]
    [InlineData("-fsanitize-system-ignorelist Ignorelist")]
    [InlineData("-fsanitize-coverage-allowlist=Ignorelist")]
    public void ResolveExactAppleSourceCommit_classifies_sanitizer_list_inputs(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SanitizerListRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Ignorelist", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_angled_source_include_with_unbound_search_roots()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AngledIncludeRepo");
        var project = scope.CreateDirectory(Path.Combine("AngledIncludeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), "#include <Injected/Host.h>\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("angled preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unbound compiler search roots", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_missing_quoted_source_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MissingQuotedIncludeRepo");
        var project = scope.CreateDirectory(Path.Combine("MissingQuotedIncludeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.m"), "#include \"Injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Quoted preprocessor include", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Injected.h", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("link framework \"Injected\"")]
    [InlineData("link \"Injected\"")]
    public void ResolveExactAppleSourceCommit_rejects_unbound_module_map_autolinks(string declaration)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryName = "ModuleMapAutolinkRepo" + declaration.Length;
        var repositoryRoot = scope.CreateDirectory(repositoryName);
        var project = scope.CreateDirectory(Path.Combine(repositoryName, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { MODULEMAP_FILE = Config.modulemap; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Config.modulemap"), $"module Sample {{ {declaration} export * }}");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unbound autolink", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Injected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_inline_assembler_file_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InlineAssemblerInputRepo");
        var project = scope.CreateDirectory(Path.Combine("InlineAssemblerInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.m; sourceTree = \"<group>\"; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Source.m"),
            "void load(void) { __asm__(\".incbin \\\"/tmp/payload.bin\\\"\"); }\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("inline", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute .incbin", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_computed_inline_assembler_text()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ComputedInlineAssemblerRepo");
        var project = scope.CreateDirectory(Path.Combine("ComputedInlineAssemblerRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.c"), "void load(const char *text) { asm(text); }\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("computed inline assembler", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-fprofile-list=Rules")]
    [InlineData("-fprofile-list Rules")]
    [InlineData("-fprofile-remapping-file=Mappings")]
    [InlineData("-fprofile-remapping-file Mappings")]
    public void ResolveExactAppleSourceCommit_classifies_profile_configuration_files(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ProfileInputRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("copy")]
    public void ResolveExactAppleSourceCommit_accepts_tracked_swift_package_resources(string factory)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(
            scope,
            "TrackedSwiftResourceRepo" + factory);
        var resources = Directory.CreateDirectory(Path.Combine(packageRoot, "Sources", "Shared", "Resources"));
        File.WriteAllText(Path.Combine(resources.FullName, "message.txt"), "approved");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            $"let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", resources: [.{factory}(\"Resources\")])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var commit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.NotEmpty(commit);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_missing_swift_package_resource()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "MissingSwiftResourceRepo");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", resources: [.process(\"Resources\")])])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("resource input was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_indirect_swift_package_resources()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(scope, "IndirectSwiftResourceRepo");
        var resources = Directory.CreateDirectory(Path.Combine(packageRoot, "Sources", "Shared", "Resources"));
        File.WriteAllText(Path.Combine(resources.FullName, "message.txt"), "approved");
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\n" +
            "let assets: [Resource] = [.process(\"Resources\")]\n" +
            "let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", resources: assets)])");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("indirect resource declaration", exception.Message, StringComparison.OrdinalIgnoreCase);
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

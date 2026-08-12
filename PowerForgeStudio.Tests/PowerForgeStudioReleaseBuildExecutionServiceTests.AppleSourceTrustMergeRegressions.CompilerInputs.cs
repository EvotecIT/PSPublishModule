using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
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

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unreachable_tracked_angled_header()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnreachableAngledHeaderRepo");
        var project = scope.CreateDirectory(Path.Combine("UnreachableAngledHeaderRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.c"), "#include <Injected.h>\n");
        var docs = Directory.CreateDirectory(Path.Combine(repositoryRoot, "docs"));
        File.WriteAllText(Path.Combine(docs.FullName, "Injected.h"), "#define VALUE 1\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unbound compiler search roots", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_assembler_file_input_after_label()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LabeledAssemblerInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LabeledAssemblerInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Payload.s; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Payload.s"), "payload: .incbin \"/tmp/payload.bin\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("absolute .incbin", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-profile-use=Rules.profdata")]
    [InlineData("-profile-use Rules.profdata")]
    [InlineData("-profile-sample-use=Rules.profdata")]
    [InlineData("-profile-sample-use Rules.profdata")]
    public void ResolveExactAppleSourceCommit_classifies_swift_profile_inputs(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftProfileInputRepo" + option.Length,
            $"OTHER_SWIFT_FLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_absolute_has_include_probe()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("HasIncludeProbeRepo");
        var project = scope.CreateDirectory(Path.Combine("HasIncludeProbeRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.c"), "#if __has_include(\"/tmp/Injected.h\")\nint injected;\n#endif\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("probes absolute preprocessor input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_preprocessed_plist_file_directive()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("PreprocessedPlistRepo");
        var project = scope.CreateDirectory(Path.Combine("PreprocessedPlistRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_PREPROCESS = YES; INFOPLIST_FILE = Info.plist; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), "#include \"/tmp/Injected.inc\"\n<plist><dict /></plist>\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("preprocessed INFOPLIST_FILE", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file-selecting", exception.Message, StringComparison.OrdinalIgnoreCase);
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

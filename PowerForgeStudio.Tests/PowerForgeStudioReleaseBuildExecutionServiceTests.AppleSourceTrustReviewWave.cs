using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("Payload.mm", null)]
    [InlineData("Payload.data", "sourcecode.cpp.cpp")]
    public void ResolveExactAppleSourceCommit_scans_cpp_imports_by_effective_language(
        string sourceName,
        string? explicitFileType)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EffectiveCppImportRepo" + sourceName.Length);
        var project = scope.CreateDirectory(Path.Combine(Path.GetFileName(repositoryRoot), "Sample.xcodeproj"));
        var fileType = string.IsNullOrWhiteSpace(explicitFileType) ? string.Empty : $"explicitFileType = {explicitFileType};";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            $"000000000000000000000002 = {{ isa = PBXFileReference; path = {sourceName}; {fileType} sourceTree = \"<group>\"; }}; " +
            "000000000000000000000003 = { isa = PBXSourcesBuildPhase; files = (000000000000000000000001,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (000000000000000000000003,); productType = \"com.apple.product-type.application\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, sourceName), "import Injected;\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("C++", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#pragma include_alias(\"Owned.h\", \"/tmp/Injected.h\")")]
    [InlineData("_Pragma(\"include_alias(\\\"Owned.h\\\", \\\"/tmp/Injected.h\\\")\")")]
    [InlineData("_Pragma\n(\"include_alias(\\\"Owned.h\\\", \\\"/tmp/Injected.h\\\")\")")]
    public void ResolveExactAppleSourceCommit_rejects_preprocessor_include_aliases(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "IncludeAliasRepo" + source.Length,
            "Source.m",
            source + "\n#include \"Owned.h\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Owned.h"), "// tracked\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("include_alias", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_scans_cpp_imports_across_newlines()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "MultilineCppImportRepo",
            "Source.cpp",
            "export\nimport\nInjected;\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("C++", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_scans_objective_c_imports_across_newlines()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "MultilineObjectiveCImportRepo",
            "Source.m",
            "@import\nInjected\n;\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Objective-C module", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_standard_metal_library_header()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "MetalStandardLibraryRepo",
            "Shader.metal",
            "#include <metal_stdlib>\nusing namespace metal;\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("EXPORTED_SYMBOLS_FILE")]
    [InlineData("UNEXPORTED_SYMBOLS_FILE")]
    [InlineData("ORDER_FILE")]
    public void ResolveExactAppleSourceCommit_attests_linker_input_file_build_settings(string setting)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "LinkerInputSettingRepo" + setting,
            $"{setting} = /tmp/Injected.list\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(setting, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("linkedLibrary", "Injected")]
    [InlineData("linkedFramework", "Injected")]
    public void ResolveExactAppleSourceCommit_rejects_unapproved_swift_package_link_inputs(
        string factory,
        string name)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(
            scope,
            "SwiftPackageLinkInputRepo" + factory);
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\n" +
            $"let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", linkerSettings: [.{factory}(\"{name}\")])])\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(factory, exception.Message, StringComparison.Ordinal);
        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("linkedLibrary", "z")]
    [InlineData("linkedFramework", "Foundation")]
    [InlineData("linkedFramework", "AuthenticationServices")]
    public void ResolveExactAppleSourceCommit_accepts_approved_swift_package_link_inputs(
        string factory,
        string name)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, _, packageRoot) = CreateLocalPackageFixture(
            scope,
            "ApprovedSwiftPackageLinkInputRepo" + factory);
        File.WriteAllText(
            Path.Combine(packageRoot, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\n" +
            $"let package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", linkerSettings: [.{factory}(\"{name}\")])])\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("-includeMissingRules")]
    [InlineData("-imacrosMissingRules")]
    public void ResolveExactAppleSourceCommit_classifies_joined_forced_include_inputs(string flag)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "JoinedForcedIncludeRepo" + flag.Length,
            $"OTHER_CFLAGS = {flag}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-ivfsoverlay Overlay.yaml")]
    [InlineData("-vfsoverlay=Overlay.yaml")]
    public void ResolveExactAppleSourceCommit_rejects_vfs_overlays(string flag)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "VfsOverlayRepo" + flag.Length,
            $"OTHER_CFLAGS = {flag}\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Overlay.yaml"), "{ 'version': 0, 'roots': [] }\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("VFS overlay", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact-source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_quoted_headers_through_tracked_search_roots()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("QuotedSearchRootRepo");
        var project = scope.CreateDirectory(Path.Combine("QuotedSearchRootRepo", "Sample.xcodeproj"));
        var sources = scope.CreateDirectory(Path.Combine("QuotedSearchRootRepo", "Sources"));
        var headers = scope.CreateDirectory(Path.Combine("QuotedSearchRootRepo", "Headers"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Source.m; sourceTree = SOURCE_ROOT; }; " +
            "000000000000000000000003 = { isa = XCBuildConfiguration; buildSettings = { HEADER_SEARCH_PATHS = Headers; }; };");
        File.WriteAllText(
            Path.Combine(sources, "Source.m"),
            "#include \"Foo.h\"\n#if __has_include(\"Foo.h\")\nint found;\n#endif\n");
        File.WriteAllText(Path.Combine(headers, "Foo.h"), "// tracked header\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("-access-notes-path MissingRules")]
    [InlineData("-access-notes-path=MissingRules")]
    public void ResolveExactAppleSourceCommit_attests_swift_access_note_inputs(string flag)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftAccessNotesRepo" + flag.Length,
            $"OTHER_SWIFT_FLAGS = {flag}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_attests_private_module_map_build_setting()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "PrivateModuleMapRepo",
            "MODULEMAP_PRIVATE_FILE = /tmp/Injected.modulemap\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("MODULEMAP_PRIVATE_FILE", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_metal_headers_through_metal_search_roots()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("MetalSearchRootRepo");
        var project = scope.CreateDirectory(Path.Combine("MetalSearchRootRepo", "Sample.xcodeproj"));
        var sources = scope.CreateDirectory(Path.Combine("MetalSearchRootRepo", "Sources"));
        var headers = scope.CreateDirectory(Path.Combine("MetalSearchRootRepo", "MetalHeaders"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Shader.metal; sourceTree = SOURCE_ROOT; }; " +
            "000000000000000000000003 = { isa = XCBuildConfiguration; buildSettings = { MTL_HEADER_SEARCH_PATHS = MetalHeaders; }; };");
        File.WriteAllText(Path.Combine(sources, "Shader.metal"), "#include \"Shared.metal\"\n");
        File.WriteAllText(Path.Combine(headers, "Shared.metal"), "// tracked Metal header\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("-foverride-record-layout=MissingLayout")]
    [InlineData("-foverride-record-layout MissingLayout")]
    public void ResolveExactAppleSourceCommit_attests_clang_record_layout_override_inputs(string flag)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "RecordLayoutOverrideRepo" + flag.Length,
            $"OTHER_CFLAGS = -Xclang {flag}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("MissingLayout", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#pragma comment(lib, \"Injected\")")]
    [InlineData("_Pragma(\"comment(lib, \\\"Injected\\\")\")")]
    [InlineData("__pragma(comment(lib, \"Injected\"))")]
    public void ResolveExactAppleSourceCommit_rejects_pragma_linked_libraries(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "PragmaLinkedLibraryRepo" + source.Length,
            "Source.m",
            source + "\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("comment(lib)", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unbound linker search root", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("const char *value = \"#pragma comment(lib, \\\"NotExecutable\\\")\";")]
    [InlineData("const char *value = \"_Pragma(\\\"comment(lib, \\\\\\\"NotExecutable\\\\\\\")\\\")\";")]
    [InlineData("const char *value = \"__pragma(comment(lib, \\\"NotExecutable\\\"))\";")]
    public void ResolveExactAppleSourceCommit_ignores_pragma_link_text_inside_literals(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "PragmaLinkedLibraryLiteralRepo" + source.Length,
            "Source.m",
            source + "\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_attests_static_libtool_file_lists()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "StaticLibtoolFileListRepo",
            "OTHER_LIBTOOLFLAGS = -D -filelist /tmp/InjectedInputs\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("OTHER_LIBTOOLFLAGS", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("__has_embed(\"/tmp/payload.bin\")", "__has_embed")]
    [InlineData("__has_ ## embed(\"/tmp/payload.bin\")", "__has_embed")]
    [InlineData("__has_ ## include(\"/tmp/payload.bin\")", "__has_include")]
    public void ResolveExactAppleSourceCommit_rejects_unbound_preprocessor_file_probe(
        string probe,
        string expectedOperator)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("C23HasEmbedRepo");
        var project = scope.CreateDirectory(Path.Combine("C23HasEmbedRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(
            Path.Combine(repositoryRoot, "Source.c"),
            $"#if {probe}\nint payload = 1;\n#endif\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedOperator, exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_token_pasted_preprocessed_plist_probe()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("TokenPastedPlistProbeRepo");
        var project = scope.CreateDirectory(Path.Combine("TokenPastedPlistProbeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { INFOPLIST_PREPROCESS = YES; INFOPLIST_FILE = Info.plist; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), "#if __has_ ## embed(\"payload.bin\")\n<plist><dict /></plist>\n#endif\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("preprocessed INFOPLIST_FILE", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file-selecting", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GCC_PREPROCESSOR_DEFINITIONS = SEED=__TIME__")]
    [InlineData("OTHER_CFLAGS = -DSEED=__TIME__")]
    public void ResolveExactAppleSourceCommit_rejects_nondeterministic_macro_from_build_settings(string assignment)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "BuildSettingTimeMacroRepo" + assignment.Length,
            assignment + "\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("__TIME__", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nondeterministic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-load-plugin-executable Plugin#Macros", "Plugin")]
    [InlineData("-load-plugin-executable=Plugin#Macros", "Plugin")]
    [InlineData("-external-plugin-path Plugins#Server", "Plugins")]
    [InlineData("-external-plugin-path=Plugins#Server", "Plugins")]
    [InlineData("-load-plugin-library Plugin.dylib", "Plugin.dylib")]
    [InlineData("-load-plugin-library=Plugin.dylib", "Plugin.dylib")]
    public void ResolveExactAppleSourceCommit_classifies_swift_compiler_plugin_paths(string option, string expectedPath)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftCompilerPluginPathRepo" + option.Length,
            $"OTHER_SWIFT_FLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_classifies_swift_external_plugin_server_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftExternalPluginServerRepo",
            "OTHER_SWIFT_FLAGS = -external-plugin-path Plugins#Server\n");
        var plugins = Directory.CreateDirectory(Path.Combine(repositoryRoot, "Plugins"));
        File.WriteAllText(Path.Combine(plugins.FullName, "marker"), "tracked plugin search root");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Server", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--config Rules")]
    [InlineData("--config=Rules")]
    [InlineData("--config-user-dir Config")]
    [InlineData("--config-user-dir=Config")]
    [InlineData("--config-system-dir Config")]
    [InlineData("--config-system-dir=Config")]
    public void ResolveExactAppleSourceCommit_rejects_clang_configuration_file_controls(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ClangConfigurationControlRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Clang configuration-file option", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".byte 0; .incbin \"/tmp/payload.bin\"")]
    [InlineData(".byte 0; label: .include \"/tmp/payload.inc\"")]
    public void ResolveExactAppleSourceCommit_rejects_assembler_inputs_after_statement_separator(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AssemblerStatementSeparatorRepo" + source.Length);
        var project = scope.CreateDirectory(Path.Combine(Path.GetFileName(repositoryRoot), "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Source.s; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.s"), source + "\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("outside the exact-source graph", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_validates_effective_info_plist_preprocessor_flags()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InfoPlistPreprocessorFlagsRepo");
        var project = scope.CreateDirectory(Path.Combine("InfoPlistPreprocessorFlagsRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { " +
            "INFOPLIST_PREPROCESS = YES; INFOPLIST_FILE = Info.plist; " +
            "INFOPLIST_OTHER_PREPROCESSOR_FLAGS = \"-include /tmp/Injected.h\"; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), "<plist><dict /></plist>\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_OTHER_PREPROCESSOR_FLAGS", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absolute path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_inactive_info_plist_preprocessor_flags()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InactiveInfoPlistPreprocessorFlagsRepo");
        var project = scope.CreateDirectory(Path.Combine("InactiveInfoPlistPreprocessorFlagsRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCBuildConfiguration; buildSettings = { " +
            "INFOPLIST_PREPROCESS = NO; INFOPLIST_FILE = Info.plist; " +
            "INFOPLIST_OTHER_PREPROCESSOR_FLAGS = \"-include /tmp/Inactive.h\"; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), "<plist><dict /></plist>\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("-Wp,-I,External,-include,Injected.h")]
    [InlineData("-Wa,-I,External")]
    [InlineData("-Xpreprocessor -include -Xpreprocessor Injected.h")]
    [InlineData("-Xassembler -I -Xassembler External")]
    [InlineData("-Xclang -include -Xclang Injected.h")]
    public void ResolveExactAppleSourceCommit_classifies_forwarded_preprocessor_and_assembler_inputs(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ForwardedCompilerInputRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-frandomize-layout-seed-file Rules")]
    [InlineData("-frandomize-layout-seed-file=Rules")]
    public void ResolveExactAppleSourceCommit_classifies_randomized_layout_seed_file(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "RandomizedLayoutSeedRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-fbuild-session-file=Session", "Session")]
    [InlineData("-fcodegen-data-use Codegen.cgdata", "Codegen.cgdata")]
    [InlineData("-fmemory-profile-use=Memory.profdata", "Memory.profdata")]
    [InlineData("-iapinotes-path Notes.apinotes", "Notes.apinotes")]
    [InlineData("-ivfsstatcache Stats.cache", "Stats.cache")]
    [InlineData("--warning-suppression-mappings=Warnings.txt", "Warnings.txt")]
    [InlineData("-multi-lib-config Config.yaml", "Config.yaml")]
    [InlineData("--cuda-path Toolchain", "Toolchain")]
    public void ResolveExactAppleSourceCommit_classifies_other_clang_filesystem_controls(
        string option,
        string expectedPath)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ClangFilesystemControlRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_preserves_real_include_after_cpp_raw_string()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "CppRawStringIncludeRepo",
            "Source.cpp",
            "auto text = R\"tag(\" /*)tag\";\n#include \"/tmp/Injected.h\"\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("absolute preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_include_text_inside_cpp_raw_string()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "CppRawStringTextRepo",
            "Source.cpp",
            "auto text = R\"tag(\n#include \"/tmp/NotAnInclude.h\"\n)tag\";\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unbound_objective_c_module_import()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "ObjectiveCModuleImportRepo",
            "Source.m",
            "@import Injected;\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Objective-C module 'Injected'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_approved_apple_objective_c_module_import()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "AppleObjectiveCModuleImportRepo",
            "Source.m",
            "@import Foundation;\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Source.m", "#pragma clang module import Injected\n")]
    [InlineData("Source.cpp", "import Injected;\n")]
    [InlineData("Source.cpp", "import \"Injected.h\";\n")]
    public void ResolveExactAppleSourceCommit_rejects_other_unbound_language_module_imports(
        string sourceName,
        string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "LanguageModuleImportRepo" + sourceName.Length + source.Length,
            sourceName,
            source);
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("module", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (string RepositoryRoot, string ConfigPath) CreateTrackedSourceFixture(
        TemporaryDirectoryScope scope,
        string name,
        string sourceName,
        string source)
    {
        var repositoryRoot = scope.CreateDirectory(name);
        var project = scope.CreateDirectory(Path.Combine(name, "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            $"000000000000000000000002 = {{ isa = PBXFileReference; path = {sourceName}; sourceTree = \"<group>\"; }};");
        File.WriteAllText(Path.Combine(repositoryRoot, sourceName), source);
        return (repositoryRoot, WriteAppleReleaseConfig(repositoryRoot, projectRoot: "."));
    }
}

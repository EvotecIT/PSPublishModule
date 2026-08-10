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
}

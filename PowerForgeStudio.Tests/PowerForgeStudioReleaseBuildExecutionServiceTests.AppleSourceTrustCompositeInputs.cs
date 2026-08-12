using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("-load-resolved-plugin Lib#Exec#Macros", false)]
    [InlineData("-load-resolved-plugin=Lib#Exec#Macros", false)]
    [InlineData("-Xfrontend -load-resolved-plugin -Xfrontend Lib#Exec#Macros", false)]
    [InlineData("@Plugin.rsp", true)]
    public void ResolveExactAppleSourceCommit_attests_both_resolved_swift_plugin_paths(
        string option,
        bool responseFile)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ResolvedSwiftPluginRepo" + option.Length,
            $"OTHER_SWIFT_FLAGS = {option}\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Lib"), "tracked plugin library");
        if (responseFile)
            File.WriteAllText(Path.Combine(repositoryRoot, "Plugin.rsp"), "-load-resolved-plugin Lib#Exec#Macros\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Equal(Path.Combine(repositoryRoot, "Exec"), exception.FileName);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-dylib_file Foo:Current")]
    [InlineData("-dylib_file=Foo:Current")]
    [InlineData("-Wl,-dylib_file,Foo:Current")]
    public void ResolveExactAppleSourceCommit_attests_current_dylib_override_path(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "DylibOverrideRepo" + option.Length,
            $"OTHER_LDFLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Equal(Path.Combine(repositoryRoot, "Current"), exception.FileName);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-remap-file Source.c;Alias.c")]
    [InlineData("-remap-file=Source.c;Alias.c")]
    [InlineData("-Xclang -remap-file -Xclang Source.c;Alias.c")]
    [InlineData("@Remap.rsp")]
    public void ResolveExactAppleSourceCommit_attests_both_clang_remap_paths(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "ClangRemapRepo" + option.Length,
            $"OTHER_CFLAGS = {option}\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Source.c"), "int source;\n");
        if (option.StartsWith("@", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(repositoryRoot, "Remap.rsp"), "-remap-file Source.c;Alias.c\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Equal(Path.Combine(repositoryRoot, "Alias.c"), exception.FileName);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#define PAYLOAD .incbin \"/tmp/payload.bin\"\nPAYLOAD\n")]
    [InlineData("#define JOIN(a, b) a ## b\nJOIN(.inc, bin) \"/tmp/payload.bin\"\n")]
    [InlineData("#define DIRECTIVE(op, path) . op path\nDIRECTIVE(incbin, \"/tmp/payload.bin\")\n")]
    public void ResolveExactAppleSourceCommit_rejects_preprocessed_assembler_file_directive_macros(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "PreprocessedAssemblerMacroRepo" + source.Length,
            "Source.S",
            source);
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Preprocessed assembler", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GCC_PREPROCESSOR_DEFINITIONS = PAYLOAD=.incbin")]
    [InlineData("OTHER_CFLAGS = -DPAYLOAD=.incbin")]
    public void ResolveExactAppleSourceCommit_rejects_build_setting_assembler_file_directive_macros(string assignment)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "AssemblerBuildSettingMacroRepo" + assignment.Length,
            assignment + "\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("file-consuming assembler directive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_non_file_preprocessed_assembler_macros()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "SafePreprocessedAssemblerMacroRepo",
            "Source.S",
            "#define VALUE 1\n.long VALUE\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_non_file_include_named_preprocessor_definition()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SafeIncludeNamedDefinitionRepo",
            "GCC_PREPROCESSOR_DEFINITIONS = INCLUDE=1\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }
}

using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("-load-pass-plugin Rules.dylib")]
    [InlineData("-load-pass-plugin=Rules.dylib")]
    [InlineData("-Xfrontend -load-pass-plugin -Xfrontend Rules.dylib")]
    public void ResolveExactAppleSourceCommit_classifies_swift_pass_plugin_paths(string option)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftPassPluginRepo" + option.Length,
            $"OTHER_SWIFT_FLAGS = {option}\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules.dylib", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_classifies_swift_pass_plugin_path_from_response_file()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftPassPluginResponseRepo",
            "OTHER_SWIFT_FLAGS = @Swift.rsp\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Swift.rsp"), "-load-pass-plugin=Rules.dylib\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Rules.dylib", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_assembler_include_from_project_working_directory()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AssemblerWorkingDirectoryRepo");
        var project = scope.CreateDirectory(Path.Combine("AssemblerWorkingDirectoryRepo", "Sample.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Sources"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Payload.s; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Payload.s"), ".include \"Rules.inc\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Rules.inc"), ".byte 1\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Rules.inc"), ".incbin \"/tmp/untrusted.bin\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/untrusted.bin", exception.Message, StringComparison.Ordinal);
        Assert.Contains("outside the exact-source graph", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_assembler_include_from_validated_search_root()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AssemblerSearchRootRepo");
        var project = scope.CreateDirectory(Path.Combine("AssemblerSearchRootRepo", "Sample.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Sources"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "AssemblerIncludes"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Payload.s; sourceTree = \"<group>\"; }; " +
            "000000000000000000000003 = { isa = XCBuildConfiguration; buildSettings = { OTHER_CFLAGS = \"-I AssemblerIncludes\"; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Payload.s"), ".include \"Rules.inc\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "AssemblerIncludes", "Rules.inc"), ".byte 1\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var commit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Matches("^[A-Fa-f0-9]{40}$", commit);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_assembler_include_from_per_file_search_root_regardless_of_object_order()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("AssemblerPerFileSearchRootRepo");
        var project = scope.CreateDirectory(Path.Combine("AssemblerPerFileSearchRootRepo", "Sample.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Sources"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "AssemblerIncludes"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Payload.s; sourceTree = \"<group>\"; }; " +
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; settings = { COMPILER_FLAGS = \"-I AssemblerIncludes\"; }; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Payload.s"), ".include \"Rules.inc\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "AssemblerIncludes", "Rules.inc"), ".byte 1\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var commit = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Matches("^[A-Fa-f0-9]{40}$", commit);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_inline_assembler_include_from_project_working_directory()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("InlineAssemblerWorkingDirectoryRepo");
        var project = scope.CreateDirectory(Path.Combine("InlineAssemblerWorkingDirectoryRepo", "Sample.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Sources"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Sources/Payload.c; sourceTree = \"<group>\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Payload.c"), "void f(void) { __asm__(\".include \\\"Rules.inc\\\"\"); }\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sources", "Rules.inc"), ".byte 1\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Rules.inc"), ".incbin \"/tmp/untrusted.bin\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("/tmp/untrusted.bin", exception.Message, StringComparison.Ordinal);
        Assert.Contains("outside the exact-source graph", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

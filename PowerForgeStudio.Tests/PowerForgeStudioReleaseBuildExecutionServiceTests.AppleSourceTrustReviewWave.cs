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
}

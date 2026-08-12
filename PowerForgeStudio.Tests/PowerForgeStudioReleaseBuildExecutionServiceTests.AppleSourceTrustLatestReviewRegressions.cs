using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Theory]
    [InlineData("INFOPLIST_FILE = Info.plist;", "#include \"/tmp/Injected.h\"\n<plist><dict /></plist>\n")]
    [InlineData("INFOPLIST_FILE = Info.plist; INFOPLIST_OTHER_PREPROCESSOR_FLAGS = \"-include /tmp/Injected.h\";", "<plist><dict /></plist>\n")]
    public void ResolveExactAppleSourceCommit_tracks_info_plist_preprocessing_across_setting_layers(
        string projectSettings,
        string plistContents)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LayeredInfoPlistPreprocessRepo" + projectSettings.Length);
        var project = scope.CreateDirectory(Path.Combine(Path.GetFileName(repositoryRoot), "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(repositoryRoot, "Base.xcconfig"), "INFOPLIST_PREPROCESS = YES\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), plistContents);
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXFileReference; path = Base.xcconfig; sourceTree = \"<group>\"; }; " +
            "000000000000000000000002 = { isa = XCBuildConfiguration; baseConfigurationReference = 000000000000000000000001; " +
            $"buildSettings = {{ {projectSettings} }}; }};");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.ThrowsAny<Exception>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            exception.Message.Contains("preprocess", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_honors_project_override_that_disables_base_plist_preprocessing()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("DisabledLayeredInfoPlistPreprocessRepo");
        var project = scope.CreateDirectory(Path.Combine("DisabledLayeredInfoPlistPreprocessRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(repositoryRoot, "Base.xcconfig"), "INFOPLIST_PREPROCESS = YES\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Info.plist"), "#include \"/tmp/Inactive.h\"\n<plist><dict /></plist>\n");
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXFileReference; path = Base.xcconfig; sourceTree = \"<group>\"; }; " +
            "000000000000000000000002 = { isa = XCBuildConfiguration; baseConfigurationReference = 000000000000000000000001; " +
            "buildSettings = { INFOPLIST_PREPROCESS = NO; INFOPLIST_FILE = Info.plist; }; };");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("_Pragma(\"clang module import Injected\")")]
    [InlineData("_Pragma(\"\\x63lang module import Injected\")")]
    [InlineData("_Pragma(PRAGMA_PAYLOAD)")]
    public void ResolveExactAppleSourceCommit_rejects_unbound_or_computed_pragma_module_imports(string source)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "PragmaModuleImportRepo" + source.Length,
            "Source.m",
            source + "\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Pragma", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bound", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_approved_apple_pragma_module_import()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateTrackedSourceFixture(
            scope,
            "ApprovedPragmaModuleImportRepo",
            "Source.m",
            "_Pragma(\"clang module import Foundation\")\n");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("COMPILER_FLAGS = \"-x c\";")]
    [InlineData("", "sourcecode.c.c")]
    public void ResolveExactAppleSourceCommit_scans_shipping_sources_by_effective_compiler_language(
        string perFileSettings,
        string? explicitFileType = null)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("EffectiveSourceLanguageRepo" + perFileSettings.Length);
        var project = scope.CreateDirectory(Path.Combine(Path.GetFileName(repositoryRoot), "Sample.xcodeproj"));
        var settings = string.IsNullOrWhiteSpace(perFileSettings) ? string.Empty : $"settings = {{ {perFileSettings} }};";
        var fileType = string.IsNullOrWhiteSpace(explicitFileType) ? string.Empty : $"explicitFileType = {explicitFileType};";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $"000000000000000000000001 = {{ isa = PBXBuildFile; fileRef = 000000000000000000000002; {settings} }}; " +
            $"000000000000000000000002 = {{ isa = PBXFileReference; path = Payload.data; {fileType} sourceTree = \"<group>\"; }}; " +
            "000000000000000000000003 = { isa = PBXSourcesBuildPhase; files = (000000000000000000000001,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (000000000000000000000003,); productType = \"com.apple.product-type.application\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Payload.data"), "#include \"/tmp/Injected.h\"\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("absolute preprocessor include", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_global_compiler_language_override()
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "GlobalCompilerLanguageOverrideRepo",
            "OTHER_CFLAGS = -x c\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("language override", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source-owned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_shipping_source_with_unclassified_language()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnclassifiedShippingLanguageRepo");
        var project = scope.CreateDirectory(Path.Combine("UnclassifiedShippingLanguageRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = PBXBuildFile; fileRef = 000000000000000000000002; }; " +
            "000000000000000000000002 = { isa = PBXFileReference; path = Payload.data; sourceTree = \"<group>\"; }; " +
            "000000000000000000000003 = { isa = PBXSourcesBuildPhase; files = (000000000000000000000001,); }; " +
            "000000000000000000000004 = { isa = PBXNativeTarget; buildPhases = (000000000000000000000003,); productType = \"com.apple.product-type.application\"; };");
        File.WriteAllText(Path.Combine(repositoryRoot, "Payload.data"), "opaque source\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("compiler language", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-swift-module-file=Rules=Injected.swiftmodule", "Injected.swiftmodule")]
    [InlineData("-Xfrontend -swift-module-file=Rules=Injected.swiftmodule", "Injected.swiftmodule")]
    [InlineData("-swift-module-cross-import Rules Injected.swiftoverlay", "Injected.swiftoverlay")]
    [InlineData("-candidate-module-file Injected.swiftmodule", "Injected.swiftmodule")]
    [InlineData("-explicit-swift-module-map-file Injected.json", "Injected.json")]
    [InlineData("@Module.rsp", "Injected.swiftmodule")]
    public void ResolveExactAppleSourceCommit_attests_swift_module_injection_inputs(string option, string expectedPath)
    {
        using var scope = new TemporaryDirectoryScope();
        var (repositoryRoot, configPath) = CreateXcconfigFixture(
            scope,
            "SwiftModuleInjectionRepo" + option.Length,
            $"OTHER_SWIFT_FLAGS = {option}\n");
        if (option.StartsWith("@", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(repositoryRoot, "Module.rsp"), "-swift-module-file=Rules=Injected.swiftmodule\n");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Equal(Path.Combine(repositoryRoot, expectedPath), exception.FileName);
        Assert.Contains("missing exact-source input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

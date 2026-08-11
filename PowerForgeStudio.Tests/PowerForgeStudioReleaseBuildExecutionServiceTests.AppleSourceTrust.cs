using PowerForge;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    [Fact]
    public void AppleCheckpointRequest_UsesCapturedExactReleaseConfiguration()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CapturedConfigRepo");
        var project = scope.CreateDirectory(Path.Combine("CapturedConfigRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// tracked project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        var snapshot = new AppleReleaseSourceTrustService().Capture(repositoryRoot, configPath);
        File.WriteAllText(configPath, "{}");

        var request = ReleaseBuildExecutionService.CreateUnifiedReleaseBuildRequest(
            configPath,
            "PSPublishModule.dll",
            Path.Combine(repositoryRoot, "staging"),
            snapshot.ExactConfigurationContent);

        Assert.False(request.SkipAppleApps);
        Assert.True(request.CheckpointAppleApps);
        Assert.Equal(snapshot.ExactConfigurationContent, request.ExactConfigurationContent);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_tracked_project_inputs_and_ignored_user_state()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("TrackedAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("TrackedAppleRepo", "Sample.xcodeproj"));
        var userState = scope.CreateDirectory(Path.Combine(
            "TrackedAppleRepo",
            "Sample.xcodeproj",
            "xcuserdata",
            "developer.xcuserdatad"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "path = Sample.swift; sourceTree = SOURCE_ROOT;");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sample.swift"), "struct Sample {}");
        File.WriteAllText(Path.Combine(userState, "xcschememanagement.plist"), "local user state");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "**/xcuserdata/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_project_root_outside_repository()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ContainedAppleRepo");
        scope.CreateDirectory("OutsideAppleSource");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: "../OutsideAppleSource");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("ProjectRoot", exception.Message, StringComparison.Ordinal);
        Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_file_referenced_by_Xcode_project()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("IgnoredAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("IgnoredAppleInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "baseConfigurationReference = Sample.xcconfig; path = Sample.swift;");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sample.swift"), "struct Sample {}");
        File.WriteAllText(Path.Combine(repositoryRoot, "Sample.xcconfig"), "SWIFT_ACTIVE_COMPILATION_CONDITIONS = UNREVIEWED");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "*.xcconfig\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Ignored Apple build input", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Sample.xcconfig", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_compiled_source_even_without_explicit_reference()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("IgnoredSwiftRepo");
        var project = scope.CreateDirectory(Path.Combine("IgnoredSwiftRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// synchronized project fixture");
        File.WriteAllText(Path.Combine(repositoryRoot, "Generated.swift"), "struct Generated {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Generated.swift\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Generated.swift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_resource_in_synchronized_Xcode_group()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SynchronizedAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("SynchronizedAppleInputRepo", "Sample.xcodeproj"));
        var synchronizedSources = scope.CreateDirectory(Path.Combine("SynchronizedAppleInputRepo", "Parent", "AppSources"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXGroup;
                children = (
                    000000000000000000000002,
                );
                path = Parent;
                sourceTree = "<group>";
            };
            /* Begin PBXFileSystemSynchronizedRootGroup section */
                000000000000000000000002 = {
                    isa = PBXFileSystemSynchronizedRootGroup;
                    path = AppSources;
                    sourceTree = "<group>";
                };
            /* End PBXFileSystemSynchronizedRootGroup section */
            """);
        File.WriteAllText(Path.Combine(synchronizedSources, "RuntimeConfig.json"), "{ \"mode\": \"unreviewed\" }");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Parent/AppSources/RuntimeConfig.json\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("RuntimeConfig.json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_user_scheme_selected_for_release()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UserSchemeAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("UserSchemeAppleRepo", "Sample.xcodeproj"));
        var userSchemes = scope.CreateDirectory(Path.Combine(
            "UserSchemeAppleRepo",
            "Sample.xcodeproj",
            "xcuserdata",
            "developer.xcuserdatad",
            "xcschemes"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact project");
        File.WriteAllText(Path.Combine(userSchemes, "Sample.xcscheme"), "<Scheme/>");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "**/xcuserdata/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Ignored Apple build input", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sample.xcscheme", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_project_reference_outside_repository()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ExternalReferenceAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("ExternalReferenceAppleRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000000 = {
                isa = PBXBuildFile;
                fileRef = 000000000000000000000001;
            };
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = ../../OutsideSecrets/Injected.swift;
                sourceTree = SOURCE_ROOT;
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Xcode PBXFileReference input", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_source_tree_build_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ExternalTreeBuildInputRepo");
        var project = scope.CreateDirectory(Path.Combine("ExternalTreeBuildInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000000 = {
                isa = PBXBuildFile;
                fileRef = 000000000000000000000001;
            };
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = ../../../../tmp/Injected.swift;
                sourceTree = DEVELOPER_DIR;
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("external source tree", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Injected.swift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_owned_built_product_reference()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("OwnedBuiltProductRepo");
        var project = scope.CreateDirectory(Path.Combine("OwnedBuiltProductRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000000 = { isa = PBXBuildFile; fileRef = 000000000000000000000001; };
            000000000000000000000001 = { isa = PBXFileReference; path = SampleExtension.appex; sourceTree = BUILT_PRODUCTS_DIR; };
            000000000000000000000002 = {
                isa = PBXNativeTarget;
                productReference = 000000000000000000000001;
                productType = "com.apple.product-type.app-extension";
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_sdk_framework_build_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SdkFrameworkInputRepo");
        var project = scope.CreateDirectory(Path.Combine("SdkFrameworkInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000000 = { isa = PBXBuildFile; fileRef = 000000000000000000000001; };
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = System/Library/Frameworks/Foundation.framework;
                sourceTree = SDKROOT;
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, actual);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_recursively_validates_referenced_subprojects()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("NestedProjectAppleRepo");
        var outer = scope.CreateDirectory(Path.Combine("NestedProjectAppleRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(outer, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = Nested.xcodeproj;
                sourceTree = SOURCE_ROOT;
            };
            """);
        var nested = scope.CreateDirectory(Path.Combine("NestedProjectAppleRepo", "Nested.xcodeproj"));
        File.WriteAllText(
            Path.Combine(nested, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCBuildConfiguration;
                buildSettings = {
                    INFOPLIST_FILE = Secret/Nested-Info.plist;
                };
            };
            """);
        var secret = scope.CreateDirectory(Path.Combine("NestedProjectAppleRepo", "Secret"));
        File.WriteAllText(Path.Combine(secret, "Nested-Info.plist"), "unreviewed nested input");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Secret/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_FILE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_generated_project_metadata()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("GeneratedProjectAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("GeneratedProjectAppleRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// generated project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(
                "\"Scheme\": \"Sample\"",
                "\"Scheme\": \"Sample\", \"RegenerateProject\": true",
                StringComparison.Ordinal));
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Generate the project first", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_scheme_container_outside_repository()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ExternalSchemeContainerRepo");
        var project = scope.CreateDirectory(Path.Combine("ExternalSchemeContainerRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            Path.Combine(project, "xcshareddata", "xcschemes", "Sample.xcscheme"),
            """
            <Scheme>
              <BuildAction>
                <BuildActionEntries>
                  <BuildActionEntry>
                    <BuildableReference ReferencedContainer="container:../../Outside.xcodeproj" />
                  </BuildActionEntry>
                </BuildActionEntries>
              </BuildAction>
            </Scheme>
            """);
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("scheme referenced container", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_scheme_execution_actions()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SchemeActionAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("SchemeActionAppleRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            Path.Combine(project, "xcshareddata", "xcschemes", "Sample.xcscheme"),
            "<Scheme><BuildAction><PreActions><ExecutionAction /></PreActions></BuildAction></Scheme>");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("scheme actions", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_variable_based_project_input()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("VariableInputAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("VariableInputAppleRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = "$(SRCROOT)/Injected.swift";
                sourceTree = SOURCE_ROOT;
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Variable-based", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_resolves_nested_workspace_groups()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("NestedWorkspaceAppleRepo");
        var workspace = scope.CreateDirectory(Path.Combine("NestedWorkspaceAppleRepo", "Sample.xcworkspace"));
        var project = scope.CreateDirectory(Path.Combine("NestedWorkspaceAppleRepo", "Projects", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact nested project");
        File.WriteAllText(
            Path.Combine(workspace, "contents.xcworkspacedata"),
            "<Workspace><Group location=\"group:Projects\"><FileRef location=\"group:Sample.xcodeproj\" /></Group></Workspace>");
        var schemes = Directory.CreateDirectory(Path.Combine(workspace, "xcshareddata", "xcschemes"));
        File.WriteAllText(
            Path.Combine(schemes.FullName, "Sample.xcscheme"),
            "<Scheme><BuildAction><BuildableReference ReferencedContainer=\"container:Projects/Sample.xcodeproj\" /></BuildAction></Scheme>");
        var configPath = Path.Combine(repositoryRoot, "powerforge.release.json");
        File.WriteAllText(
            configPath,
            """
            {
              "AppleApps": {
                "ProjectRoot": ".",
                "Apps": [
                  {
                    "Name": "Sample",
                    "ProjectPath": "Sample.xcworkspace",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
        var expected = CommitRepository(repositoryRoot);

        var actual = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Apple_source_snapshot_allows_only_declared_generated_outputs_after_build()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("GeneratedArchiveOutputRepo");
        var project = scope.CreateDirectory(Path.Combine("GeneratedArchiveOutputRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact project");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Artifacts/\nbuild/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var expected = CommitRepository(repositoryRoot);
        var service = new AppleReleaseSourceTrustService();

        var snapshot = service.Capture(repositoryRoot, configPath);
        var archive = Directory.CreateDirectory(Path.Combine(
            repositoryRoot,
            "Artifacts",
            "Apple",
            "Archives",
            "iOS",
            "Sample.xcarchive"));
        File.WriteAllText(Path.Combine(archive.FullName, "Info.plist"), "generated archive");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "build", "powerforge", "apple"));
        File.WriteAllText(
            Path.Combine(repositoryRoot, "build", "powerforge", "apple", "release-plan.json"),
            "generated receipt");

        service.ValidateAfterBuild(repositoryRoot, configPath, snapshot);

        Assert.Equal(expected, snapshot.SourceCommit);
    }

    [Fact]
    public void Apple_source_snapshot_rejects_new_source_outside_declared_outputs()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ChangedAppleSourceRepo");
        var project = scope.CreateDirectory(Path.Combine("ChangedAppleSourceRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// exact project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        var service = new AppleReleaseSourceTrustService();
        var snapshot = service.Capture(repositoryRoot, configPath);
        File.WriteAllText(Path.Combine(repositoryRoot, "Injected.swift"), "struct Injected {}");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.ValidateAfterBuild(repositoryRoot, configPath, snapshot));

        Assert.Contains("Injected.swift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_file_valued_build_setting()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildSettingAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("BuildSettingAppleInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCBuildConfiguration;
                buildSettings = {
                    INFOPLIST_FILE = Secret/Info.plist;
                };
            };
            """);
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Secret"));
        File.WriteAllText(Path.Combine(repositoryRoot, "Secret", "Info.plist"), "unreviewed plist");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Secret/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_FILE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_untracked_descendant_of_folder_reference()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FolderReferenceAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("FolderReferenceAppleInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000000 = {
                isa = PBXBuildFile;
                fileRef = 000000000000000000000001;
            };
            000000000000000000000001 = {
                isa = PBXFileReference;
                lastKnownFileType = folder;
                path = Resources;
                sourceTree = SOURCE_ROOT;
            };
            """);
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Resources"));
        File.WriteAllText(Path.Combine(repositoryRoot, "Resources", "config.json"), "unreviewed resource");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Resources/config.json\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("config.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_xcconfig_include()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("XcconfigIncludeAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("XcconfigIncludeAppleInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = Config/Base.xcconfig;
                sourceTree = SOURCE_ROOT;
            };
            000000000000000000000002 = {
                isa = XCBuildConfiguration;
                baseConfigurationReference = 000000000000000000000001;
                buildSettings = {};
            };
            """);
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Config"));
        File.WriteAllText(Path.Combine(repositoryRoot, "Config", "Base.xcconfig"), "#include \"Secret.settings\"\n");
        File.WriteAllText(Path.Combine(repositoryRoot, "Config", "Secret.settings"), "SWIFT_ACTIVE_COMPILATION_CONDITIONS = UNREVIEWED\n");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Config/Secret.settings\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("xcconfig include", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_external_file_setting_from_xcconfig()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("XcconfigExternalInputRepo");
        var project = scope.CreateDirectory(Path.Combine("XcconfigExternalInputRepo", "Sample.xcodeproj"));
        var outside = scope.CreateDirectory("ExternalXcodePayload");
        var outsidePlist = Path.Combine(outside, "Info.plist");
        File.WriteAllText(outsidePlist, "external payload");
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileReference;
                path = Config.xcconfig;
                sourceTree = SOURCE_ROOT;
            };
            000000000000000000000002 = {
                isa = XCBuildConfiguration;
                baseConfigurationReference = 000000000000000000000001;
                buildSettings = {};
            };
            """);
        File.WriteAllText(Path.Combine(repositoryRoot, "Config.xcconfig"), $"INFOPLIST_FILE = {outsidePlist}\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("INFOPLIST_FILE", exception.Message, StringComparison.Ordinal);
        Assert.Contains("inside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("OTHER_CFLAGS", "-fplugin=/tmp/injected.dylib")]
    [InlineData("OTHER_CFLAGS", "-B/tmp/injected-tools")]
    [InlineData("OTHER_LDFLAGS", "-Wl,-force_load,/tmp/libInjected.a")]
    [InlineData("OTHER_SWIFT_FLAGS", "-Xcc -fplugin=/tmp/injected.dylib")]
    public void ResolveExactAppleSourceCommit_rejects_paths_hidden_in_compiler_option_tokens(
        string setting,
        string value)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FlagOptionPathRepo");
        var project = scope.CreateDirectory(Path.Combine("FlagOptionPathRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $$"""
            000000000000000000000001 = {
                isa = XCBuildConfiguration;
                buildSettings = {
                    {{setting}} = "{{value}}";
                };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("/tmp", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_local_swift_package_source()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LocalPackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LocalPackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("LocalPackageInputRepo", "Packages", "Shared"));
        File.WriteAllText(Path.Combine(package, "Package.swift"), "// swift-tools-version: 6.0");
        var sources = Directory.CreateDirectory(Path.Combine(package, "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources.FullName, "Generated.swift"), "struct Injected {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Packages/Shared/Sources/Shared/Generated.swift\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Generated.swift", exception.Message, StringComparison.Ordinal);
        Assert.Contains("tracked", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_ignored_source_inside_archive_root_outside_exact_artifact()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ArchiveRootSourceRepo");
        var project = scope.CreateDirectory(Path.Combine("ArchiveRootSourceRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// project");
        var generatedRoot = scope.CreateDirectory(Path.Combine("ArchiveRootSourceRepo", "Generated"));
        File.WriteAllText(Path.Combine(generatedRoot, "Secret.h"), "#define INJECTED 1");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Generated/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace(
                "\"ProjectRoot\": \".\",",
                "\"ProjectRoot\": \".\",\n    \"ArchiveRoot\": \"Generated\",",
                StringComparison.Ordinal));
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Generated/Secret.h", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Ignored Apple build input", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

}

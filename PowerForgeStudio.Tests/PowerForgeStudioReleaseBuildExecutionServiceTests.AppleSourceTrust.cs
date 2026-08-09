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
            000000000000000000000002 = { isa = PBXNativeTarget; productReference = 000000000000000000000001; };
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

    [Theory]
    [InlineData("--skip-worktree")]
    [InlineData("--assume-unchanged")]
    public void ResolveExactAppleSourceCommit_rejects_hidden_index_state_on_xcode_input(string indexFlag)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("HiddenIndexAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("HiddenIndexAppleInputRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        RunGit(repositoryRoot, "update-index", indexFlag, "Sample.xcodeproj/project.pbxproj");
        File.WriteAllText(projectFile, "// hidden replacement project");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("skip-worktree or assume-unchanged", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unlocked_remote_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("RemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("RemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/MutablePackage.git";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_lock_for_substring_package_identity()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SubstringPackageLockRepo");
        var project = scope.CreateDirectory(Path.Combine("SubstringPackageLockRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "https://example.invalid/foo.git";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "SubstringPackageLockRepo", "Sample.xcodeproj", "project.xcworkspace", "xcshareddata", "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            """{ "pins": [ { "identity": "foo-tools", "location": "https://example.invalid/foo-tools.git", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("https://example.invalid/foo.git", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unlocked_remote_dependency_in_local_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LocalRemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LocalRemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("LocalRemotePackageInputRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [
                    .package(url: "https://example.invalid/MutablePackage.git", from: "1.0.0")
                ]
            )
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("Package.resolved", exception.Message, StringComparison.Ordinal);
        Assert.Contains("exact", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_unsafe_flags_in_local_swift_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnsafeFlagsPackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                targets: [
                    .target(
                        name: "Shared",
                        cSettings: [.unsafeFlags(["-include", "/tmp/injected.h"])]
                    )
                ]
            )
            """);
        var sources = scope.CreateDirectory(Path.Combine("UnsafeFlagsPackageInputRepo", "Packages", "Shared", "Sources", "Shared"));
        File.WriteAllText(Path.Combine(sources, "shared.c"), "int shared(void) { return 1; }");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("unsafeFlags", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("let rejected = CSetting.`unsafeFlags`([\"-include\", \"/tmp/injected.h\"])", "unsafeFlags")]
    [InlineData("let rejected = CSetting.unsafeFlags", "unsafeFlags")]
    [InlineData("let rejected = Target.`systemLibrary`", "systemLibrary")]
    public void ResolveExactAppleSourceCommit_rejects_any_executable_unsafe_manifest_identifier(
        string manifestSyntax,
        string expectedIdentifier)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("UnsafeManifestIdentifierRepo");
        var project = scope.CreateDirectory(Path.Combine("UnsafeManifestIdentifierRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("UnsafeManifestIdentifierRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\n{manifestSyntax}\nlet package = Package(name: \"Shared\")");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains(expectedIdentifier, exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be proven", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_ignores_disallowed_manifest_tokens_in_comments_and_strings()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("CommentedPackageSyntaxRepo");
        var project = scope.CreateDirectory(Path.Combine("CommentedPackageSyntaxRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("CommentedPackageSyntaxRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            // Documentation example: .unsafeFlags(["-I/tmp"]) and .systemLibrary(name: "Host")
            let documentation = ".unsafeFlags( and .systemLibrary( are rejected when used as syntax"
            let package = Package(name: "Shared")
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, resolved);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_local_system_library_package()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("SystemLibraryPackageRepo");
        var project = scope.CreateDirectory(Path.Combine("SystemLibraryPackageRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("SystemLibraryPackageRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            """
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                targets: [.systemLibrary(name: "CLib", pkgConfig: "libfoo")]
            )
            """);
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("systemLibrary", exception.Message, StringComparison.Ordinal);
        Assert.Contains("pkg-config", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("targets: [.plugin(name: \"Generator\", capability: .buildTool())]")]
    [InlineData("targets: [.target(name: \"App\", plugins: [.plugin(name: \"Generator\")])]")]
    public void ResolveExactAppleSourceCommit_rejects_local_swift_build_tool_plugins(string targets)
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("BuildToolPluginPackageRepo");
        var project = scope.CreateDirectory(Path.Combine("BuildToolPluginPackageRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("BuildToolPluginPackageRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $"// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Shared\", {targets})");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("plugin", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime inputs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_computed_local_package_path()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ComputedPackagePathRepo");
        var project = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            "000000000000000000000001 = { isa = XCLocalSwiftPackageReference; relativePath = Packages/Shared; };");
        var package = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Packages", "Shared"));
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            "// swift-tools-version: 6.0\nimport PackageDescription\nlet custom = \"Generated\"\nlet package = Package(name: \"Shared\", targets: [.target(name: \"Shared\", path: custom)])");
        var generated = scope.CreateDirectory(Path.Combine("ComputedPackagePathRepo", "Packages", "Shared", "Generated"));
        File.WriteAllText(Path.Combine(generated, "Injected.swift"), "struct Injected {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "Packages/Shared/Generated/\n");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("computed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_clean_smudge_filtered_worktree_bytes()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredAppleInputRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredAppleInputRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "*.pbxproj filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        File.Delete(projectFile);
        RunGit(repositoryRoot, "checkout", "--", "Sample.xcodeproj/project.pbxproj");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AppleSourceSnapshot_rejects_smudged_bytes_created_only_in_detached_checkout()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredSnapshotAppleRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredSnapshotAppleRepo", "Sample.xcodeproj"));
        var projectFile = Path.Combine(project, "project.pbxproj");
        File.WriteAllText(projectFile, "// committed project");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "*.pbxproj filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);
        Assert.Equal("// committed project", File.ReadAllText(projectFile));

        var plan = new PowerForgeAppleReleasePlan
        {
            ProjectRoot = repositoryRoot,
            Archive = true,
            SourceCommit = sourceCommit,
            RequireImmutableSourceSnapshot = true,
            ExactSourceConfigPath = configPath
        };
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = AppleReleaseSourceSnapshot.CreateIfRequired(plan);
        });

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("project.pbxproj", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_filtered_bytes_in_synchronized_tree()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("FilteredSynchronizedTreeRepo");
        var project = scope.CreateDirectory(Path.Combine("FilteredSynchronizedTreeRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = PBXFileSystemSynchronizedRootGroup;
                path = Sources;
                sourceTree = SOURCE_ROOT;
            };
            """);
        var sources = scope.CreateDirectory(Path.Combine("FilteredSynchronizedTreeRepo", "Sources"));
        var sourceFile = Path.Combine(sources, "Filtered.swift");
        File.WriteAllText(sourceFile, "struct committed {}");
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitattributes"), "Sources/*.swift filter=attested\n");
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "filter.attested.clean", "sed 's/worktree/committed/g'");
        RunGit(repositoryRoot, "config", "filter.attested.smudge", "sed 's/committed/worktree/g'");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        CommitRepository(repositoryRoot);
        File.Delete(sourceFile);
        RunGit(repositoryRoot, "checkout", "--", "Sources/Filtered.swift");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("differs from the exact source commit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Filtered.swift", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_tracked_project_lock_for_local_package_dependency()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("LockedLocalRemotePackageInputRepo");
        var project = scope.CreateDirectory(Path.Combine("LockedLocalRemotePackageInputRepo", "Sample.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            """
            000000000000000000000001 = {
                isa = XCLocalSwiftPackageReference;
                relativePath = Packages/Shared;
            };
            """);
        var package = scope.CreateDirectory(Path.Combine("LockedLocalRemotePackageInputRepo", "Packages", "Shared"));
        const string dependencyUrl = "https://example.invalid/MutablePackage.git";
        File.WriteAllText(
            Path.Combine(package, "Package.swift"),
            $$"""
            // swift-tools-version: 6.0
            import PackageDescription
            let package = Package(
                name: "Shared",
                dependencies: [
                    .package(url: "{{dependencyUrl}}", from: "1.0.0")
                ]
            )
            """);
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "LockedLocalRemotePackageInputRepo",
            "Sample.xcodeproj",
            "project.xcworkspace",
            "xcshareddata",
            "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            $$"""{ "pins": [ { "location": "{{dependencyUrl}}", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, resolved);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_tracked_workspace_lock_for_nested_project_dependency()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("WorkspacePackageLockRepo");
        var project = scope.CreateDirectory(Path.Combine("WorkspacePackageLockRepo", "Apps", "iOS", "App.xcodeproj"));
        const string dependencyUrl = "https://example.invalid/WorkspacePackage.git";
        File.WriteAllText(
            Path.Combine(project, "project.pbxproj"),
            $$"""
            000000000000000000000001 = {
                isa = XCRemoteSwiftPackageReference;
                repositoryURL = "{{dependencyUrl}}";
                requirement = { kind = upToNextMajorVersion; minimumVersion = 1.0.0; };
            };
            """);
        var workspace = scope.CreateDirectory(Path.Combine("WorkspacePackageLockRepo", "Main.xcworkspace"));
        File.WriteAllText(
            Path.Combine(workspace, "contents.xcworkspacedata"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Workspace version="1.0"><FileRef location="group:Apps/iOS/App.xcodeproj"/></Workspace>
            """);
        var schemeDirectory = scope.CreateDirectory(Path.Combine(
            "WorkspacePackageLockRepo", "Main.xcworkspace", "xcshareddata", "xcschemes"));
        File.WriteAllText(Path.Combine(schemeDirectory, "App.xcscheme"), "<Scheme/>");
        var lockDirectory = scope.CreateDirectory(Path.Combine(
            "WorkspacePackageLockRepo", "Main.xcworkspace", "xcshareddata", "swiftpm"));
        File.WriteAllText(
            Path.Combine(lockDirectory, "Package.resolved"),
            $$"""{ "pins": [ { "location": "{{dependencyUrl}}", "state": { "revision": "0123456789abcdef0123456789abcdef01234567" } } ] }""");
        var configPath = Path.Combine(repositoryRoot, "powerforge.release.json");
        File.WriteAllText(
            configPath,
            """
            {
              "AppleApps": {
                "ProjectRoot": ".",
                "Apps": [
                  {
                    "Name": "App",
                    "ProjectPath": "Main.xcworkspace",
                    "Scheme": "App"
                  }
                ]
              }
            }
            """);
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(sourceCommit, resolved);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_rejects_git_replacement_refs()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("ReplacementRefRepo");
        var project = scope.CreateDirectory(Path.Combine("ReplacementRefRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var originalHead = CommitRepository(repositoryRoot);
        File.WriteAllText(Path.Combine(repositoryRoot, "replacement.txt"), "alternate source");
        RunGit(repositoryRoot, "add", "replacement.txt");
        RunGit(repositoryRoot, "commit", "--quiet", "-m", "Replacement source");
        RunGit(repositoryRoot, "replace", originalHead, "HEAD");
        RunGit(repositoryRoot, "reset", "--hard", originalHead);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath));

        Assert.Contains("replacement refs", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExactAppleSourceCommit_accepts_sha256_repository_head()
    {
        using var scope = new TemporaryDirectoryScope();
        var repositoryRoot = scope.CreateDirectory("Sha256AppleSourceRepo");
        RunGit(repositoryRoot, "init", "--quiet", "--object-format=sha256");
        var project = scope.CreateDirectory(Path.Combine("Sha256AppleSourceRepo", "Sample.xcodeproj"));
        File.WriteAllText(Path.Combine(project, "project.pbxproj"), "// SHA-256 project");
        var configPath = WriteAppleReleaseConfig(repositoryRoot, projectRoot: ".");
        var sourceCommit = CommitRepository(repositoryRoot);

        var resolved = ReleaseBuildExecutionService.ResolveExactAppleSourceCommit(repositoryRoot, configPath);

        Assert.Equal(64, sourceCommit.Length);
        Assert.Equal(sourceCommit, resolved);
    }

    private static string WriteAppleReleaseConfig(
        string repositoryRoot,
        string projectRoot,
        bool createSharedScheme = true)
    {
        var projectPath = Path.Combine(repositoryRoot, "Sample.xcodeproj");
        if (createSharedScheme && Directory.Exists(projectPath))
        {
            var schemes = Directory.CreateDirectory(Path.Combine(projectPath, "xcshareddata", "xcschemes"));
            File.WriteAllText(Path.Combine(schemes.FullName, "Sample.xcscheme"), "<Scheme/>");
        }
        var configPath = Path.Combine(repositoryRoot, "powerforge.release.json");
        File.WriteAllText(
            configPath,
            $$"""
            {
              "AppleApps": {
                "ProjectRoot": "{{projectRoot}}",
                "Apps": [
                  {
                    "Name": "Sample",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
        return configPath;
    }

    private static string CommitRepository(string repositoryRoot)
    {
        RunGit(repositoryRoot, "init", "--quiet");
        RunGit(repositoryRoot, "config", "user.name", "PowerForge Tests");
        RunGit(repositoryRoot, "config", "user.email", "powerforge-tests@example.invalid");
        RunGit(repositoryRoot, "add", ".");
        RunGit(repositoryRoot, "commit", "--quiet", "-m", "Apple source fixture");
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to read fixture HEAD.");
        var sha = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git rev-parse HEAD failed: {error}");
        return sha;
    }
}

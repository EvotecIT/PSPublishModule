using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
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

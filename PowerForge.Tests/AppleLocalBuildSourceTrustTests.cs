using System.Diagnostics;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed class AppleLocalBuildSourceTrustTests
{
    [Fact]
    public void ValidateLocalBuildInputContainment_rejects_external_assembler_include_flags()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalAppleTrust",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            var externalRoot = Path.Combine(
                Path.GetTempPath(),
                "PowerForge.Tests.ExternalAssemblerInput",
                Guid.NewGuid().ToString("N"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                $$"""
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = XCBuildConfiguration;
                            buildSettings = {
                                OTHER_ASFLAGS = "-I {{externalRoot}}";
                            };
                        };
                    };
                }
                """);
            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project.FullName,
                "xcshareddata",
                "xcschemes"));
            File.WriteAllText(
                Path.Combine(schemeRoot.FullName, "CasaRay.xcscheme"),
                "<Scheme />");
            InitializeGitRepository(root.FullName, writeInputs: null);

            var error = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseSourceTrustService()
                    .ValidateLocalBuildInputContainment(
                        root.FullName,
                        project.FullName,
                        "CasaRay"));

            Assert.Contains(
                "OTHER_ASFLAGS",
                error.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "inside the repository",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateLocalBuildInputContainment_rejects_an_unrelated_target_script_phase_fail_closed()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalAppleTrust",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = WriteMixedProjectWithUnsafeTarget(root.FullName);
            InitializeGitRepository(root.FullName, writeInputs: null);

            var error = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseSourceTrustService()
                    .ValidateLocalBuildInputContainment(
                    root.FullName,
                    project,
                    "CasaRay"));

            Assert.Contains(
                "complete referenced Xcode project",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "shell-script build phases",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateLocalBuildInputContainment_rejects_unused_absolute_project_inputs_fail_closed()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalAppleTrust",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                """
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = PBXFileReference;
                            path = /tmp/Unrelated.xcconfig;
                            sourceTree = "<absolute>";
                        };
                    };
                }
                """);
            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project.FullName,
                "xcshareddata",
                "xcschemes"));
            File.WriteAllText(
                Path.Combine(schemeRoot.FullName, "CasaRay.xcscheme"),
                "<Scheme />");
            InitializeGitRepository(root.FullName, writeInputs: null);

            var error = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseSourceTrustService()
                    .ValidateLocalBuildInputContainment(
                        root.FullName,
                        project.FullName,
                        "CasaRay"));

            Assert.Contains(
                "complete referenced Xcode project",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "Absolute Xcode project inputs",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                "separate project",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateLocalBuildInputContainment_rejects_untracked_empty_built_directories()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalAppleTrust",
            Guid.NewGuid().ToString("N")));
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                """
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = PBXBuildFile;
                            fileRef = AA0000000000000000000002;
                        };
                        AA0000000000000000000002 = {
                            isa = PBXFileReference;
                            lastKnownFileType = folder;
                            path = Assets;
                            sourceTree = "<group>";
                        };
                    };
                }
                """);
            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project.FullName,
                "xcshareddata",
                "xcschemes"));
            File.WriteAllText(
                Path.Combine(schemeRoot.FullName, "CasaRay.xcscheme"),
                "<Scheme />");
            var assets = Directory.CreateDirectory(Path.Combine(
                root.FullName,
                "Assets"));
            File.WriteAllText(
                Path.Combine(assets.FullName, "tracked.txt"),
                "tracked");
            InitializeGitRepository(root.FullName, writeInputs: null);
            var injected = Directory.CreateDirectory(Path.Combine(
                assets.FullName,
                "Injected"));
            Assert.True(string.IsNullOrWhiteSpace(
                ReadGit(root.FullName, "status", "--porcelain")));

            var error = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseSourceTrustService()
                    .ValidateLocalBuildInputContainment(
                        root.FullName,
                        project.FullName,
                        "CasaRay"));

            Assert.Contains(
                FrameworkCompatibility.GetRelativePath(
                    root.FullName,
                    injected.FullName).Replace('\\', '/'),
                error.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "not represented by tracked source",
                error.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ValidateLocalBuildInputContainment_inspects_locked_remote_package_execution_features()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.LocalAppleTrust",
            Guid.NewGuid().ToString("N")));
        var package = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.Tests.RemotePackage",
            Guid.NewGuid().ToString("N")));
        try
        {
            InitializeGitRepository(package.FullName, () =>
            {
                File.WriteAllText(
                    Path.Combine(package.FullName, "Package.swift"),
                    "// swift-tools-version: 6.0\nimport PackageDescription\nlet package = Package(name: \"Unsafe\", targets: [.plugin(name: \"Generator\", capability: .buildTool())])\n");
            });
            var revision = ReadGit(package.FullName, "rev-parse", "HEAD").Trim();

            var project = Directory.CreateDirectory(Path.Combine(root.FullName, "CasaRay.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                $$"""
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = XCRemoteSwiftPackageReference;
                            repositoryURL = "https://example.invalid/Unsafe.git";
                            requirement = { kind = revision; revision = "{{revision}}"; };
                        };
                    };
                }
                """);
            var schemeRoot = Directory.CreateDirectory(Path.Combine(
                project.FullName,
                "xcshareddata",
                "xcschemes"));
            File.WriteAllText(Path.Combine(schemeRoot.FullName, "CasaRay.xcscheme"), "<Scheme />");
            var lockRoot = Directory.CreateDirectory(Path.Combine(
                project.FullName,
                "project.xcworkspace",
                "xcshareddata",
                "swiftpm"));
            File.WriteAllText(
                Path.Combine(lockRoot.FullName, "Package.resolved"),
                JsonSerializer.Serialize(new
                {
                    pins = new[]
                    {
                        new
                        {
                            identity = "unsafe",
                            kind = "remoteSourceControl",
                            location = "https://example.invalid/Unsafe.git",
                            state = new { revision, version = "1.0.0" }
                        }
                    },
                    version = 3
                }));
            InitializeGitRepository(root.FullName, writeInputs: null);

            var service = new AppleReleaseSourceTrustService(
                remotePackageCheckoutResolver: (_, _) => package.FullName);

            var error = Assert.Throws<InvalidOperationException>(() =>
                service.ValidateLocalBuildInputContainment(
                    root.FullName,
                    project.FullName,
                    "CasaRay"));

            Assert.Contains("plugin or macro", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
            try { package.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private static void InitializeGitRepository(
        string workingDirectory,
        Action? writeInputs)
    {
        RunGit(workingDirectory, "init");
        RunGit(workingDirectory, "config", "user.name", "PowerForge Tests");
        RunGit(workingDirectory, "config", "user.email", "powerforge-tests@example.invalid");
        writeInputs?.Invoke();
        RunGit(workingDirectory, "add", ".");
        RunGit(workingDirectory, "commit", "-m", "fixture");
    }

    private static string WriteMixedProjectWithUnsafeTarget(string root)
    {
        const string selectedTarget = "AA0000000000000000000001";
        const string unsafeTarget = "AA0000000000000000000002";
        const string scriptPhase = "AA0000000000000000000003";
        var project = Directory.CreateDirectory(Path.Combine(
            root,
            "CasaRay.xcodeproj"));
        File.WriteAllText(
            Path.Combine(project.FullName, "project.pbxproj"),
            $$"""
            {
                objects = {
                    {{selectedTarget}} = {
                        isa = PBXNativeTarget;
                        buildPhases = ();
                        buildRules = ();
                        dependencies = ();
                        productType = "com.apple.product-type.application";
                    };
                    {{unsafeTarget}} = {
                        isa = PBXNativeTarget;
                        buildPhases = ({{scriptPhase}});
                        buildRules = ();
                        dependencies = ();
                        productType = "com.apple.product-type.application";
                    };
                    {{scriptPhase}} = {
                        isa = PBXShellScriptBuildPhase;
                        files = ();
                    };
                };
            }
            """);
        var schemeRoot = Directory.CreateDirectory(Path.Combine(
            project.FullName,
            "xcshareddata",
            "xcschemes"));
        File.WriteAllText(
            Path.Combine(schemeRoot.FullName, "CasaRay.xcscheme"),
            $$"""
            <Scheme>
              <BuildAction>
                <BuildActionEntries>
                  <BuildActionEntry>
                    <BuildableReference
                      BlueprintIdentifier="{{selectedTarget}}"
                      ReferencedContainer="container:CasaRay.xcodeproj" />
                  </BuildActionEntry>
                </BuildActionEntries>
              </BuildAction>
            </Scheme>
            """);
        return project.FullName;
    }

    private static string ReadGit(string workingDirectory, params string[] arguments)
        => RunGit(workingDirectory, arguments, captureOutput: true);

    private static void RunGit(string workingDirectory, params string[] arguments)
        => _ = RunGit(workingDirectory, arguments, captureOutput: false);

    private static string RunGit(
        string workingDirectory,
        string[] arguments,
        bool captureOutput)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(standardError);
        return captureOutput ? standardOutput : string.Empty;
    }
}

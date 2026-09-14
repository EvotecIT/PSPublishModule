using PowerForge;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData(DotNetPublishStyle.PortableCompat)]
    [InlineData(DotNetPublishStyle.PortableSize)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void BuildProjectEvaluationRequests_PreservesSingleFileSdkEvidenceForPortableStyles(
        DotNetPublishStyle style)
    {
        var plan = new DotNetPublishPlan
        {
            Configuration = "Release",
            Targets =
            [
                new DotNetPublishTargetPlan
                {
                    Name = "app",
                    ProjectPath = Path.GetFullPath("App.csproj"),
                    Publish = new DotNetPublishPublishOptions(),
                    Combinations =
                    [
                        new DotNetPublishTargetCombination
                        {
                            Framework = "net10.0",
                            Runtime = "linux-x64",
                            Style = style
                        }
                    ]
                }
            ]
        };
        MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
            "BuildProjectEvaluationRequests",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var requests = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            method.Invoke(null, [Array.Empty<string>(), "Release", plan, null]));
        object request = Assert.Single(requests.Cast<object>());
        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            request.GetType().GetProperty(
                    "SdkPackageEvidenceGlobalProperties",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(request));

        Assert.Equal("true", properties["PublishSingleFile"]);
        Assert.Equal("true", properties["PublishTrimmed"]);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void RuntimeIdentifiersMatrix_IsRetainedForRestoreButNotConcretePublish()
    {
        var target = new DotNetPublishTargetPlan
        {
            Name = "desktop",
            ProjectPath = "Desktop.csproj",
            Publish = new DotNetPublishPublishOptions(),
            Combinations =
            [
                new DotNetPublishTargetCombination
                {
                    Framework = "net10.0-windows",
                    Runtime = "win-arm64",
                    Style = DotNetPublishStyle.PortableCompat
                }
            ]
        };
        var plan = new DotNetPublishPlan
        {
            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RuntimeIdentifiers"] = "linux-x64;win-x64;win-arm64"
            },
            Targets = [target]
        };

        Dictionary<string, string> restore = DotNetPublishPipelineRunner.BuildRestoreMsBuildProperties(
            plan,
            target.ProjectPath,
            "win-arm64",
            "net10.0-windows");
        Dictionary<string, string> publish = DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(
            plan,
            target,
            "net10.0-windows",
            "win-arm64",
            DotNetPublishStyle.PortableCompat);

        Assert.Equal("linux-x64;win-x64;win-arm64", restore["RuntimeIdentifiers"]);
        Assert.DoesNotContain("RuntimeIdentifiers", publish.Keys);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_ResolvesLiteralProjectReferenceRemovalSelfReference()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      GlobalPropertiesToRemove="%(GlobalPropertiesToRemove);Flavor" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == ''">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string> { ["Flavor"] = "Parent" },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void PublishProvenanceLease_UsesOneLinuxWatcherForManyDirectories()
    {
        if (!OperatingSystem.IsLinux())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string[] guardedPaths = Enumerable.Range(0, 140)
                .Select(index =>
                {
                    string directory = Directory.CreateDirectory(
                        Path.Combine(root, "inputs", "input-" + index)).FullName;
                    string path = Path.Combine(directory, "input.props");
                    File.WriteAllText(path, "<Project />");
                    return path;
                })
                .ToArray();
            using DotNetPublishPipelineRunner.PublishProvenanceLease lease =
                DotNetPublishPipelineRunner.PublishProvenanceLease.Create(guardedPaths);
            FieldInfo linuxWatcherField = typeof(DotNetPublishPipelineRunner.PublishProvenanceLease)
                .GetField("_linuxWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!;
            FieldInfo watchersField = typeof(DotNetPublishPipelineRunner.PublishProvenanceLease)
                .GetField("_watchers", BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.NotNull(linuxWatcherField.GetValue(lease));
            Assert.Empty(Assert.IsType<List<FileSystemWatcher>>(watchersField.GetValue(lease)));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void VerifiedPackageCatalog_AcceptsIsolatedSdkArchiveWithCommittedHash()
    {
        Type catalogType = typeof(DotNetPublishPipelineRunner).GetNestedType(
            "VerifiedPackageInputCatalog",
            BindingFlags.NonPublic)!;
        MethodInfo method = catalogType.GetMethod(
            "HaveSameVerifiedPackageHash",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        const string packageKey = "Microsoft.NET.ILLink.Tasks|10.0.11";
        var committed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [packageKey] = "same-content-hash"
        };
        var sdk = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [packageKey] = "same-content-hash"
        };

        Assert.True((bool)method.Invoke(null, [packageKey, committed, sdk])!);
        sdk[packageKey] = "different-content-hash";
        Assert.False((bool)method.Invoke(null, [packageKey, committed, sdk])!);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void LockedPackageHashes_SelectOnlyActiveFrameworkAndRuntimeSections()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string lockFile = Path.Combine(root, "packages.lock.json");
            File.WriteAllText(lockFile, """
                {
                  "version": 2,
                  "dependencies": {
                    "net8.0": {
                      "Microsoft.NET.ILLink.Tasks": {
                        "type": "Direct",
                        "resolved": "8.0.30",
                        "contentHash": "net8-hash"
                      }
                    },
                    "net10.0": {
                      "Shared.Package": {
                        "type": "Direct",
                        "resolved": "1.0.0",
                        "contentHash": "shared-hash"
                      },
                      "Microsoft.NET.ILLink.Tasks": {
                        "type": "Direct",
                        "resolved": "10.0.11",
                        "contentHash": "net10-hash"
                      }
                    },
                    "net10.0/linux-x64": {
                      "Runtime.Package": {
                        "type": "Transitive",
                        "resolved": "2.0.0",
                        "contentHash": "runtime-hash"
                      }
                    },
                    "net10.0/win-x64": {
                      "Windows.Package": {
                        "type": "Transitive",
                        "resolved": "3.0.0",
                        "contentHash": "windows-hash"
                      }
                    },
                    ".NETStandard,Version=v2.1": {
                      "Standard.Package": {
                        "type": "Direct",
                        "resolved": "4.0.0",
                        "contentHash": "standard-hash"
                      }
                    },
                    "net10.0-windows7.0": {
                      "Windows.Forms.Package": {
                        "type": "Direct",
                        "resolved": "5.0.0",
                        "contentHash": "windows-forms-hash"
                      }
                    },
                    "net10.0-windows7.0/win-x64": {
                      "Windows.Runtime.Package": {
                        "type": "Transitive",
                        "resolved": "6.0.0",
                        "contentHash": "windows-runtime-hash"
                      }
                    }
                  }
                }
                """);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetNestedType(
                    "VerifiedPackageInputCatalog",
                    BindingFlags.NonPublic)!
                .GetMethod("TryReadLockedPackageHashes", BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] arguments = [lockFile, "net10.0", "linux-x64", null];

            Assert.True((bool)method.Invoke(null, arguments)!);
            var hashes = Assert.IsType<Dictionary<string, string>>(arguments[3]);
            Assert.Equal(3, hashes.Count);
            Assert.Equal("shared-hash", hashes["Shared.Package|1.0.0"]);
            Assert.Equal("net10-hash", hashes["Microsoft.NET.ILLink.Tasks|10.0.11"]);
            Assert.Equal("runtime-hash", hashes["Runtime.Package|2.0.0"]);
            Assert.DoesNotContain("Microsoft.NET.ILLink.Tasks|8.0.30", hashes.Keys);
            Assert.DoesNotContain("Windows.Package|3.0.0", hashes.Keys);

            object?[] standardArguments = [lockFile, "netstandard2.1", null, null];
            Assert.True((bool)method.Invoke(null, standardArguments)!);
            var standardHashes = Assert.IsType<Dictionary<string, string>>(standardArguments[3]);
            Assert.Single(standardHashes);
            Assert.Equal("standard-hash", standardHashes["Standard.Package|4.0.0"]);

            object?[] windowsArguments = [lockFile, "net10.0-windows", "win-x64", null];
            Assert.True((bool)method.Invoke(null, windowsArguments)!);
            var windowsHashes = Assert.IsType<Dictionary<string, string>>(windowsArguments[3]);
            Assert.Equal(2, windowsHashes.Count);
            Assert.Equal("windows-forms-hash", windowsHashes["Windows.Forms.Package|5.0.0"]);
            Assert.Equal("windows-runtime-hash", windowsHashes["Windows.Runtime.Package|6.0.0"]);

            object?[] missingArguments = [lockFile, "net9.0", "linux-x64", null];
            Assert.False((bool)method.Invoke(null, missingArguments)!);
            Assert.Empty(Assert.IsType<Dictionary<string, string>>(missingArguments[3]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void VerifiedPackageCatalog_InheritsOnlyMissingSdkEvidenceWithoutPromotingLockedPackages()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string packageRoot = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string sharedArchive = Path.Combine(packageRoot, "shared.nupkg");
            string sharedAliasArchive = Path.Combine(packageRoot, "shared-alias.nupkg");
            string sdkArchive = Path.Combine(packageRoot, "sdk.nupkg");
            Type runnerType = typeof(DotNetPublishPipelineRunner);
            Type catalogType = runnerType.GetNestedType(
                "VerifiedPackageInputCatalog",
                BindingFlags.NonPublic)!;
            Type cacheType = runnerType.GetNestedType(
                "VerifiedPackageArchiveCache",
                BindingFlags.NonPublic)!;
            object cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
            try
            {
                ConstructorInfo constructor = Assert.Single(
                    catalogType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
                object catalog = constructor.Invoke(
                [
                    new[] { packageRoot },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Shared.Package|1.0.0"] = "shared-hash"
                    },
                    cache,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Shared.Package|1.0.0"] = sharedArchive
                    },
                    Array.Empty<string>()
                ]);
                Type evidenceType = catalogType.GetNestedType(
                    "VerifiedSdkManagedPackageEvidence",
                    BindingFlags.NonPublic)!;
                Array evidence = Array.CreateInstance(evidenceType, 2);
                evidence.SetValue(
                    Activator.CreateInstance(evidenceType, "Shared.Package|1.0.0", "shared-hash", sharedAliasArchive),
                    0);
                evidence.SetValue(
                    Activator.CreateInstance(evidenceType, "Sdk.Package|1.0.0", "sdk-hash", sdkArchive),
                    1);
                MethodInfo inherit = catalogType.GetMethod(
                    "TryInheritSdkManagedPackageEvidence",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                object?[] arguments = [evidence, null];

                Assert.True((bool)inherit.Invoke(catalog, arguments)!);
                Assert.Null(arguments[1]);

                var inherited = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
                    catalogType.GetProperty(
                            "SdkManagedPackageEvidence",
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(catalog));
                object inheritedPackage = Assert.Single(inherited.Cast<object>());
                Assert.Equal(
                    "Sdk.Package|1.0.0",
                    evidenceType.GetProperty("PackageKey")!.GetValue(inheritedPackage));

                Array conflictingEvidence = Array.CreateInstance(evidenceType, 1);
                conflictingEvidence.SetValue(
                    Activator.CreateInstance(evidenceType, "Shared.Package|1.0.0", "different-hash", sharedArchive),
                    0);
                object?[] conflictingArguments = [conflictingEvidence, null];
                Assert.False((bool)inherit.Invoke(catalog, conflictingArguments)!);
                Assert.Contains("conflicted", Assert.IsType<string>(conflictingArguments[1]));
            }
            finally
            {
                (cache as IDisposable)?.Dispose();
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ProjectEvaluationRequest_IsolatesDependencyAndReleaseRootScopes()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string appProject = Path.Combine(root, "App", "App.csproj");
            string libraryProject = Path.Combine(root, "Library", "Library.csproj");
            Type requestType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "ProjectEvaluationRequest",
                BindingFlags.NonPublic)!;
            ConstructorInfo constructor = Assert.Single(
                requestType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
            object CreateRequest(string projectPath) => constructor.Invoke(
            [
                projectPath,
                "net10.0",
                "Release",
                null,
                null,
                null,
                null,
                true,
                null,
                true,
                null
            ]);
            MethodInfo forScope = requestType.GetMethod(
                "ForEvaluationScope",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo forProject = requestType.GetMethod(
                "ForProject",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(string)],
                modifiers: null)!;
            MethodInfo buildVisitKey = requestType.GetMethod(
                "BuildVisitKey",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            PropertyInfo isScopeRoot = requestType.GetProperty(
                "IsEvaluationScopeRoot",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            object appRoot = forScope.Invoke(CreateRequest(appProject), [appProject])!;
            object libraryAsDependency = forProject.Invoke(appRoot, [libraryProject, "net10.0"])!;
            object libraryAsRoot = forScope.Invoke(CreateRequest(libraryProject), [libraryProject])!;

            Assert.False((bool)isScopeRoot.GetValue(libraryAsDependency)!);
            Assert.True((bool)isScopeRoot.GetValue(libraryAsRoot)!);
            Assert.NotEqual(
                (string)buildVisitKey.Invoke(libraryAsDependency, null)!,
                (string)buildVisitKey.Invoke(libraryAsRoot, null)!);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void WriteManifestsWithProvenance_UsesConfirmedPublishProvenance()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string outputDirectory = Directory.CreateDirectory(Path.Combine(root, "publish")).FullName;
            File.WriteAllText(Path.Combine(outputDirectory, "App.dll"), "payload");
            string manifestPath = Path.Combine(root, "manifest.json");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Outputs = new DotNetPublishOutputs { ManifestJsonPath = manifestPath }
            };
            var artefacts = new List<DotNetPublishArtefactResult>
            {
                new()
                {
                    Category = DotNetPublishArtefactCategory.Publish,
                    Target = "App",
                    Framework = "net8.0",
                    Runtime = "win-x64",
                    Style = DotNetPublishStyle.PortableCompat,
                    PublishDir = outputDirectory,
                    OutputDir = outputDirectory,
                    Files = 1,
                    TotalBytes = 7
                }
            };
            var confirmed = new DotNetPublishPipelineRunner.SourceProvenance(
                "confirmed-revision",
                dirty: false);

            DotNetPublishPipelineRunner.WriteManifestsWithProvenance(
                plan,
                artefacts,
                new List<DotNetPublishStorePackageResult>(),
                new List<DotNetPublishMsiBuildResult>(),
                verifiedSourceProvenance: confirmed);

            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement entry = Assert.Single(manifest.RootElement.EnumerateArray());
            Assert.Equal("confirmed-revision", entry.GetProperty("SourceRevision").GetString());
            Assert.False(entry.GetProperty("SourceDirty").GetBoolean());
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_NoBuildPublishKeepsPublicDefiningProjectMetadataBeforeSnapshotCopyBinding()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            DotNetPublishResult result = RunNoBuildSnapshotScenario(
                root,
                propertyXml: null,
                """
                <Target Name="CaptureOriginalDefiningProject" BeforeTargets="_ComputeResolvedFilesToPublishTypes">
                  <ItemGroup>
                    <PowerForgeAppItem Include="@(ResolvedFileToPublish)"
                                       Condition="'%(ResolvedFileToPublish.RelativePath)' == 'App.dll'" />
                  </ItemGroup>
                  <Error Condition="'%(PowerForgeAppItem.DefiningProjectName)' == 'PowerForge.NoBuildPublishInputs'"
                         Text="The public publish item was replaced before project hooks observed its defining project." />
                </Target>
                """,
                out string outputDirectory,
                out _);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(outputDirectory, "App.dll")));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedDotNetInstallationSnapshot_ObservesFrameworkAndWorkloadPackRoots()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            string sdkDirectory = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100")).FullName;
            string packFile = CreateFixtureFile(root, "packs", "Microsoft.NETCore.App.Ref", "10.0.0", "ref.dll");
            string manifestFile = CreateFixtureFile(root, "sdk-manifests", "10.0.100", "fixture", "WorkloadManifest.json");
            string workloadFile = CreateFixtureFile(root, "metadata", "workloads", "installed.json");
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(Path.Combine(sdkDirectory, "MSBuild.dll"), "trusted-sdk");

            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(executablePath, root);

            Assert.True(snapshot.AffectsCapturedClosureForTest(packFile));
            Assert.True(snapshot.AffectsCapturedClosureForTest(manifestFile));
            Assert.True(snapshot.AffectsCapturedClosureForTest(workloadFile));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedDotNetInstallationSnapshot_IntermediateValidationRejectsObservedSdkReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string executablePath = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            string sdkDirectory = Directory.CreateDirectory(Path.Combine(root, "sdk", "10.0.100")).FullName;
            string sdkAssembly = Path.Combine(sdkDirectory, "MSBuild.dll");
            File.WriteAllText(executablePath, "trusted-host");
            File.WriteAllText(sdkAssembly, "trusted-sdk");
            using DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedDotNetInstallationSnapshot.Create(executablePath, root);

            File.AppendAllText(sdkAssembly, "-replaced");

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.ValidateUnchanged(verifyHashes: false));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedNativeAotPathSnapshot_RejectsCompilerReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string toolPath = Path.Combine(root, OperatingSystem.IsWindows() ? "link.exe" : "clang");
            File.WriteAllText(toolPath, "trusted-native-tool");
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    toolPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
#endif
            using DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot.Create(root);

            File.WriteAllText(toolPath, "replaced-native-tool");

            Assert.Throws<InvalidOperationException>(() =>
                snapshot.ValidateUnchanged(verifyHashes: true));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TrustedNativeAotPathSnapshot_IgnoresUntrackedNonExecutableFiles()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string toolPath = Path.Combine(root, "link.exe");
            string libraryPath = Path.Combine(root, "unrelated.dll");
            File.WriteAllText(toolPath, "trusted-native-tool");
            File.WriteAllText(libraryPath, "unrelated-library");

            using DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot snapshot =
                DotNetPublishPipelineRunner.TrustedNativeAotPathSnapshot.Create(root);

            Assert.True(snapshot.AffectsNativeToolForTest(toolPath));
            Assert.True(snapshot.AffectsNativeToolForTest(Path.Combine(root, "new-shim.cmd")));
            Assert.False(snapshot.AffectsNativeToolForTest(libraryPath));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SourceProvenance_RejectsNewUntrackedSourceAtCachedCheckpoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string projectPath = Path.Combine(projectDirectory, "App.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(projectDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");
            Assert.False(provenance.Dirty);

            File.WriteAllText(
                Path.Combine(projectDirectory, "NewSource.cs"),
                "internal static class NewSource { }");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                provenance.ValidateCurrentSource);
            Assert.Contains("NewSource.cs", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SourceProvenance_RejectsHeadAdvanceAtCachedCheckpoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string sourcePath = Path.Combine(root, "Program.cs");
            File.WriteAllText(sourcePath, "internal static class Program { }");
            RunGit(root, "add Program.cs");
            RunGit(root, "commit -m \"approved source\"");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, sourceRootPaths: [root]);
            Assert.False(provenance.Dirty);

            File.AppendAllText(sourcePath, Environment.NewLine + "// later commit");
            RunGit(root, "add Program.cs");
            RunGit(root, "commit -m \"later source\"");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                provenance.ValidateCurrentSource);
            Assert.Contains("revision changed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void SourceProvenance_AllowsNewGeneratedOutputAtCachedCheckpoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            RunGit(root, "add Program.cs");
            RunGit(root, "commit -m \"approved source\"");
            string outputDirectory = Path.Combine(root, "Artifacts", "Publish");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    generatedPaths: [outputDirectory],
                    sourceRootPaths: [root]);
            Assert.False(provenance.Dirty);

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "App.dll"), "published");

            provenance.ValidateCurrentSource();
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void TryBuildManifestProvenance_PreservesSharedCachedCheckpoint()
    {
        int validationCount = 0;
        var shared = new DotNetPublishPipelineRunner.SourceProvenance(
            "approved-revision",
            dirty: false,
            validateCurrentSource: () => validationCount++);
        DotNetPublishArtefactResult[] artefacts =
        [
            CreatePublishArtefact("win-x64"),
            CreatePublishArtefact("linux-x64")
        ];
        var provenances = new Dictionary<string, DotNetPublishPipelineRunner.SourceProvenance>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["App|net8.0|win-x64|PortableCompat"] = shared,
            ["App|net8.0|linux-x64|PortableCompat"] = shared
        };

        DotNetPublishPipelineRunner.SourceProvenance? result =
            DotNetPublishPipelineRunner.TryBuildManifestProvenance(artefacts, provenances);

        Assert.Same(shared, result);
        result!.ValidateCurrentSource();
        Assert.Equal(1, validationCount);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void NormalizeBuildInputPathRoot_PreservesFileSystemRoot()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

        string normalized = DotNetPublishPipelineRunner.NormalizeBuildInputPathRoot(root);

        Assert.Equal(root, normalized);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void NormalizeBuildInputPathRoot_CanonicalizesMacOsVarAlias()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            return;
        }

        string normalized = DotNetPublishPipelineRunner.NormalizeBuildInputPathRoot(
            "/var/folders/powerforge");

        Assert.Equal("/private/var/folders/powerforge", normalized);
    }

    private static DotNetPublishArtefactResult CreatePublishArtefact(string runtime)
        => new()
        {
            Category = DotNetPublishArtefactCategory.Publish,
            Target = "App",
            Framework = "net8.0",
            Runtime = runtime,
            Style = DotNetPublishStyle.PortableCompat
        };

    private static string CreateFixtureFile(string root, params string[] parts)
    {
        string path = parts.Aggregate(root, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return path;
    }
}

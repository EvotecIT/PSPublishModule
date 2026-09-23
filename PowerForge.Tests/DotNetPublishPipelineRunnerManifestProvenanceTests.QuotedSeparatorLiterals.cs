using System.Reflection;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ProjectEvaluationIdentityHash_IsDeterministicWithoutExposingTheValue()
    {
        const string value = "private-build-credential";

        string first = DotNetPublishPipelineRunner.HashProjectEvaluationIdentityValue(value);
        string second = DotNetPublishPipelineRunner.HashProjectEvaluationIdentityValue(value);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.DoesNotContain(value, first, StringComparison.Ordinal);
        Assert.NotEqual(
            first,
            DotNetPublishPipelineRunner.HashProjectEvaluationIdentityValue(value + "-different"));
    }

    [Fact]
    public void PowerForgeSdkPackageLock_ReadsVersionedPackageHashes()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string lockPath = Path.Combine(root, "packages.powerforge.lock.json");
            File.WriteAllText(
                lockPath,
                """
                {
                  "version": 1,
                  "sdkManagedPackages": {
                    "Microsoft.NETCore.App.Runtime.win-x64": {
                      "version": "10.0.11",
                      "contentHash": "sha512-runtime"
                    }
                  }
                }
                """);

            Type catalogType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "VerifiedPackageInputCatalog",
                BindingFlags.NonPublic)!;
            MethodInfo read = catalogType.GetMethod(
                "TryReadPowerForgeSdkPackageHashes",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] arguments = [lockPath, null];

            Assert.True((bool)read.Invoke(null, arguments)!);
            var hashes = Assert.IsType<Dictionary<string, string>>(arguments[1]);
            Assert.Equal(
                "sha512-runtime",
                hashes["Microsoft.NETCore.App.Runtime.win-x64|10.0.11"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("2", "10.0.11", "sha512-runtime")]
    [InlineData("1", "not-a-version", "sha512-runtime")]
    [InlineData("1", "10.0.11", "")]
    public void PowerForgeSdkPackageLock_RejectsMalformedEntries(
        string schemaVersion,
        string packageVersion,
        string contentHash)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string lockPath = Path.Combine(root, "packages.powerforge.lock.json");
            File.WriteAllText(
                lockPath,
                $$"""
                {
                  "version": {{schemaVersion}},
                  "sdkManagedPackages": {
                    "Microsoft.NETCore.App.Runtime.win-x64": {
                      "version": "{{packageVersion}}",
                      "contentHash": "{{contentHash}}"
                    }
                  }
                }
                """);

            Type catalogType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "VerifiedPackageInputCatalog",
                BindingFlags.NonPublic)!;
            MethodInfo read = catalogType.GetMethod(
                "TryReadPowerForgeSdkPackageHashes",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            object?[] arguments = [lockPath, null];

            Assert.False((bool)read.Invoke(null, arguments)!);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("$([System.String]::Copy('$(MSBuildProjectDirectory)').Replace('\\','/'))")]
    [InlineData("$([System.String]::Copy('$(MSBuildProjectDirectory)').Replace('/','\\'))")]
    public void ControlledBuildInputScanner_AllowsQuotedDirectorySeparatorLiterals(string value)
    {
        Assert.False(DotNetPublishPipelineRunner.ContainsRootedBuildValue(value, gitRoot: null));
    }

    [Fact]
    public void ControlledBuildInputScanner_StillRejectsQuotedRootedReplacement()
    {
        string rooted = OperatingSystem.IsWindows()
            ? @"C:\outside\payload"
            : "/outside/payload";
        string value =
            "$([System.String]::Copy('$(MSBuildProjectDirectory)').Replace('\\','" + rooted + "'))";

        Assert.True(DotNetPublishPipelineRunner.ContainsRootedBuildValue(value, gitRoot: null));
    }

    [Fact]
    public void EvaluatedPropertyQueries_AreSplitBelowTheCommandLengthBudget()
    {
        string[] commonArguments = ["msbuild", "App.csproj", "-nologo", "-verbosity:quiet"];
        string[] propertyNames = Enumerable.Range(0, 18)
            .Select(index => "ConditionProperty" + index.ToString("D3") + new string('X', 12))
            .ToArray();

        string[][] batches = DotNetPublishPipelineRunner.BuildEvaluatedPropertyQueryBatches(
            commonArguments,
            propertyNames,
            maximumCommandLength: 180);

        Assert.True(batches.Length > 1);
        Assert.Equal(
            propertyNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            batches.SelectMany(batch => batch));
        Assert.All(batches, batch =>
        {
            int estimatedLength = commonArguments.Sum(argument => argument.Length + 3) +
                                  "-getProperty:MSBuildProjectFullPath".Length + 3 +
                                  batch.Sum(name => "-getProperty:".Length + name.Length + 3);
            Assert.InRange(estimatedLength, 1, 180);
        });
    }


    [Fact]
    public void ControlledBuildInputs_AcceptProjectDirectorySeparatorNormalization()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup>
                    <NormalizedProjectDirectory>$([System.String]::Copy('$(MSBuildProjectDirectory)').Replace('\','/'))</NormalizedProjectDirectory>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptUnsafeValueOnlyWhenEveryEvaluatedContextDisablesIt()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = WriteConditionalExternalPathProject(root);
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]> contexts =
                new Dictionary<string, IReadOnlyDictionary<string, string>[]>(
                    OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal)
                {
                    [Path.GetFullPath(projectPath)] =
                    [
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["UseLocalDependency"] = "false"
                        },
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["UseLocalDependency"] = "false",
                            ["TargetFramework"] = "net10.0"
                        }
                    ]
                };

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedProjectContexts: contexts));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_IgnoreExistsInputInsideInactiveTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup>
                    <EnableNativeCopy>$([System.String]::Copy('$(MSBuildProjectName)').ToUpperInvariant().StartsWith('CERTNOOB'))</EnableNativeCopy>
                  </PropertyGroup>
                  <Target Name="CopyNative" Condition="'$(EnableNativeCopy)' == 'true'">
                    <ItemGroup>
                      <NativeFile Include="$(OutDir)native.dll" Condition="Exists('$(OutDir)native.dll')" />
                    </ItemGroup>
                    <Copy SourceFiles="$(OutDir)native.dll" DestinationFiles="$(OutDir)copied-native.dll" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EnableNativeCopy"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectTargetTimeActivationOfEvaluatedInactiveCopyInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            File.WriteAllText(externalPath, "untracked external payload");
            var project = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XElement("Project",
                    new System.Xml.Linq.XElement("PropertyGroup",
                        new System.Xml.Linq.XElement("UseExternal", "false")),
                    new System.Xml.Linq.XElement("Target",
                        new System.Xml.Linq.XAttribute("Name", "Build"),
                        new System.Xml.Linq.XElement("PropertyGroup",
                            new System.Xml.Linq.XElement("UseExternal", "true")),
                        new System.Xml.Linq.XElement("Copy",
                            new System.Xml.Linq.XAttribute(
                                "Condition",
                                "'$(UseExternal)' == 'true'"),
                            new System.Xml.Linq.XAttribute("SourceFiles", externalPath),
                            new System.Xml.Linq.XAttribute("DestinationFolder", "output")))));
            project.Save(projectPath);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseExternal"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("BeforeTargets")]
    [InlineData("DependsOnTargets")]
    public void ControlledBuildInputs_RejectPrerequisiteTargetActivationOfEvaluatedInactiveCopyInput(
        string schedulingMode)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkedPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "untracked external payload");
            try
            {
                File.CreateSymbolicLink(linkedPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string buildDependencies = schedulingMode == "DependsOnTargets"
                ? " DependsOnTargets=\"ActivateExternalInput\""
                : string.Empty;
            string activationSchedule = schedulingMode == "BeforeTargets"
                ? " BeforeTargets=\"Build\""
                : string.Empty;
            File.WriteAllText(
                projectPath,
                $"""
                <Project>
                  <PropertyGroup><UseExternal>false</UseExternal></PropertyGroup>
                  <Target Name="ActivateExternalInput"{activationSchedule}>
                    <PropertyGroup><UseExternal>true</UseExternal></PropertyGroup>
                  </Target>
                  <Target Name="Build"{buildDependencies}>
                    <Copy Condition="'$(UseExternal)' == 'true'"
                          SourceFiles="payload-link.txt"
                          DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseExternal"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AllowNonFileConditionWithUnevaluatedProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup Condition="'$(OptionalFeature)' == ''">
                    <FeatureState>disabled</FeatureState>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_IgnoreExistsOperandAfterTrueOrBranch()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemGroup Condition="'$(OptionalPath)' == '' or !Exists('$(OptionalPath)')">
                    <Compile Include="Program.cs" />
                  </ItemGroup>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OptionalPath"] = string.Empty
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectEmptyExistsOperandBeforeTrueOrBranch()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <ItemGroup Condition="Exists('$(OptionalPath)') or 'true' == 'true'">
                    <Compile Include="Program.cs" />
                  </ItemGroup>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["OptionalPath"] = string.Empty
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData(null)]
    public void ControlledBuildInputs_RejectUnresolvedExistsInputWhenTargetCanRun(string? enabled)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <PropertyGroup>
                    <EnableNativeCopy>$([System.String]::Copy('$(MSBuildProjectName)').ToUpperInvariant().StartsWith('CERTNOOB'))</EnableNativeCopy>
                  </PropertyGroup>
                  <Target Name="CopyNative" Condition="'$(EnableNativeCopy)' == 'true'">
                    <ItemGroup>
                      <NativeFile Include="$(OutDir)native.dll" Condition="Exists('$(OutDir)native.dll')" />
                    </ItemGroup>
                    <Copy SourceFiles="$(OutDir)native.dll" DestinationFiles="$(OutDir)copied-native.dll" />
                  </Target>
                </Project>
                """);
            IReadOnlyDictionary<string, string> properties = enabled is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EnableNativeCopy"] = enabled
                };

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: properties));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AggregateProjectContextsForSharedProps()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string propsPath = WriteConditionalExternalPathProject(root, "Directory.Build.props");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project />");
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>[]> contexts =
                new Dictionary<string, IReadOnlyDictionary<string, string>[]>(
                    OperatingSystem.IsWindows()
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal)
                {
                    [Path.GetFullPath(projectPath)] =
                    [
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["TargetFramework"] = "net10.0"
                        }
                    ]
                };
            IReadOnlyDictionary<string, string> globalProperties =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseLocalDependency"] = "false"
                };

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [propsPath, projectPath],
                [propsPath, projectPath],
                evaluatedGlobalProperties: globalProperties,
                controlledProjectPath: projectPath,
                evaluatedProjectContexts: contexts));

            contexts = new Dictionary<string, IReadOnlyDictionary<string, string>[]>(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            {
                [Path.GetFullPath(projectPath)] =
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["UseLocalDependency"] = "true"
                    }
                ]
            };
            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [propsPath, projectPath],
                [propsPath, projectPath],
                evaluatedGlobalProperties: globalProperties,
                controlledProjectPath: projectPath,
                evaluatedProjectContexts: contexts));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData(null)]
    public void ControlledBuildInputs_RejectUnsafeValueWhenConditionIsActiveOrUnproven(
        string? useLocalDependency)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = WriteConditionalExternalPathProject(root);
            IReadOnlyDictionary<string, string> properties = useLocalDependency is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseLocalDependency"] = useLocalDependency
                };

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: properties));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static string WriteConditionalExternalPathProject(
        string root,
        string fileName = "App.proj")
    {
        string projectPath = Path.Combine(root, fileName);
        File.WriteAllText(
            projectPath,
            """
            <Project>
              <PropertyGroup Condition="'$(UseLocalDependency)' == 'true' and Exists('$(MSBuildThisFileDirectory)marker.txt')">
                <LocalDependencyRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..\LocalDependency'))</LocalDependencyRoot>
              </PropertyGroup>
            </Project>
            """);
        return projectPath;
    }
}

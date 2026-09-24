using System.Reflection;
using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildTasks_IgnorePropertyFunctionInsideImmutableInactiveTarget()
    {
        XDocument document = XDocument.Parse(
            "<Project><Target Name='Build' Condition=\"'$(Flavor)' == 'Signed'\"><Message Text=\"$([System.String]::Copy('unreachable'))\" /></Target></Project>");
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Plain"
        };

        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document,
            [document],
            properties));
        Assert.False(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document,
            [document],
            properties,
            properties));
        var activeProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Signed"
        };
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document,
            [document],
            activeProperties,
            activeProperties));
    }

    [Theory]
    [InlineData("'01' == '1'")]
    [InlineData("'0x1' == '1'")]
    [InlineData("'On' == 'true'")]
    [InlineData("'!false' == 'true'")]
    [InlineData("'!true' == 'false'")]
    [InlineData("'Pl%61in' == 'Plain'")]
    [InlineData("'NaN' == 'NaN'")]
    [InlineData("'NaN' != 'NaN'")]
    [InlineData("'+NaN' != '+NaN'")]
    [InlineData("'-NaN' != '-NaN'")]
    [InlineData("'%4e%61%4e' == '%4e%61%4e'")]
    [InlineData("!('01' != '1')")]
    public void ControlledBuildTasks_KeepTargetsWithUnmodeledMsBuildComparison(string condition)
    {
        XDocument document = XDocument.Parse(
            $"<Project><Target Name='Danger' BeforeTargets='Build' Condition=\"{condition}\"><Exec Command='unsafe' /></Target></Project>");
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document, [document], properties, properties));
    }

    [Fact]
    public void ControlledBuildTasks_SkipIdenticalNumericGuardOperands()
    {
        XDocument document = XDocument.Parse(
            "<Project><Target Name='Inactive' BeforeTargets='Build' Condition=\"'$(TargetFrameworkVersion)' != '8.0'\"><Exec Command='unsafe' /></Target></Project>");
        var immutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TargetFrameworkVersion"] = "8.0"
        };

        Assert.False(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document, [document], immutable, immutable));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void ControlledBuildTasks_KeepDependencyFromActiveDuplicateTarget(
        bool inactiveLast,
        bool useCallTarget)
    {
        string activeBuild = useCallTarget
            ? "<Target Name='Build'><CallTarget Targets='Danger' /></Target>"
            : "<Target Name='Build' DependsOnTargets='Danger' />";
        XDocument activeDefinition = XDocument.Parse(
            "<Project>" + activeBuild +
            "<Target Name='Danger'><Exec Command='unsafe' /></Target></Project>");
        XDocument inactiveDefinition = XDocument.Parse("""
            <Project>
              <Target Name="Build" Condition="'$(Flavor)' == 'Signed'" />
            </Project>
            """);
        var immutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Plain"
        };

        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            activeDefinition,
            inactiveLast
                ? [activeDefinition, inactiveDefinition]
                : [inactiveDefinition, activeDefinition],
            immutable,
            immutable));
    }

    [Fact]
    public void ControlledBuildTasks_KeepHookFromActiveDuplicateTarget()
    {
        XDocument activeDefinition = XDocument.Parse("""
            <Project>
              <Target Name="Hook" BeforeTargets="Build"><Exec Command="unsafe" /></Target>
            </Project>
            """);
        XDocument inactiveDefinition = XDocument.Parse("""
            <Project>
              <Target Name="Hook" Condition="'$(Flavor)' == 'Signed'" />
            </Project>
            """);
        var immutable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Plain"
        };

        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            activeDefinition,
            [activeDefinition, inactiveDefinition],
            immutable,
            immutable));
    }

    [Theory]
    [InlineData("DependsOnTargets='Missing$(Unknown)'")]
    [InlineData("")]
    public void ControlledBuildTasks_SkipInactiveTargetDestinations(string targetAttributes)
    {
        string operation = targetAttributes.Length == 0
            ? "<CallTarget Targets='Missing$(Unknown)' /><OnError ExecuteTargets='Missing$(Unknown)' />"
            : string.Empty;
        XDocument document = XDocument.Parse(
            $"<Project><Target Name='Build' Condition=\"'$(Flavor)' == 'Signed'\" {targetAttributes}>{operation}</Target></Project>");
        var inactive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Plain"
        };
        var active = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Signed"
        };

        Assert.False(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document, [document], inactive, inactive));
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document, [document], active, active));
    }

    [Fact]
    public void ControlledBuildTasks_KeepHooksAroundSkippedTarget()
    {
        XDocument document = XDocument.Parse(
            "<Project><Target Name='Build' Condition=\"'$(Flavor)' == 'Signed'\" /><Target Name='Hook' BeforeTargets='Build'><Exec Command='unsafe' /></Target></Project>");
        var inactive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Flavor"] = "Plain"
        };

        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document, [document], inactive, inactive));
    }

    [Theory]
    [InlineData("CoreCompile")]
    [InlineData("PrepareForBuild")]
    [InlineData("ResolveAssemblyReferences")]
    public void ControlledBuildTasks_KeepSdkHooksWhenSdkPathsAreNotReplayed(string destination)
    {
        XDocument document = XDocument.Parse(
            $"<Project><Target Name='Inject' BeforeTargets='{destination}'><Exec Command='unsafe' /></Target></Project>");
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledControlledBuildTask(
            document,
            [document],
            conservativeSdkHooks: true));
    }

    [Fact]
    public void ControlledBuildInputs_SkipInactiveSdkFileProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath,
                "<Project><Target Name='Build' Condition=\"'$(Flavor)' == 'Signed'\"><PropertyGroup><ApplicationIcon>../outside.ico</ApplicationIcon></PropertyGroup></Target></Project>");

            bool IsControlled(string flavor) => DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = flavor
                });

            Assert.True(IsControlled("Plain"));
            Assert.False(IsControlled("Signed"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_SkipInactiveCallTargetDestination()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath,
                "<Project><Target Name='Build' Condition=\"'$(Flavor)' == 'Signed'\"><CallTarget Targets='Missing$(Unknown)' /></Target></Project>");

            bool IsControlled(string flavor) => DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = flavor
                });

            Assert.True(IsControlled("Plain"));
            Assert.False(IsControlled("Signed"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("Plain", true)]
    [InlineData("Signed", false)]
    public void ReadSourceProvenance_ScopesSdkParentTargetGuardAcrossProjectReference(
        string flavor,
        bool safe)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                      <Target Name="PotentialInput" BeforeTargets="Build" Condition="'$(Flavor)' == 'Signed'">
                        <PropertyGroup><ApplicationIcon>../../../outside.ico</ApplicationIcon></PropertyGroup>
                      </Target>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = flavor
                },
                buildFramework: "net8.0");

        if (safe)
        {
            Assert.True(!provenance.DirtyReasons.Any(reason =>
                reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        else
        {
            Assert.Contains(provenance.DirtyReasons, reason =>
                reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("<Project><ImportGroup><Import Project='package.targets' /></ImportGroup></Project>", true)]
    [InlineData("<Project TreatAsLocalProperty='Flavor'><Import Project='package.targets' /></Project>", false)]
    [InlineData("<Project><Target Name='Change'><PropertyGroup><Flavor>Signed</Flavor></PropertyGroup></Target></Project>", false)]
    public void GeneratedImportGuardProofRequiresAnInertWrapper(string xml, bool expected)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "App.csproj.nuget.g.targets");
        try
        {
            File.WriteAllText(path, xml);
            Assert.Equal(expected, DotNetPublishPipelineRunner.IsGuardInertGeneratedImportWrapper(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Theory]
    [InlineData("Plain", "Plain", true)]
    [InlineData("Plain", "Signed", false)]
    [InlineData("Signed", "Plain", false)]
    public void ReadSourceProvenance_ChecksSharedTargetInEachProjectInstance(
        string parentFlavor,
        string childFlavor,
        bool safe)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" AdditionalProperties="Flavor={childFlavor}" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Directory.Build.targets"] = """
                        <Project>
                          <Target Name="PotentialInput" BeforeTargets="Build" Condition="'$(Flavor)' == 'Signed'">
                            <PropertyGroup><ApplicationIcon>../../../outside.ico</ApplicationIcon></PropertyGroup>
                            <ItemGroup><Content Include="local.txt" CopyToPublishDirectory="Always" /></ItemGroup>
                          </Target>
                        </Project>
                        """,
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }",
                    ["src/App/local.txt"] = "fixture",
                    ["src/Library/local.txt"] = "fixture"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = parentFlavor
                },
                buildFramework: "net8.0");

        Assert.True(safe == !provenance.DirtyReasons.Any(reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase)),
            "Reasons: " + string.Join(" | ", provenance.DirtyReasons) +
            " Paths: " + string.Join(" | ", provenance.DirtyPaths));
    }

    [Theory]
    [InlineData("Plain", "Plain", true)]
    [InlineData("Plain", "Signed", false)]
    [InlineData("Signed", "Plain", true)]
    public void ReadSourceProvenance_ScopesChildLocalTargetDuringReferenceResolution(
        string parentFlavor,
        string childFlavor,
        bool safe)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" ReferenceOutputAssembly="false" AdditionalProperties="Flavor={childFlavor}" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <Target Name="PotentialInput" BeforeTargets="CoreCompile" Condition="'$(Flavor)' == 'Signed'">
                        <PropertyGroup><ApplicationIcon>../../../outside.ico</ApplicationIcon></PropertyGroup>
                      </Target>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = parentFlavor
                },
                buildFramework: "net8.0");

        Assert.True(safe == !provenance.DirtyReasons.Any(reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase)),
            "Reasons: " + string.Join(" | ", provenance.DirtyReasons) +
            " Paths: " + string.Join(" | ", provenance.DirtyPaths));
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotInheritRemovedParentGuardProof()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" ReferenceOutputAssembly="false" UndefineProperties="Flavor" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework><Flavor>Plain</Flavor></PropertyGroup>
                      <Target Name="PotentialInput" BeforeTargets="CoreCompile" Condition="'$(Flavor)' == 'Signed'">
                        <PropertyGroup><ApplicationIcon>../../../outside.ico</ApplicationIcon></PropertyGroup>
                      </Target>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = "Plain"
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("TreatAsLocalProperty='Flavor'", "")]
    [InlineData("", "<Target Name='ChangeFlavor' BeforeTargets='CoreCompile'><PropertyGroup><Flavor>Signed</Flavor></PropertyGroup></Target>")]
    public void ReadSourceProvenance_ChildProjectCannotClaimMutableGuard(
        string projectAttributes,
        string mutationTarget)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" ReferenceOutputAssembly="false" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: $"""
                    <Project Sdk="Microsoft.NET.Sdk" {projectAttributes}>
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      {mutationTarget}
                      <Target Name="PotentialInput" BeforeTargets="CoreCompile" Condition="'$(Flavor)' == 'Signed'">
                        <PropertyGroup><ApplicationIcon>../../../outside.ico</ApplicationIcon></PropertyGroup>
                      </Target>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = "Plain"
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("'$(GatePath)' == '__SOURCE_ROOT__'")]
    [InlineData("'$(GuardAlias)' == '__SOURCE_ROOT__'")]
    [InlineData("'$(MSBuildThisFileDirectory)' == '__APP_DIR_WITH_SEPARATOR__'")]
    public void ReadSourceProvenance_DoesNotHideActivePathGuardAfterCheckoutRelocation(
        string condition)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <GuardAlias>$(GatePath)</GuardAlias>
                      </PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                      <Target Name="PathGuard" BeforeTargets="CoreCompile" Condition="{condition}">
                        <Exec Command="echo fixture" />
                      </Target>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GatePath"] = "__SOURCE_ROOT__"
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotHideChildPathGuardAfterCheckoutRelocation()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <Target Name="ChildPathGuard" BeforeTargets="CoreCompile"
                              Condition="'$(GatePath)' == '__SOURCE_ROOT__'">
                        <Exec Command="echo fixture" />
                      </Target>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["GatePath"] = "__SOURCE_ROOT__"
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("BuildProjectReferences", "false", "true")]
    [InlineData("RunAnalyzers", "true", "false")]
    public void ReadSourceProvenance_DoesNotHideControlledOverrideGuard(
        string propertyName,
        string originalValue,
        string controlledValue)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                      <Target Name="ControlledOverrideGuard" BeforeTargets="CoreCompile"
                              Condition="'$({propertyName})' == '{controlledValue}'">
                        <Exec Command="echo fixture" />
                      </Target>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [propertyName] = originalValue
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotHideChildBuildProjectReferencesOverride()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance =
            ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <Target Name="ChildControlledOverride" BeforeTargets="CoreCompile"
                              Condition="'$(BuildProjectReferences)' == 'false'">
                        <Exec Command="echo fixture" />
                      </Target>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["src/Library/Selected.cs"] = "public static class SelectedInput { }"
                },
                mutatedPath: "src/Library/Selected.cs",
                buildProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildProjectReferences"] = "true"
                },
                buildFramework: "net8.0");

        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ControlledBuildInputs_AcceptEmptySourceItemFromInactiveTaskOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Target Name="GenerateSources" Condition="'$(Flavor)' == 'Signed'">
                    <Message Text="unused">
                      <Output TaskParameter="Text" ItemName="Compile" />
                    </Message>
                  </Target>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Compile)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Flavor"] = "Plain"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledPublishPlaceholders_AcceptOnlyInactiveTaskOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourceRoot = Path.Combine(root, "controlled");
            Directory.CreateDirectory(sourceRoot);
            string project = """
                <Project>
                  <Target Name="GenerateSources" Condition="'$(Flavor)' == 'Signed'">
                    <Message Text="generated">
                      <Output TaskParameter="Text" ItemName="Compile" />
                    </Message>
                  </Target>
                  <Target Name="Build">
                    <ItemGroup><ResolvedFileToPublish Include="@(Compile)" /></ItemGroup>
                  </Target>
                </Project>
                """;
            string originalPath = Path.Combine(root, "App.proj");
            string controlledPath = Path.Combine(sourceRoot, "App.proj");
            File.WriteAllText(originalPath, project);
            File.WriteAllText(controlledPath, project);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "TryCreateControlledPublishInputPlaceholders",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Evaluate(string flavor) => (bool)method.Invoke(
                null,
                [root, sourceRoot, controlledPath, new[] { originalPath },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Flavor"] = flavor
                    }, false])!;

            Assert.True(Evaluate("Plain"));
            Assert.False(Evaluate("Signed"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("TargetFramework", "net8.0", "net9.0")]
    [InlineData("Configuration", "Release", "Debug")]
    public void ControlledPublishPlaceholders_UseEffectiveRequestGuardProperties(
        string propertyName,
        string selectedValue,
        string activeValue)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourceRoot = Path.Combine(root, "controlled");
            Directory.CreateDirectory(sourceRoot);
            string project = $"""
                <Project>
                  <Target Name="GenerateSources" Condition="'$({propertyName})' == '{activeValue}'">
                    <Message Text="generated">
                      <Output TaskParameter="Text" ItemName="Compile" />
                    </Message>
                  </Target>
                  <Target Name="Build">
                    <ItemGroup><ResolvedFileToPublish Include="@(Compile)" /></ItemGroup>
                  </Target>
                </Project>
                """;
            string originalPath = Path.Combine(root, "App.proj");
            string controlledPath = Path.Combine(sourceRoot, "App.proj");
            File.WriteAllText(originalPath, project);
            File.WriteAllText(controlledPath, project);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "TryCreateControlledPublishInputPlaceholders",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Evaluate(string selected, bool hasMatrix) => (bool)method.Invoke(
                null,
                [root, sourceRoot, controlledPath, new[] { originalPath },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [propertyName] = selected
                    }, hasMatrix])!;

            Assert.True(Evaluate(selectedValue, false));
            Assert.False(Evaluate(activeValue, false));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledPublishPlaceholders_DoNotUseInnerFrameworkProofForMatrixRestore()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourceRoot = Path.Combine(root, "controlled");
            Directory.CreateDirectory(sourceRoot);
            const string project = """
                <Project>
                  <Target Name="GenerateSources" Condition="'$(TargetFramework)' == ''">
                    <Message Text="generated">
                      <Output TaskParameter="Text" ItemName="Compile" />
                    </Message>
                  </Target>
                  <Target Name="Build">
                    <ItemGroup><ResolvedFileToPublish Include="@(Compile)" /></ItemGroup>
                  </Target>
                </Project>
                """;
            string originalPath = Path.Combine(root, "App.proj");
            string controlledPath = Path.Combine(sourceRoot, "App.proj");
            File.WriteAllText(originalPath, project);
            File.WriteAllText(controlledPath, project);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "TryCreateControlledPublishInputPlaceholders",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            bool Evaluate(bool hasMatrix) => (bool)method.Invoke(
                null,
                [root, sourceRoot, controlledPath, new[] { originalPath },
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["TargetFramework"] = "net8.0"
                    }, hasMatrix])!;

            Assert.True(Evaluate(false));
            Assert.False(Evaluate(true));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }


    [Fact]
    public void ControlledBuildInputs_IgnoreAssignmentsInsideInactiveTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Target Name="CopyNative" Condition="'$(IsCertNoobProject)' == 'true' and '$(TargetFramework)' == 'net472'">
                    <PropertyGroup>
                      <NativeRid>win-x64</NativeRid>
                    </PropertyGroup>
                    <ItemGroup>
                      <NativeFile Include="$(OutDir)native.dll" Condition="Exists('$(NativeRid)/native.dll')" />
                    </ItemGroup>
                    <Copy SourceFiles="@(NativeFile)" DestinationFiles="$(OutDir)native.dll" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["IsCertNoobProject"] = "false",
                    ["TargetFramework"] = "net10.0-windows"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImplicitLastTaskResultActivation()
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
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Target Name="Check" BeforeTargets="CopyNative">
                    <Message Text="completed" />
                  </Target>
                  <Target Name="CopyNative" Condition="'$(MSBuildLastTaskResult)' == 'true'">
                    <PropertyGroup><NativeRid>win-x64</NativeRid></PropertyGroup>
                    <ItemGroup>
                      <NativeFile Include="payload-link.txt" Condition="Exists('payload-link.txt')" />
                    </ItemGroup>
                    <Copy SourceFiles="@(NativeFile)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBuildLastTaskResult"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("<Project><Target Name='BuildOnlySettings'><PropertyGroup><BuildingProject>true</BuildingProject></PropertyGroup></Target></Project>")]
    [InlineData("<Project TreatAsLocalProperty='BuildingProject' />")]
    [InlineData("<Project><Target Name='BuildOnlySettings'><Message Text='done'><Output TaskParameter='Text' PropertyName='BuildingProject' /></Message></Target></Project>")]
    public void ImmutableTargetGuardProperties_ExcludeExternalMutations(string importedProject)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string importPath = Path.Combine(root, "Imported.targets");
            File.WriteAllText(importPath, importedProject);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "ReadImmutableTargetGuardProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildingProject"] = "false",
                ["TargetFramework"] = "net10.0-windows"
            };

            var immutable = (IReadOnlyDictionary<string, string>)method.Invoke(
                null,
                [globals, new[] { importPath }])!;
            Assert.False(immutable.ContainsKey("BuildingProject"));
            Assert.Equal("net10.0-windows", immutable["TargetFramework"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ImmutableTargetGuardProperties_FailClosedForDynamicOutputName()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string importPath = Path.Combine(root, "Imported.targets");
            File.WriteAllText(
                importPath,
                "<Project><Target Name='Change'><Message Text='done'><Output TaskParameter='Text' PropertyName='$(AssignedProperty)' /></Message></Target></Project>");
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "ReadImmutableTargetGuardProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetFramework"] = "net10.0-windows"
            };

            var immutable = (IReadOnlyDictionary<string, string>)method.Invoke(
                null,
                [globals, new[] { importPath }])!;
            Assert.Empty(immutable);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("<MSBuild Projects='$(MSBuildProjectFullPath)' Properties='Flavor=Signed' />")]
    [InlineData("<MSBuild Projects='$(MSBuildProjectFullPath)' RemoveProperties='Flavor' />")]
    [InlineData("<MSBuild Projects='@(NestedProjects)' />")]
    public void ImmutableTargetGuardProperties_PreserveParentAcrossNestedBuilds(string task)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name='Nested'>" + task + "</Target></Project>");
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "ReadImmutableTargetGuardProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Flavor"] = "Plain"
            };

            var immutable = (IReadOnlyDictionary<string, string>)method.Invoke(
                null,
                [globals, new[] { projectPath }])!;
            Assert.Equal("Plain", immutable["Flavor"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

}

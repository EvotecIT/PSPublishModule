using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_DoesNotRestoreUnselectedDeclaredFrameworkPackages()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
                    <PackageReference Include="System.Text.Json" Version="10.0.10" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Shared.Value; } }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved selected-framework graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PreservesDeclaredFrameworkSemanticsForSelectedSubset()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string bridgeDirectory = Directory.CreateDirectory(Path.Combine(root, "Bridge")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../Shared/Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net10.0; net8.0;net9.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFrameworks)' == 'net10.0; net8.0;net9.0'">
                    <PackageReference Include="NuGet.Versioning" Version="7.9.0" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public static int Value => NuGet.Versioning.NuGetVersion.Parse(\"1.0.0\").Major; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved selected subset graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("single-context")]
    [InlineData("two-contexts")]
    [InlineData("conditional-packages")]
    [InlineData("custom-props")]
    [InlineData("missing-props-path")]
    [InlineData("missing-props-environment")]
    [InlineData("empty-props-path")]
    [InlineData("case-sensitive-context-values")]
    [InlineData("external-intermediate-path")]
    [InlineData("apostrophe-project-path")]
    [InlineData("apostrophe-escaped-output")]
    [InlineData("escaped-output")]
    [InlineData("escaped-outdir")]
    [InlineData("escaped-assets")]
    [InlineData("shadowed-verifier")]
    [InlineData("controlled-properties")]
    [InlineData("disabled-props-environment")]
    [InlineData("defaulted-property")]
    [InlineData("environment-props")]
    [InlineData("mutated-isolation-marker")]
    [InlineData("spoofed-original-props")]
    [InlineData("target-frameworks-context")]
    [InlineData("keep-symbols")]
    [InlineData("case-variant-symbols")]
    [InlineData("disabled-props-project")]
    [InlineData("disabled-props-project-reset")]
    [InlineData("single-context-empty-base")]
    [InlineData("removed-output-excludes")]
    [InlineData("partial-output-excludes")]
    [InlineData("late-path-mutation")]
    [InlineData("computed-late-path-mutation")]
    [InlineData("reference-props-override")]
    [InlineData("rid-list-context")]
    [InlineData("inactive-pack-path-target")]
    [InlineData("inactive-context-target")]
    [InlineData("pack-hook-called-by-build")]
    [InlineData("uppercase-context-paths")]
    [InlineData("context-activated-import")]
    [InlineData("context-activated-indirect-import")]
    [InlineData("context-activated-external-alias-import")]
    [InlineData("context-activated-property-method-import")]
    [InlineData("context-activated-chained-import")]
    [InlineData("controlled-override-path-target")]
    [InlineData("same-context-multi-framework-output")]
    [InlineData("mixed-framework-append")]
    [InlineData("global-output-roots")]
    [InlineData("reference-global-intermediate-roots")]
    [InlineData("override-activated-import")]
    [InlineData("second-context-activated-import")]
    [InlineData("inactive-override-import")]
    public void ReadSourceProvenance_RestoresEverySelectedFrameworkForSharedMultiTargetReference(string scenario)
    {
        bool distinctRestoreContexts = scenario != "single-context";
        bool unselectedContextPackage = scenario != "single-context" && scenario != "two-contexts" &&
            scenario != "defaulted-property" && scenario != "same-context-multi-framework-output" &&
            scenario != "mixed-framework-append" &&
            scenario != "reference-global-intermediate-roots" &&
            scenario != "inactive-override-import";
        bool customDirectoryBuildProps = scenario == "custom-props";
        bool emptyDirectoryBuildPropsPath = scenario == "empty-props-path";
        bool caseSensitiveContextValues = scenario == "case-sensitive-context-values";
        bool externalIntermediatePath = scenario == "external-intermediate-path";
        bool overrideSharedOutputPath = scenario == "escaped-output" || scenario == "shadowed-verifier" ||
            scenario == "mutated-isolation-marker" || scenario == "apostrophe-escaped-output";
        bool overrideSharedOutDir = scenario == "escaped-outdir";
        bool overrideSharedAssets = scenario == "escaped-assets";
        bool shadowVerifier = scenario == "shadowed-verifier";
        bool controlledProperties = scenario == "controlled-properties";
        bool disabledPropsEnvironment = scenario == "disabled-props-environment";
        bool defaultedProperty = scenario == "defaulted-property";
        bool environmentProps = scenario == "environment-props" ||
            scenario == "missing-props-environment";
        bool mutatedIsolationMarker = scenario == "mutated-isolation-marker";
        bool spoofedOriginalProps = scenario == "spoofed-original-props";
        bool targetFrameworksContext = scenario == "target-frameworks-context";
        bool sameContextMultiFrameworkOutput = scenario == "same-context-multi-framework-output" ||
            scenario == "mixed-framework-append";
        bool mixedFrameworkAppend = scenario == "mixed-framework-append";
        bool caseVariantSymbols = scenario == "case-variant-symbols";
        bool keepSymbols = scenario == "keep-symbols" || caseVariantSymbols || externalIntermediatePath ||
            sameContextMultiFrameworkOutput;
        bool disabledPropsProject = scenario == "disabled-props-project" ||
            scenario == "disabled-props-project-reset";
        bool resetDisabledPropsProject = scenario == "disabled-props-project-reset";
        bool emptySingleContextBase = scenario == "single-context-empty-base";
        bool removedOutputExcludes = scenario == "removed-output-excludes";
        bool partialOutputExcludes = scenario == "partial-output-excludes";
        bool latePathMutation = scenario == "late-path-mutation";
        bool controlledOverridePathTarget = scenario == "controlled-override-path-target";
        bool globalOutputRoots = scenario == "global-output-roots";
        bool referenceGlobalIntermediateRoots = scenario == "reference-global-intermediate-roots";
        bool overrideActivatedImport = scenario is "override-activated-import" or "second-context-activated-import";
        bool inactiveOverrideImport = scenario == "inactive-override-import";
        bool computedLatePathMutation = scenario == "computed-late-path-mutation";
        bool referencePropsOverride = scenario == "reference-props-override";
        bool ridListContext = scenario == "rid-list-context";
        bool inactivePackPathTarget = scenario == "inactive-pack-path-target";
        bool inactiveContextTarget = scenario == "inactive-context-target";
        bool packHookCalledByBuild = scenario == "pack-hook-called-by-build";
        bool uppercaseContextPaths = scenario == "uppercase-context-paths";
        bool contextActivatedImport = scenario == "context-activated-import" ||
            overrideActivatedImport ||
            scenario == "context-activated-indirect-import" ||
            scenario == "context-activated-external-alias-import" ||
            scenario == "context-activated-property-method-import" ||
            scenario == "context-activated-chained-import";
        bool indirectContextActivatedImport = scenario == "context-activated-indirect-import";
        bool externalAliasContextImport = scenario == "context-activated-external-alias-import";
        bool propertyMethodContextImport = scenario == "context-activated-property-method-import";
        bool chainedContextImport = scenario == "context-activated-chained-import";
        if (caseSensitiveContextValues || externalIntermediatePath || ridListContext ||
            inactivePackPathTarget || inactiveContextTarget || packHookCalledByBuild || uppercaseContextPaths ||
            contextActivatedImport)
            unselectedContextPackage = false;
        if ((caseVariantSymbols || uppercaseContextPaths) && !OperatingSystem.IsWindows())
            return;
        string root = Directory.CreateTempSubdirectory().FullName;
        string? externalIntermediateDirectory = null;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string bridgeDirectory = Directory.CreateDirectory(Path.Combine(root, "Bridge")).FullName;
            bool apostropheProjectPath = scenario == "apostrophe-project-path" ||
                scenario == "apostrophe-escaped-output";
            string sharedDirectoryName = apostropheProjectPath ? "Bob'sShared" : "Shared";
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, sharedDirectoryName)).FullName;
            string leafDirectory = Directory.CreateDirectory(Path.Combine(root, "Leaf")).FullName;
            string utilityDirectory = Directory.CreateDirectory(Path.Combine(root, "Utility")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            string leafProject = Path.Combine(leafDirectory, "Leaf.csproj");
            string utilityProject = Path.Combine(utilityDirectory, "Utility.csproj");
            string? customPropsPath = null;
            string customPropsArgument = string.Empty;
            string globalOutputRoot = Path.Combine(root, "global-bin") + Path.DirectorySeparatorChar;
            string referenceGlobalIntermediateRoot =
                (Path.Combine(root, "global-obj") + Path.DirectorySeparatorChar).Replace('\\', '/');
            string globalOutputArguments = globalOutputRoots
                ? $" -p:BaseOutputPath={globalOutputRoot} -p:OutputPath={globalOutputRoot}"
                : string.Empty;
            if (customDirectoryBuildProps || environmentProps || spoofedOriginalProps ||
                scenario == "missing-props-path")
            {
                customPropsPath = Path.Combine(root, "Custom.Build.props");
                if (scenario != "missing-props-path" && scenario != "missing-props-environment")
                    File.WriteAllText(customPropsPath,
                        "<Project><PropertyGroup><ContextPropsMarker>Loaded</ContextPropsMarker>" +
                        "<ContextPropsPathMarker Condition=\"'$(DirectoryBuildPropsPath)' == '$(MSBuildThisFileFullPath)'\">Loaded</ContextPropsPathMarker>" +
                        "</PropertyGroup></Project>");
                if (customDirectoryBuildProps || scenario == "missing-props-path")
                    customPropsArgument = $" -p:DirectoryBuildPropsPath=\"{customPropsPath}\"";
                else if (spoofedOriginalProps)
                    customPropsArgument = $" -p:_PowerForgeOriginalDirectoryBuildPropsPath=\"{customPropsPath}\"";
            }
            if (scenario == "missing-props-path" || scenario == "missing-props-environment")
                Assert.False(File.Exists(customPropsPath));
            if (emptyDirectoryBuildPropsPath)
            {
                File.WriteAllText(Path.Combine(sharedDirectory, "Directory.Build.props"),
                    "<Project><PropertyGroup><UnexpectedPropsMarker>Loaded</UnexpectedPropsMarker></PropertyGroup></Project>");
                customPropsArgument = " -p:DirectoryBuildPropsPath=";
            }
            if (externalIntermediatePath)
            {
                externalIntermediateDirectory = Directory.CreateTempSubdirectory("pf-original-obj-").FullName;
                File.WriteAllText(Path.Combine(sharedDirectory, "Directory.Build.props"),
                    $"<Project><PropertyGroup><BaseIntermediateOutputPath>{externalIntermediateDirectory}{Path.DirectorySeparatorChar}</BaseIntermediateOutputPath></PropertyGroup></Project>");
            }
            if (defaultedProperty)
                File.WriteAllText(Path.Combine(sharedDirectory, "Directory.Build.props"),
                    "<Project><PropertyGroup><Flavor Condition=\"'$(Flavor)' == ''\">Direct</Flavor></PropertyGroup></Project>");
            if (sameContextMultiFrameworkOutput)
                File.WriteAllText(Path.Combine(sharedDirectory, "Directory.Build.props"),
                    "<Project><PropertyGroup><BaseOutputPath>bin/$(TargetFramework)/</BaseOutputPath>" +
                    (mixedFrameworkAppend
                        ? "<AppendTargetFrameworkToOutputPath Condition=\"'$(TargetFramework)' == 'net8.0'\">false</AppendTargetFrameworkToOutputPath>"
                        : "<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>") +
                    "</PropertyGroup></Project>");
            string directContext = ridListContext
                ? " AdditionalProperties=\"RuntimeIdentifiers=linux-x64%3Bwin-x64\""
                : caseSensitiveContextValues
                ? " AdditionalProperties=\"DefineConstants=FOO\""
                : targetFrameworksContext
                ? " AdditionalProperties=\"TargetFrameworks=net10.0\""
                : referenceGlobalIntermediateRoots
                ? " AdditionalProperties=\"Flavor=Direct;BaseIntermediateOutputPath=../global-obj/;MSBuildProjectExtensionsPath=../global-obj/\""
                : distinctRestoreContexts && !defaultedProperty
                ? " AdditionalProperties=\"Flavor=Direct\""
                : string.Empty;
            string bridgeContext = referencePropsOverride
                ? " AdditionalProperties=\"Flavor=Bridge;DirectoryBuildPropsPath=$(MSBuildProjectDirectory)/../Reference.Build.props\""
                : ridListContext
                ? " AdditionalProperties=\"RuntimeIdentifiers=linux-x64%3Bosx-x64\""
                : caseSensitiveContextValues
                ? " AdditionalProperties=\"DefineConstants=foo\""
                : targetFrameworksContext
                ? " AdditionalProperties=\"TargetFrameworks=net8.0\""
                : referenceGlobalIntermediateRoots
                ? " AdditionalProperties=\"Flavor=Bridge;BaseIntermediateOutputPath=../global-obj/;MSBuildProjectExtensionsPath=../global-obj/\""
                : sameContextMultiFrameworkOutput
                ? " AdditionalProperties=\"Flavor=Direct\""
                : distinctRestoreContexts
                ? " AdditionalProperties=\"Flavor=Bridge\""
                : string.Empty;
            if (referencePropsOverride)
                File.WriteAllText(Path.Combine(root, "Reference.Build.props"), "<Project />");
            string bridgeSharedReference = caseVariantSymbols
                ? "../shared/Shared.csproj"
                : $"../{sharedDirectoryName}/Shared.csproj";
            File.WriteAllText(appProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../{sharedDirectoryName}/Shared.csproj"{directContext} />
                    {(emptySingleContextBase || sameContextMultiFrameworkOutput ? "<ProjectReference Include=\"../Utility/Utility.csproj\" />" : string.Empty)}
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="{bridgeSharedReference}"{bridgeContext} /></ItemGroup>
                </Project>
                """);
            string contextPackages = unselectedContextPackage
                ? """
                  <ItemGroup Condition="'$(Flavor)' == 'Direct' and '$(TargetFramework)' == 'net10.0'">
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Bridge' and '$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="System.Text.Json" Version="10.0.10" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Direct' and '$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="NuGet.Versioning" Version="7.9.0" />
                  </ItemGroup>
                  """
                : string.Empty;
            string outputOverride = overrideSharedOutputPath
                ? "<OutputPath>$(BaseOutputPath)../shared/</OutputPath>"
                : uppercaseContextPaths
                    ? "<BaseOutputPath Condition=\"$([System.String]::Copy('$(BaseOutputPath)').Contains('powerforge-context'))\">$(BaseOutputPath.ToUpperInvariant())</BaseOutputPath>"
                : string.Empty;
            string outDirOverride = overrideSharedOutDir
                ? "<OutDir>$(BaseOutputPath)../shared/</OutDir>"
                : string.Empty;
            string assetsOverride = overrideSharedAssets
                ? "<ProjectAssetsFile Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\">$(MSBuildProjectDirectory)/../shared-assets/$(Flavor)/project.assets.json</ProjectAssetsFile>"
                : string.Empty;
            string defaultItemExcludesOverride = removedOutputExcludes
                ? "<DefaultItemExcludes Condition=\"$([System.String]::Copy('$(BaseOutputPath)').Contains('powerforge-context'))\">$(BaseIntermediateOutputPath)**</DefaultItemExcludes>"
                : string.Empty;
            if (partialOutputExcludes)
                defaultItemExcludesOverride = "<DefaultItemExcludes Condition=\"$([System.String]::Copy('$(BaseOutputPath)').Contains('powerforge-context'))\">$(DefaultItemExcludes)bogus</DefaultItemExcludes>";
            string latePathTarget = latePathMutation
                ? "<Target Name=\"MutateContextIntermediate\" BeforeTargets=\"Build\" Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target>"
                : controlledOverridePathTarget
                    ? "<Target Name=\"ControlledOverrideMutation\" BeforeTargets=\"CoreCompile\" Condition=\"'$(BuildProjectReferences)' == 'false'\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target>"
                : computedLatePathMutation
                    ? "<PropertyGroup><DestinationProperty>IntermediateOutputPath</DestinationProperty></PropertyGroup><Target Name=\"MutateContextIntermediate\" BeforeTargets=\"Build\"><CreateProperty Value=\"$(MSBuildProjectDirectory)/../shared-obj/\"><Output TaskParameter=\"Value\" PropertyName=\"$(DestinationProperty)\" /></CreateProperty></Target>"
                : inactivePackPathTarget || packHookCalledByBuild
                    ? "<Target Name=\"PrepareUnusedPackPath\" BeforeTargets=\"Pack\"><PropertyGroup><PublishDir>$(MSBuildProjectDirectory)/../pack-output/</PublishDir></PropertyGroup></Target>"
                : inactiveContextTarget
                    ? "<Target Name=\"DisabledContextPath\" BeforeTargets=\"CoreCompile\" Condition=\"'false' == 'true'\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target>"
                : string.Empty;
            string callPackHookDuringBuild = packHookCalledByBuild
                ? "<BuildDependsOn>$(BuildDependsOn);PrepareUnusedPackPath</BuildDependsOn>"
                : string.Empty;
            string requireFrameworkSplit = sameContextMultiFrameworkOutput
                ? "<Target Name=\"RequireFrameworkSplit\" BeforeTargets=\"CoreCompile\" Condition=\"$(BaseOutputPath.Contains('powerforge-context')) and '$(AppendTargetFrameworkToOutputPath)' != 'true'\"><Error Condition=\"$(OutputPath.Contains('framework-')) != 'True' or $(IntermediateOutputPath.Contains('framework-')) != 'True'\" Text=\"Framework outputs were not isolated: OutputPath=$(OutputPath), IntermediateOutputPath=$(IntermediateOutputPath), BaseOutputPath=$(BaseOutputPath), Append=$(AppendTargetFrameworkToOutputPath).\" /></Target>"
                : string.Empty;
            string contextualImport = externalAliasContextImport
                ? "<Import Project=\"Context.aliases.props\" /><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
                : chainedContextImport
                    ? "<Import Project=\"Context.aliases.props\" Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\" /><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
                : overrideActivatedImport
                    ? "<Import Project=\"Context.targets\" Condition=\"'$(BuildProjectReferences)' == 'false'" +
                      (scenario == "second-context-activated-import" ? " and '$(Flavor)' == 'Direct'" : "") + "\" />"
                : inactiveOverrideImport
                    ? "<Import Project=\"Missing.targets\" Condition=\"'false' == 'true' and '$(BuildProjectReferences)' == 'false'\" />"
                : indirectContextActivatedImport
                ? "<PropertyGroup><ContextImportEnabled>$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))</ContextImportEnabled></PropertyGroup><Import Project=\"Context.targets\" Condition=\"'$(ContextImportEnabled)' == 'true'\" />"
                : propertyMethodContextImport
                    ? "<Import Project=\"Context.targets\" Condition=\"$(BaseIntermediateOutputPath.Contains('powerforge-context'))\" />"
                : contextActivatedImport
                    ? "<Import Project=\"Context.targets\" Condition=\"$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))\" />"
                    : string.Empty;
            if (contextActivatedImport)
            {
                if (externalAliasContextImport || chainedContextImport)
                    File.WriteAllText(Path.Combine(sharedDirectory, "Context.aliases.props"),
                        "<Project><PropertyGroup><ContextImportEnabled>$([System.String]::Copy('$(BaseIntermediateOutputPath)').Contains('powerforge-context'))</ContextImportEnabled></PropertyGroup></Project>");
                File.WriteAllText(Path.Combine(sharedDirectory, "Context.targets"),
                    "<Project><Target Name=\"MutateImportedContext\" BeforeTargets=\"CoreCompile\"><PropertyGroup><IntermediateOutputPath>$(MSBuildProjectDirectory)/../shared-obj/</IntermediateOutputPath></PropertyGroup></Target></Project>");
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(
                    Path.Combine(root, "shared-obj")).FullName, ".keep"), string.Empty);
            }
            string isolationMarkerOverride = mutatedIsolationMarker
                ? "<_PowerForgeRequiresContextIsolation>false</_PowerForgeRequiresContextIsolation><_PowerForgeRestoreContextMatched>false</_PowerForgeRestoreContextMatched>"
                : string.Empty;
            string verifierCollision = shadowVerifier
                ? "<Target Name=\"PowerForgeVerifyRestoreContext\" />"
                : string.Empty;
            string sharedProjectStart = disabledPropsProject
                ? "<Project><PropertyGroup><ImportDirectoryBuildProps>false</ImportDirectoryBuildProps></PropertyGroup>" +
                  "<Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" />" +
                  (resetDisabledPropsProject
                      ? "<PropertyGroup><ImportDirectoryBuildProps>true</ImportDirectoryBuildProps></PropertyGroup>"
                      : string.Empty)
                : "<Project Sdk=\"Microsoft.NET.Sdk\">";
            string sharedProjectEnd = disabledPropsProject
                ? "<Import Project=\"Sdk.targets\" Sdk=\"Microsoft.NET.Sdk\" /></Project>"
                : "</Project>";
            File.WriteAllText(sharedProject, $"""
                {sharedProjectStart}
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <NuGetLockFilePath Condition="'$(Flavor)' != ''">packages.$(Flavor).lock.json</NuGetLockFilePath>
                    {outputOverride}
                    {outDirOverride}
                    {assetsOverride}
                    {defaultItemExcludesOverride}
                    {isolationMarkerOverride}
                    {callPackHookDuringBuild}
                  </PropertyGroup>
                  <Target Name="RequireCustomProps" BeforeTargets="Restore;Build" Condition="'$(Flavor)' != '' and '{(customDirectoryBuildProps || environmentProps && scenario != "missing-props-environment").ToString().ToLowerInvariant()}' == 'true'">
                    <Error Condition="'$(ContextPropsMarker)' != 'Loaded'" Text="Custom DirectoryBuildPropsPath was not imported." />
                    <Error Condition="'$(ContextPropsPathMarker)' != 'Loaded'" Text="Original props observed a substituted DirectoryBuildPropsPath." />
                  </Target>
                  <Target Name="RejectSpoofedProps" BeforeTargets="Restore;Build" Condition="'{spoofedOriginalProps.ToString().ToLowerInvariant()}' == 'true'">
                    <Error Condition="'$(ContextPropsMarker)' == 'Loaded'" Text="A caller property redirected the original props import." />
                  </Target>
                  <Target Name="RejectUnexpectedProps" BeforeTargets="Restore;Build" Condition="'{emptyDirectoryBuildPropsPath.ToString().ToLowerInvariant()}' == 'true'">
                    <Error Condition="'$(UnexpectedPropsMarker)' == 'Loaded'" Text="An explicitly empty DirectoryBuildPropsPath imported discovered props." />
                  </Target>
                  {(targetFrameworksContext || apostropheProjectPath || referenceGlobalIntermediateRoots ? string.Empty : "<ItemGroup><ProjectReference Include=\"../Leaf/Leaf.csproj\" /></ItemGroup>")}
                  {contextPackages}
                  {verifierCollision}
                    {latePathTarget}
                    {requireFrameworkSplit}
                    {contextualImport}
                {sharedProjectEnd}
                """);
            File.WriteAllText(leafProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            if (sameContextMultiFrameworkOutput)
            {
                File.WriteAllText(utilityProject, """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                      <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" AdditionalProperties="Flavor=Bridge" /></ItemGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(utilityDirectory, "Utility.cs"),
                    "public static class Utility { public const int Value = 1; }");
            }
            if (emptySingleContextBase)
            {
                File.WriteAllText(utilityProject, """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <BaseIntermediateOutputPath></BaseIntermediateOutputPath>
                        <IntermediateOutputPath>obj/</IntermediateOutputPath>
                      </PropertyGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(utilityDirectory, "Utility.cs"),
                    "public static class Utility { public const int Value = 1; }");
            }
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                emptySingleContextBase
                    ? "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value + Utility.Value; } }"
                    : "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"), targetFrameworksContext ||
                apostropheProjectPath || referenceGlobalIntermediateRoots
                ? "public static class Shared { public const int Value = 1; }"
                : "public static class Shared { public static int Value => Leaf.Value; }");
            File.WriteAllText(Path.Combine(leafDirectory, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nshared-assets/\nglobal-bin/\nglobal-obj/\n");
            var testEnvironment = environmentProps
                ? new Dictionary<string, string> { ["DirectoryBuildPropsPath"] = customPropsPath! }
                : null;
            if (referenceGlobalIntermediateRoots)
                RunDotNet(root,
                    $"restore \"{sharedProject}\" --use-lock-file --nologo " +
                    $"-p:BaseIntermediateOutputPath={referenceGlobalIntermediateRoot} " +
                    $"-p:MSBuildProjectExtensionsPath={referenceGlobalIntermediateRoot}", testEnvironment);
            if (unselectedContextPackage)
            {
                RunDotNet(root,
                    $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Direct -p:TargetFrameworks=net10.0{customPropsArgument}{globalOutputArguments}", testEnvironment);
                RunDotNet(root,
                    $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Bridge -p:TargetFrameworks=net8.0{customPropsArgument}{globalOutputArguments}", testEnvironment);
            }
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false{customPropsArgument}{globalOutputArguments}", testEnvironment);
            if (unselectedContextPackage)
            {
                Assert.Contains("Newtonsoft.Json", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Direct.lock.json")));
                Assert.Contains("System.Text.Json", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Bridge.lock.json")));
                Assert.DoesNotContain("NuGet.Versioning", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Direct.lock.json")));
                Assert.DoesNotContain("NuGet.Versioning", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Bridge.lock.json")));
            }
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved multi-framework graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                $"/p:ContinuousIntegrationBuild=true /p:DebugType={(keepSymbols ? "portable" : "None")} " +
                $"/p:DebugSymbols={keepSymbols.ToString().ToLowerInvariant()}{customPropsArgument}{globalOutputArguments}", testEnvironment);
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Publish = new DotNetPublishPublishOptions { KeepSymbols = keepSymbols },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            if (keepSymbols)
            {
                plan.MsBuildProperties["DebugType"] = "portable";
                plan.MsBuildProperties["DebugSymbols"] = "true";
                plan.MsBuildProperties["ContinuousIntegrationBuild"] = "true";
                plan.MsBuildProperties["IncludeSourceRevisionInInformationalVersion"] = "true";
            }

            if (customPropsPath is not null)
            {
                if (customDirectoryBuildProps)
                    plan.MsBuildProperties["DirectoryBuildPropsPath"] = customPropsPath;
                else if (environmentProps)
                {
                    plan.EnvironmentVariables["DirectoryBuildPropsPath"] = customPropsPath;
                    plan.ControlledBuildEnvironmentVariableNames = ["DirectoryBuildPropsPath"];
                }
                else
                    plan.MsBuildProperties["_PowerForgeOriginalDirectoryBuildPropsPath"] = customPropsPath;
            }
            if (emptyDirectoryBuildPropsPath)
                plan.MsBuildProperties["DirectoryBuildPropsPath"] = string.Empty;
            if (controlledProperties || controlledOverridePathTarget || overrideActivatedImport)
            {
                plan.MsBuildProperties["BuildProjectReferences"] = "true";
                plan.MsBuildProperties["RestoreRecursive"] = "true";
            }
            if (globalOutputRoots)
            {
                plan.MsBuildProperties["BaseOutputPath"] = globalOutputRoot;
                plan.MsBuildProperties["OutputPath"] = globalOutputRoot;
            }
            if (disabledPropsEnvironment)
            {
                plan.EnvironmentVariables["ImportDirectoryBuildProps"] = "false";
                plan.ControlledBuildEnvironmentVariableNames = ["ImportDirectoryBuildProps"];
            }

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            if (overrideSharedOutputPath || overrideSharedOutDir || overrideSharedAssets ||
                disabledPropsEnvironment || disabledPropsProject || removedOutputExcludes ||
                partialOutputExcludes || latePathMutation || computedLatePathMutation || referencePropsOverride ||
                ridListContext || contextActivatedImport || packHookCalledByBuild || controlledOverridePathTarget)
            {
                Assert.True(provenance.Dirty);
                string expected = disabledPropsEnvironment || disabledPropsProject && !resetDisabledPropsProject
                    ? "disables Directory.Build.props imports"
                    : resetDisabledPropsProject
                        ? "PowerForgeVerifyRestoreContext_"
                    : removedOutputExcludes || partialOutputExcludes
                        ? "removes controlled output-tree exclusions"
                    : computedLatePathMutation
                        ? "MSBuild graph contains an uncontrolled build task"
                    : ridListContext
                        ? "different RuntimeIdentifiers lists"
                    : contextActivatedImport
                        ? "isolation-sensitive property"
                    : packHookCalledByBuild
                        ? "target-time assignment to an isolation-sensitive property"
                    : latePathMutation || controlledOverridePathTarget
                        ? "target-time assignment to an isolation-sensitive property"
                    : referencePropsOverride
                        ? "project reference changes DirectoryBuildPropsPath"
                    : overrideSharedOutDir
                        ? "OutDir is outside its controlled context"
                        : overrideSharedAssets
                            ? "assets path is outside its controlled context"
                        : "output path is outside its controlled context";
                Assert.True(provenance.DirtyReasons.Any(
                    reason => reason.Contains(expected, StringComparison.Ordinal)),
                    string.Join(Environment.NewLine, provenance.DirtyReasons));
            }
            else
            {
                Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            }
        }
        finally
        {
            try
            {
                DeleteTestRepository(root);
            }
            finally
            {
                if (externalIntermediateDirectory is not null)
                    DeleteTestRepository(externalIntermediateDirectory);
            }
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledRestore_SeparatesProjectsSharingDirectoryAndContext()
    {
        string directory = Path.Combine(Path.GetTempPath(), "powerforge-context-identity");
        string context = "Flavor=Direct";
        string first = DotNetPublishPipelineRunner.ComputeControlledRestoreContextDirectoryName(
            Path.Combine(directory, "First.csproj"), context);
        string second = DotNetPublishPipelineRunner.ComputeControlledRestoreContextDirectoryName(
            Path.Combine(directory, "Second.csproj"), context);

        Assert.NotEqual(first, second);
        Assert.Equal(first, DotNetPublishPipelineRunner.ComputeControlledRestoreContextDirectoryName(
            Path.Combine(directory, "First.csproj"), context));
    }

    [Theory]
    [InlineData(null, "net8.0;net10.0", false)]
    [InlineData("", "net8.0;net10.0", false)]
    [InlineData("net8.0", "net8.0;net10.0", false)]
    [InlineData("net8.0;net10.0", "net10.0", false)]
    [InlineData("net8.0;net10.0", "net8.0;net10.0", true)]
    [InlineData("net8.0;net9.0;net10.0", "net9.0;net10.0", true)]
    [InlineData("net8.0;net10.0", "net8.0;net9.0", false)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledRestore_UsesOnlyDeclaredMultiTargetFrameworkMatrix(
        string? declaredTargetFrameworks,
        string selectedTargetFrameworks,
        bool expectsMatrix)
    {
        var evaluatedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (declaredTargetFrameworks is not null)
            evaluatedProperties["TargetFrameworks"] = declaredTargetFrameworks;

        string[] frameworks = DotNetPublishPipelineRunner.SelectControlledMultiFrameworkRestoreFrameworks(
            evaluatedProperties,
            selectedTargetFrameworks.Split(';'));

        Assert.Equal(expectsMatrix, frameworks.Length > 1);
        if (expectsMatrix)
        {
            Assert.Equal(
                selectedTargetFrameworks.Split(';').OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase),
                frameworks);
        }
    }
}

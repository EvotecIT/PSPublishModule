using PowerForge;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerControlledContextInspectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledRootRestore_InspectsWrapperActivatedImports(bool unsafeImport)
    {
        string root = Directory.CreateTempSubdirectory("pf-root-restore-inspection-").FullName;
        try
        {
            string wrapper = Path.Combine(root, "PowerForge.ControlledRestoreContexts.probe.props");
            File.WriteAllText(wrapper, "<Project><PropertyGroup><_PowerForgeMatched_probe>true</_PowerForgeMatched_probe></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Context.targets"), unsafeImport
                ? "<Project><Target Name=\"Unsafe\" BeforeTargets=\"Restore\"><Exec Command=\"echo unsafe\" /><WriteLinesToFile File=\"unsafe.txt\" Lines=\"executed\" /></Target></Project>"
                : "<Project><PropertyGroup><HarmlessMarker>loaded</HarmlessMarker></PropertyGroup></Project>");
            string project = Path.Combine(root, "Probe.proj");
            File.WriteAllText(project, """
                <Project>
                  <Import Project="$(DirectoryBuildPropsPath)" />
                  <Import Project="Context.targets" Condition="$(DirectoryBuildPropsPath.Contains('PowerForge.ControlledRestoreContexts'))" />
                  <Target Name="Restore"><WriteLinesToFile File="restored.txt" Lines="completed" /></Target>
                </Project>
                """);
            // Exercise the production root-restore entry point, including its argument construction.
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Type requestType = typeof(DotNetPublishPipelineRunner).GetNestedType("ProjectEvaluationRequest", System.Reflection.BindingFlags.NonPublic)!;
            object request = requestType.GetConstructors(flags).Single().Invoke(
                [project, null, "Release", null, null, null, null, true, null, true, null]);
            var method = typeof(DotNetPublishPipelineRunner).GetMethod("TryRestoreControlledPublishRoot",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            bool accepted = (bool)method.Invoke(null,
                [request, root, root, project, new Dictionary<string, string?>(), "", "", root, wrapper])!;
            Assert.Equal(!unsafeImport, accepted);
            Assert.Equal(!unsafeImport, File.Exists(Path.Combine(root, "restored.txt")));
            Assert.False(File.Exists(Path.Combine(root, "unsafe.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("BuildDependsOn")]
    [InlineData("%42uildDependsOn")]
    [InlineData("$(DependencyProperty)")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledInvocation_BlocksTaskOutputDependencyMutation(string destination)
    {
        string root = Directory.CreateTempSubdirectory("pf-context-dependency-output-").FullName;
        try
        {
            string wrapper = Path.Combine(root, "PowerForge.ControlledRestoreContexts.probe.props");
            File.WriteAllText(wrapper, "<Project><PropertyGroup><BaseIntermediateOutputPath>obj/powerforge-context/probe/</BaseIntermediateOutputPath><_PowerForgeMatched_probe>true</_PowerForgeMatched_probe></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Context.targets"), $"""
                <Project>
                  <PropertyGroup><DependencyProperty>BuildDependsOn</DependencyProperty></PropertyGroup>
                  <Target Name="ContextSetup"><CreateProperty Value="PreparePack">
                    <Output TaskParameter="Value" PropertyName="{destination}" />
                  </CreateProperty></Target>
                  <Target Name="PreparePack" BeforeTargets="Pack">
                    <Exec Command="echo unsafe" /><WriteLinesToFile File="execution.txt" Lines="unsafe" />
                  </Target>
                </Project>
                """);
            string project = Path.Combine(root, "Probe.csproj");
            File.WriteAllText(project, """
                <Project Sdk="Microsoft.NET.Sdk" InitialTargets="ContextSetup">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Import Project="Context.targets" Condition="$(BaseIntermediateOutputPath.Contains('powerforge-context'))" />
                </Project>
                """);
            var result = DotNetPublishPipelineRunner.RunControlledMsBuildEvaluationProcess(root,
                ["msbuild", project, "-nologo", "-verbosity:quiet", "-target:Build", "-p:DirectoryBuildPropsPath=" + wrapper],
                new Dictionary<string, string?>(), TimeSpan.FromMinutes(1), root);
            Assert.NotEqual(0, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(root, "execution.txt")));
            Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledInvocation_RequiresNativeGlobalPresenceProof(bool supplied, bool local, bool accepted)
    {
        string root = Directory.CreateTempSubdirectory("pf-context-presence-").FullName;
        try
        {
            string project = Path.Combine(root, "Probe.proj");
            string wrapper = Path.Combine(root, "PowerForge.ControlledRestoreContexts.probe.props");
            File.WriteAllText(wrapper, $"""
                <Project>
                  <ItemGroup><_PowerForgeControlledPresenceProperty Include="ContextMode">
                    <ProjectPath>{System.Security.SecurityElement.Escape(project)}</ProjectPath><ResultProperty>_Supplied</ResultProperty>
                  </_PowerForgeControlledPresenceProperty></ItemGroup>
                  <PropertyGroup>
                    <Original>$(ContextMode)</Original><ContextMode>PresenceProbe</ContextMode>
                    <_Supplied Condition="$(ContextMode.Equals('PresenceProbe'))">false</_Supplied>
                    <_Supplied Condition="!$(ContextMode.Equals('PresenceProbe'))">true</_Supplied>
                    <ContextMode Condition="'$(_Supplied)' == 'false'">$(Original)</ContextMode>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(project, $"""
                <Project TreatAsLocalProperty="{(local ? "ContextMode" : "")}">
                  <Import Project="$(DirectoryBuildPropsPath)" />
                  <Target Name="Build"><WriteLinesToFile File="execution.txt" Lines="completed" /></Target>
                </Project>
                """);
            var arguments = new List<string> { "msbuild", project, "-nologo", "-verbosity:quiet", "-target:Build", "-p:DirectoryBuildPropsPath=" + wrapper };
            if (supplied) arguments.Add("-p:ContextMode=");
            var result = DotNetPublishPipelineRunner.RunControlledMsBuildEvaluationProcess(root, arguments,
                new Dictionary<string, string?>(), TimeSpan.FromMinutes(1), root);
            Assert.Equal(accepted, result.ExitCode == 0);
            Assert.Equal(accepted, File.Exists(Path.Combine(root, "execution.txt")));
            if (!accepted) Assert.Contains("does not preserve global-property presence", result.StdErr);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("alias", true)]
    [InlineData("exec", false)]
    [InlineData("initial-exec", false)]
    [InlineData("external", false)]
    [InlineData("dynamic-output", false)]
    [InlineData("publish-only", true)]
    [InlineData("publish-reached", false)]
    [InlineData("restore-activates-exec", false)]
    [InlineData("rebuild-exec", false)]
    [InlineData("clean-exec", false)]
    [InlineData("publish-selected", false)]
    [InlineData("pack-selected", false)]
    [InlineData("publish-via-root", false)]
    [InlineData("pack-via-root", false)]
    [InlineData("pack-only", true)]
    [InlineData("custom-dependency-alias", true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledInvocation_InspectsNativeImportsBeforeExecutingTargets(string scenario, bool accepted)
    {
        string root = Directory.CreateTempSubdirectory("pf-context-inspection-").FullName;
        string outside = Directory.CreateTempSubdirectory("pf-context-external-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string wrapper = Path.Combine(root, "PowerForge.ControlledRestoreContexts.probe.props");
            File.WriteAllText(wrapper, "<Project><PropertyGroup><BaseIntermediateOutputPath>obj/powerforge-context/probe/</BaseIntermediateOutputPath><_PowerForgeMatched_probe>true</_PowerForgeMatched_probe></PropertyGroup></Project>");
            string targetPath = Path.Combine(scenario == "external" ? outside : source, "Context.targets");
            string content = scenario switch
            {
                "exec" or "initial-exec" or "restore-activates-exec" => "<Target Name=\"Unsafe\" BeforeTargets=\"Build\"><Exec Command=\"echo unsafe\" /><WriteLinesToFile File=\"initial-execution.txt\" Lines=\"unsafe\" /></Target>",
                "dynamic-output" => "<PropertyGroup><PropertyToSet>OutputPath</PropertyToSet></PropertyGroup><Target Name=\"Unsafe\" BeforeTargets=\"Build\"><CreateProperty Value=\"shared-output/\"><Output TaskParameter=\"Value\" PropertyName=\"$(PropertyToSet)\" /></CreateProperty></Target>",
                "publish-only" or "publish-reached" or "publish-selected" or "publish-via-root" => "<Target Name=\"PreparePublish\" BeforeTargets=\"Publish\"><PropertyGroup><PublishDir>publish/</PublishDir></PropertyGroup></Target>",
                "pack-selected" or "pack-via-root" or "pack-only" => "<Target Name=\"PreparePack\" BeforeTargets=\"Pack\"><PropertyGroup><PublishDir>publish/</PublishDir></PropertyGroup></Target>",
                "rebuild-exec" or "clean-exec" => $"<Target Name=\"Unsafe\" BeforeTargets=\"{(scenario == "rebuild-exec" ? "Rebuild" : "Clean")}\"><Exec Command=\"echo unsafe\" /><WriteLinesToFile File=\"initial-execution.txt\" Lines=\"unsafe\" /></Target>",
                _ => "<PropertyGroup><HarmlessMarker>Loaded</HarmlessMarker></PropertyGroup>"
            };
            if (scenario == "initial-exec") content = content.Replace(" BeforeTargets=\"Build\"", string.Empty);
            if (scenario != "restore-activates-exec") File.WriteAllText(targetPath, "<Project>" + content + "</Project>");
            string project = Path.Combine(source, "Probe.proj");
            string importPath = scenario == "external" ? targetPath : "Context.targets";
            File.WriteAllText(project, $"""
                <Project InitialTargets="{(scenario == "initial-exec" ? "Unsafe" : "")}">
                  <Import Project="$(DirectoryBuildPropsPath)" />
                  <PropertyGroup><ImportAlias>{System.Security.SecurityElement.Escape(importPath)}</ImportAlias></PropertyGroup>
                  {(scenario == "custom-dependency-alias" ? "<PropertyGroup><DependencyAlias>Helper</DependencyAlias><CustomCompileDependsOn>$(DependencyAlias)</CustomCompileDependsOn></PropertyGroup>" : "")}
                  <Import Project="$(ImportAlias)" Condition="$(BaseIntermediateOutputPath.Contains('powerforge-context')) and Exists('$(ImportAlias)')" />
                  <Target Name="Restore">
                    <WriteLinesToFile File="Context.targets" Lines="{System.Security.SecurityElement.Escape("<Project>" + content + "</Project>")}" Overwrite="true" />
                  </Target>
                  <Target Name="Build" DependsOnTargets="{(scenario == "publish-reached" ? "PreparePublish" : scenario == "publish-via-root" ? "Publish" : scenario == "pack-via-root" ? "Pack" : "")}">
                    <WriteLinesToFile File="execution.txt" Lines="completed" Overwrite="true" />
                  </Target>
                  <Target Name="Clean" />
                  <Target Name="Rebuild" DependsOnTargets="Clean;Build" />
                  <Target Name="ComputeFilesToPublish" />
                  <Target Name="Publish" DependsOnTargets="{(scenario == "publish-via-root" ? "" : "Build")}" />
                  <Target Name="Pack" DependsOnTargets="{(scenario == "pack-via-root" ? "" : "Build")}" />
                </Project>
                """);
            var arguments = new List<string> { "msbuild", project, "-nologo", "-verbosity:quiet", "-target:Build", "-p:DirectoryBuildPropsPath=" + wrapper };
            arguments[4] = "-target:" + (scenario is "rebuild-exec" or "clean-exec" ? "Rebuild;ComputeFilesToPublish"
                : scenario == "publish-selected" ? "Publish" : scenario == "pack-selected" ? "Pack" : "Build");
            if (scenario == "restore-activates-exec") arguments.Add("-restore");
            var result = DotNetPublishPipelineRunner.RunControlledMsBuildEvaluationProcess(source,
                arguments,
                new Dictionary<string, string?>(), TimeSpan.FromMinutes(1), root);
            Assert.Equal(accepted, result.ExitCode == 0);
            Assert.Equal(accepted, File.Exists(Path.Combine(source, "execution.txt")));
            Assert.False(File.Exists(Path.Combine(source, "initial-execution.txt")));
            if (scenario == "restore-activates-exec") Assert.True(File.Exists(targetPath));
            if (!accepted) Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledInvocation_RequiresLockedArchiveBytesForTrustedPackageTasks(bool trusted, bool tampered)
    {
        string root = Directory.CreateTempSubdirectory("pf-context-package-").FullName;
        try
        {
            string source = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
            string packages = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string package = Directory.CreateDirectory(Path.Combine(packages, "approved", "1.0.0")).FullName;
            string build = Directory.CreateDirectory(Path.Combine(package, "build")).FullName;
            string targets = Path.Combine(build, "Approved.targets");
            File.WriteAllText(targets, "<Project><Target Name=\"ApprovedTask\" BeforeTargets=\"Build\"><Exec Command=\"echo approved-package\" /></Target></Project>");
            File.WriteAllText(Path.Combine(package, "Approved.nuspec"), "<package><metadata><id>Approved</id><version>1.0.0</version><authors>Tests</authors><description>Inspection fixture</description></metadata></package>");
            string feed = Directory.CreateDirectory(Path.Combine(root, "packages-source")).FullName;
            string archive = Path.Combine(feed, "Approved.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(package, archive);
            string hash;
            using (var reader = new PackageArchiveReader(archive)) hash = reader.GetContentHash(CancellationToken.None);
            if (tampered) File.AppendAllText(targets, "<!-- changed extracted bytes -->");
            string wrapper = Path.Combine(root, "PowerForge.ControlledRestoreContexts.probe.props");
            File.WriteAllText(wrapper, $"""
                <Project>
                  <PropertyGroup><_PowerForgeMatched_probe>true</_PowerForgeMatched_probe></PropertyGroup>
                  <ItemGroup>{(trusted ? $"<_PowerForgeControlledTrustedPackage Include=\"Approved|1.0.0\"><ContentHash>{hash}</ContentHash></_PowerForgeControlledTrustedPackage>" : "")}</ItemGroup>
                </Project>
                """);
            string project = Path.Combine(source, "Probe.proj");
            File.WriteAllText(project, $"""
                <Project><Import Project="$(DirectoryBuildPropsPath)" />
                  <Import Project="{System.Security.SecurityElement.Escape(targets)}" />
                  <Target Name="Build"><WriteLinesToFile File="execution.txt" Lines="completed" /></Target>
                </Project>
                """);
            var result = DotNetPublishPipelineRunner.RunControlledMsBuildEvaluationProcess(source,
                ["msbuild", project, "-nologo", "-verbosity:quiet", "-target:Build", "-p:DirectoryBuildPropsPath=" + wrapper],
                new Dictionary<string, string?> { ["NUGET_PACKAGES"] = packages }, TimeSpan.FromMinutes(1), root);
            bool accepted = trusted && !tampered;
            Assert.Equal(accepted, result.ExitCode == 0);
            Assert.Equal(accepted, File.Exists(Path.Combine(source, "execution.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}

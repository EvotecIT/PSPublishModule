using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerForge;

public sealed partial class DotnetPublisher
{
    /// <summary>File name of the MSBuild targets that keep NuGet package assemblies out of ReadyToRun compilation.</summary>
    internal const string ReadyToRunModuleScopeTargetsFileName = "PowerForge.ReadyToRun.ModuleAssemblies.targets";

    // The publish-only properties stay on the module project. Libraries keep the global properties of the portable
    // publish, so every runtime identifier reuses one build of each referenced project.
    private const string ReadyToRunProjectReferenceProperties =
        ProjectReferenceVersionProperties + "%3BPublishReadyToRun%3BPublishReadyToRunComposite%3BPublishReadyToRunEmitSymbols%3BCustomAfterMicrosoftCommonTargets";

    private const string ReadyToRunModuleScopeTargets = """
        <Project>
          <!-- Written by PowerForge: precompile the module's own assemblies and leave NuGet package assemblies as IL. -->
          <Target Name="PowerForgeExcludePackageAssembliesFromReadyToRun" BeforeTargets="_PrepareForReadyToRunCompilation" Condition="'$(PublishReadyToRun)' == 'true'">
            <ItemGroup>
              <PublishReadyToRunExclude Include="@(RuntimeCopyLocalItems->'%(Filename)%(Extension)')" />
            </ItemGroup>
          </Target>
        </Project>
        """;

    /// <summary>
    /// Publishes a framework-dependent ReadyToRun build of the project for each runtime identifier and returns a map of
    /// runtime identifier to publish directory. Outputs go under <paramref name="artifactsRoot"/>.
    /// </summary>
    /// <param name="projectPath">Path to the SDK-style project file to publish.</param>
    /// <param name="configuration">Build configuration (e.g., Release).</param>
    /// <param name="framework">Target framework to compile (e.g., net10.0).</param>
    /// <param name="runtimeIdentifiers">Runtime identifiers to compile for (e.g., win-x64, linux-x64, osx-arm64).</param>
    /// <param name="scope">Whether NuGet package assemblies are precompiled too.</param>
    /// <param name="version">Version to stamp into the published assemblies.</param>
    /// <param name="artifactsRoot">Root folder for build artifacts (bin/obj/publish).</param>
    /// <param name="restoreSources">Additional NuGet restore sources appended to normal project restore sources.</param>
    public IReadOnlyDictionary<string, string> PublishReadyToRun(
        string projectPath,
        string configuration,
        string framework,
        IEnumerable<string> runtimeIdentifiers,
        ModuleReadyToRunScope scope,
        string version,
        string artifactsRoot,
        IEnumerable<string>? restoreSources)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");
        if (string.IsNullOrWhiteSpace(artifactsRoot))
            throw new ArgumentException("An artifacts root is required for ReadyToRun publishing.", nameof(artifactsRoot));

        projectPath = Path.GetFullPath(projectPath);
        var projDir = Path.GetDirectoryName(projectPath)!;
        var artifacts = Path.GetFullPath(artifactsRoot.Trim().Trim('"'));
        Directory.CreateDirectory(artifacts);

        string? scopeTargetsPath = null;
        if (scope == ModuleReadyToRunScope.ModuleAssemblies)
        {
            scopeTargetsPath = Path.Combine(artifacts, ReadyToRunModuleScopeTargetsFileName);
            File.WriteAllText(scopeTargetsPath, ReadyToRunModuleScopeTargets);
        }

        var maxCpuCountArgument = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/m:1" : "-m:1";
        var existingPathMap = ReadProjectGraphPathMap(projectPath, configuration, framework, artifacts);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtimeIdentifier in runtimeIdentifiers)
        {
            var publishDir = Path.Combine(artifacts, "publish", "readytorun", framework, runtimeIdentifier);
            var args = BuildReadyToRunPublishArguments(
                projectPath,
                version,
                configuration,
                framework,
                runtimeIdentifier,
                artifacts,
                maxCpuCountArgument,
                publishDir,
                restoreSources,
                existingPathMap,
                scopeTargetsPath);
            RunDotnet(projDir, args, $"{framework}, {runtimeIdentifier}, ReadyToRun", "publish");

            if (!Directory.Exists(publishDir))
                throw new DirectoryNotFoundException($"Publish directory not found: {publishDir}");
            result[runtimeIdentifier] = publishDir;
        }

        return result;
    }

    internal static IReadOnlyList<string> BuildReadyToRunPublishArguments(
        string projectPath,
        string version,
        string configuration,
        string framework,
        string runtimeIdentifier,
        string artifacts,
        string maxCpuCountArgument,
        string publishDir,
        IEnumerable<string>? restoreSources,
        string? existingPathMap,
        string? moduleScopeTargetsPath)
    {
        var args = BuildPublishArguments(
                projectPath,
                version,
                configuration,
                framework,
                useIsolatedArtifacts: true,
                artifacts,
                maxCpuCountArgument,
                publishDir,
                restoreSources,
                existingPathMap)
            .Select(static arg => arg.StartsWith("-p:_GlobalPropertiesToRemoveFromProjectReferences=", StringComparison.Ordinal)
                ? "-p:_GlobalPropertiesToRemoveFromProjectReferences=" + ReadyToRunProjectReferenceProperties
                : arg)
            .ToList();

        args.Add("--runtime");
        args.Add(runtimeIdentifier);
        args.Add("--self-contained");
        args.Add("false");
        args.Add("-p:PublishReadyToRun=true");
        // Composite images recompile the shared framework into the module and roughly double its size.
        args.Add("-p:PublishReadyToRunComposite=false");
        args.Add("-p:PublishReadyToRunEmitSymbols=false");
        if (!string.IsNullOrWhiteSpace(moduleScopeTargetsPath))
            args.Add($"-p:CustomAfterMicrosoftCommonTargets={moduleScopeTargetsPath}");
        return args;
    }
}

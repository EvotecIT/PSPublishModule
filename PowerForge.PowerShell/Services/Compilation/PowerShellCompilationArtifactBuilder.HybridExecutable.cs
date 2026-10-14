using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static HybridExecutableBuildPlan PrepareHybridExecutable(
        string workspace,
        string artifactName,
        PowerShellCompilationBuildSpec spec,
        string[] compilationSourcePaths,
        PowerShellCompilationPlan plan,
        PowerShellCompilationDependency[] dependencyPlan,
        PowerShellCompilationCommandProviderContract[] commandProviders,
        string providerProjectReferences)
    {
        var capabilities = PowerShellCompilationBuildSpec.GetCapabilities(spec.Kind, spec.Mode);
        var entry = PowerShellHybridExecutableEntryPlanner.TryPlan(
            spec.SourcePath, compilationSourcePaths, plan, spec.TargetFramework,
            spec.SemanticProfileId, commandProviders);
        var nativeEntry = entry?.EntryPointMethod.NativeFunctionBinding is not null;
        var typed = new PowerShellTypedCompilationTranspiler(commandProviders, spec.SemanticProfileId).TranspileForBinaryModule(
            compilationSourcePaths,
            "PowerForge.Compiled",
            PowerShellCSharpSymbolRenderer.Identifier(artifactName) + "Methods",
            spec.TargetFramework,
            capabilities,
            guardedExecutableEntryPath: spec.SourcePath,
            nativeScriptEntry: nativeEntry);
        typed = PowerShellHybridFunctionCollisionResolver.RouteNameCollisionsToFallback(typed, spec.TargetFramework, spec.SemanticProfileId, capabilities);
        typed = PowerShellBinaryCmdletSourceGenerator.PrepareForBinaryModule(typed, exportedFunctions: null, targetFramework: spec.TargetFramework, semanticProfileId: spec.SemanticProfileId, capabilities: capabilities);
        WriteCompiledPowerShellSource(Path.Combine(workspace, "CompiledPowerShell.cs"), typed.SourceCode,
            spec.SourcePath, compilationSourcePaths);
        WriteBinaryHostRuntime(workspace, typed, requiresExecutableEntryHost: entry is not null,
            requiresNativeEntryHost: nativeEntry);
        if (entry is not null)
            WriteCompiledPowerShellSource(
                Path.Combine(workspace, "CompiledPowerShellEntry.cs"),
                nativeEntry ? PowerShellHybridNativeEntryEmitter.GenerateSource(entry, spec.SourcePath)
                    : PowerShellHybridExecutableEntryEmitter.GenerateSource(entry),
                spec.SourcePath, compilationSourcePaths, includeSyntheticEntry: !nativeEntry);
        File.WriteAllText(
            Path.Combine(workspace, "CompiledCmdlets.cs"),
            PowerShellBinaryCmdletSourceGenerator.Generate(typed, exportedFunctions: null, targetFramework: spec.TargetFramework),
            new UTF8Encoding(false));

        var packagedSources = PreparePackagedSources(workspace, spec.SourcePath, compilationSourcePaths, dependencyPlan, typed,
            nativeScriptEntry: nativeEntry);
        var parameterInitializers = PowerShellPackagedParameterBindingPolicy.Generate(spec.SourcePath, spec.TargetFramework,
            nativeScriptEntry: nativeEntry);
        // The native owner reparses exact authored text. Do not rewrite its variables,
        // insert registrations or replace its body before hashing/native script dispatch.
        var sourceMapping = nativeEntry ? null : new PowerShellPackagedSourceMapping(spec.SourcePath);
        var packagedScript = nativeEntry ? File.ReadAllText(spec.SourcePath)
            : GeneratePackagedScript(spec.SourcePath, packagedSources, typed, sourceMapping);
        if (entry is not null && !nativeEntry)
            packagedScript = PowerShellHybridExecutableEntryEmitter.ComposeScript(packagedScript, spec.SourcePath, entry, sourceMapping);
        sourceMapping?.Write(workspace);
        var packagedScriptPath = Path.Combine(workspace, "Source.ps1");
        if (nativeEntry)
            File.Copy(spec.SourcePath, packagedScriptPath, overwrite: false);
        else
            File.WriteAllText(packagedScriptPath, packagedScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(
            Path.Combine(workspace, "Program.cs"),
            ReadTemplate(PackagedProgramTemplate)
                .Replace("{{ENTRY_INVOCATION}}", PowerShellPackagedEntryInvocationSource.Render(nativeEntry))
                .Replace("{{PIPELINE_INVOCATION}}", PowerShellPackagedEntryInvocationSource.RenderInvoke(nativeEntry))
                .Replace("{{PARAMETERS}}", parameterInitializers.Parameters)
                .Replace("{{SWITCH_PARAMETERS}}", parameterInitializers.SwitchParameters)
                .Replace("{{BOOLEAN_PARAMETERS}}", parameterInitializers.BooleanParameters)
                .Replace("{{PARAMETER_ALIASES}}", parameterInitializers.ParameterAliases)
                .Replace("{{ENTRY_RELATIVE_PATH}}", PowerShellCSharpLiteral.QuoteString(packagedSources.EntryRelativePath))
                .Replace("{{ENTRY_SHA256}}", PowerShellCSharpLiteral.QuoteString(ComputeSha256(packagedScriptPath)))
                .Replace("{{DEPENDENCY_SPECS}}", packagedSources.DependencySpecs)
                .Replace("{{TARGET_FRAMEWORK}}", PowerShellCSharpLiteral.QuoteString(spec.TargetFramework)),
            new UTF8Encoding(false));
        var projectPath = Path.Combine(workspace, artifactName + ".csproj");
        File.WriteAllText(
            projectPath,
            ReadTemplate(PackagedProjectTemplate)
                .Replace("{{TARGET_FRAMEWORK}}", EscapeXml(spec.TargetFramework))
                .Replace("{{ARTIFACT_NAME}}", EscapeXml(artifactName))
                .Replace("{{SINGLE_FILE}}", spec.SingleFile ? "true" : "false")
                .Replace("{{SELF_CONTAINED}}", spec.SelfContained ? "true" : "false")
                .Replace("{{POWERSHELL_SDK_VERSION}}", GetPowerShellSdkVersion(spec.TargetFramework))
                .Replace("{{SECURITY_XML_VERSION}}", GetSecurityXmlVersion(spec.TargetFramework))
                .Replace("{{PROVIDER_REFERENCES}}", providerProjectReferences)
                .Replace("{{DEPENDENCY_RESOURCES}}", packagedSources.ProjectResources),
            new UTF8Encoding(false));
        var compiledMethods = typed.Methods.Where(static method => method.Lifecycle?.Execution != PowerShellCompilationLifecycleExecution.HostedSteppablePipeline)
            .Concat(entry is null ? Array.Empty<PowerShellCompiledMethod>() : new[] { entry.EntryPoint.Method })
            .ToArray();
        PowerShellCommandMetadataBuildSupport.Write(workspace, projectPath, typed, exportedFunctions: null);
        return new HybridExecutableBuildPlan(projectPath, typed, compiledMethods, plan.TotalUnits, packagedSources.NativeMirroredResources);
    }

    private sealed class HybridExecutableBuildPlan
    {
        internal HybridExecutableBuildPlan(
            string projectPath,
            PowerShellTypedCompilationResult typed,
            PowerShellCompiledMethod[] compiledMethods,
            int totalUnits,
            PowerShellCompilationDependency[] nativeMirroredResources)
        {
            ProjectPath = projectPath;
            Typed = typed;
            CompiledMethods = compiledMethods;
            TotalUnits = totalUnits;
            NativeMirroredResources = nativeMirroredResources;
        }

        internal string ProjectPath { get; }
        internal PowerShellTypedCompilationResult Typed { get; }
        internal PowerShellCompiledMethod[] CompiledMethods { get; }
        internal int TotalUnits { get; }
        internal PowerShellCompilationDependency[] NativeMirroredResources { get; }
    }
}

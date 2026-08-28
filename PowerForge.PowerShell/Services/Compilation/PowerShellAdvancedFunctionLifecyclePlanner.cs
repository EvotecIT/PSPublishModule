namespace PowerForge;

/// <summary>Creates explicit hosted lifecycle contracts from canonical front-end sources.</summary>
internal static class PowerShellAdvancedFunctionLifecyclePlanner
{
    internal static bool HasNamedLifecycle(IEnumerable<string> sourcePaths)
        => sourcePaths.Select(PowerShellSourceParser.ParseFile)
            .SelectMany(static document => PowerShellLifecycleSourceBinder.Bind(document, targetFramework: null))
            .Any();

    internal static PowerShellTypedCompilationResult AddHostedLifecycleMethods(
        PowerShellTypedCompilationResult typed,
        string? targetFramework)
    {
        _ = targetFramework;
        var existing = typed.Methods.Select(static method => MethodKey(method.SourcePath, method.SourceName, method.SourceLine))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lifecycleMethods = typed.LifecycleSources
            .Where(source => !existing.Contains(MethodKey(source.SourcePath, source.Name, source.SourceLine)))
            .OrderBy(static source => source.SourcePath, PowerShellCompilationPathSafety.PathComparer)
            .ThenBy(static source => source.SourceLine)
            .Select(CreateMethod)
            .ToArray();
        if (lifecycleMethods.Length == 0) return typed;
        return new PowerShellTypedCompilationResult(
            typed.SourcePath,
            typed.NamespaceName,
            typed.TypeName,
            typed.SourceCode,
            typed.Methods.Concat(lifecycleMethods)
                .OrderBy(static method => method.SourcePath, PowerShellCompilationPathSafety.PathComparer)
                .ThenBy(static method => method.SourceLine)
                .ToArray(),
            typed.Diagnostics,
            typed.SourcePaths,
            typed.LifecycleSources,
            typed.Optimization);
    }

    private static PowerShellCompiledMethod CreateMethod(PowerShellCompilationLifecycleSource source)
    {
        var pipelineNames = source.Parameters
            .Where(static parameter => parameter.Bindings.Any(binding => binding.ValueFromPipeline || binding.ValueFromPipelineByPropertyName))
            .Select(static parameter => parameter.Name)
            .ToArray();
        var binding = source.CommandBinding;
        var method = new PowerShellCompiledMethod(
            source.Name,
            PowerShellCSharpSymbolRenderer.Identifier(source.Name) + "HostedLifecycle",
            typeof(object).FullName!,
            source.Parameters,
            source.SourceLine,
            source.SourcePath,
            requiresPowerShellStreams: true,
            requiresPowerShellCommandRegions: false,
            aliases: source.Aliases,
            requiresPowerShellBoundParameters: true,
            isAdvancedFunction: true,
            commandBinding: binding,
            requiresPowerShellRuntimeState: binding.SupportsShouldProcess,
            declaredOutputType: string.Empty,
            sourceColumn: source.SourceColumn,
            sourceEndLine: source.SourceEndLine,
            sourceEndColumn: source.SourceEndColumn,
            commandProviders: new[] { PowerShellCommandSemanticRegistry.HostedRegionContract("<advanced-lifecycle>") });
        method.Help = source.Help;
        method.HostedLifecycleSource = source.HostedBodySource;
        method.Lifecycle = new PowerShellCompilationLifecycleContract
        {
            SchemaVersion = 2,
            Execution = PowerShellCompilationLifecycleExecution.HostedSteppablePipeline,
            HasBegin = source.HasBegin,
            HasProcess = source.HasProcess,
            HasEnd = source.HasEnd,
            HasClean = source.HasClean,
            MinimumPowerShellVersion = source.MinimumPowerShellVersion,
            PreservesOriginalPipelineRecord = true,
            CleanupGuaranteed = true,
            ValueFromPipeline = source.Parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromPipeline)),
            ValueFromPipelineByPropertyName = source.Parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromPipelineByPropertyName)),
            ValueFromRemainingArguments = source.Parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromRemainingArguments)),
            CommonParameters = binding.IsAdvancedFunction,
            SupportsShouldProcess = binding.SupportsShouldProcess,
            ConfirmImpact = binding.ConfirmImpact,
            SourceSha256 = source.SourceSha256,
            PipelineParameterNames = pipelineNames,
            HostingReason = "Named advanced-function blocks retain PowerShell lifecycle semantics through a generated steppable-pipeline cmdlet; the artifact is Hybrid and is not runtime-free."
        };
        return method;
    }

    private static string MethodKey(string path, string name, int line)
        => Path.GetFullPath(path) + "\0" + name + "\0" + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

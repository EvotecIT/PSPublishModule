using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>Compiles one entry script and its contained dot-source closure through the shared semantic pipeline.</summary>
internal static class PowerShellTypedExecutableCompiler
{
    private const PowerShellCompilationCapability Capabilities = PowerShellCompilationCapabilities.TypedExecutable;

    internal static PowerShellTypedExecutableCompilation Compile(
        string entryPointPath,
        IEnumerable<string> sourcePaths,
        PowerShellCompilationPlan plan,
        string targetFramework,
        string semanticProfileId,
        IEnumerable<PowerShellCompilationCommandProviderContract>? commandProviders = null)
    {
        if (!plan.CanProceed) throw CreatePlanFailure(plan);

        var entryPoint = Path.GetFullPath(entryPointPath);
        var requestedSources = sourcePaths.Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        ValidateSourceClosure(entryPoint, requestedSources);

        var identityRoot = Path.GetDirectoryName(entryPoint) ?? Directory.GetCurrentDirectory();
        var parsed = requestedSources.Select(path => Parse(path, identityRoot))
            .ToDictionary(static source => source.Path, PowerShellCompilationPathSafety.PathComparer);
        if (!parsed.TryGetValue(entryPoint, out var entrySource))
            throw new InvalidOperationException("The typed executable entrypoint is not present in its compilation source closure.");

        var definitions = parsed.Values
            .SelectMany(source => GetTopLevelFunctions(source)
                .Select(function => new LocalDefinition(source.Path, function, GetUnit(plan, source.Path, function.Name))))
            .ToArray();
        ValidateDefinitions(definitions);
        ValidateDependencyTopLevels(parsed.Values, entryPoint);
        ValidateEntryPointDeclarationOrder(entrySource);

        var statements = entrySource.Ast.EndBlock?.Statements
            .Where(static statement => statement is not FunctionDefinitionAst && !IsTopLevelDotSource(statement))
            .ToArray() ?? Array.Empty<StatementAst>();

        var entryDocument = CreateEntryDocument(entrySource, statements, identityRoot);
        var registry = PowerShellCommandSemanticRegistry.Create(commandProviders);
        var semantic = new PowerShellSemanticCompilationPipeline(registry, semanticProfileId).Compile(
            parsed.Values.Select(static source => source.Document).Append(entryDocument.Document),
            targetFramework,
            Capabilities);
        var emissions = semantic.Lowered.Functions
            .Zip(semantic.Emitted.Methods, static (function, emission) => new SemanticEmission(function, emission))
            .ToArray();
        var entry = emissions.SingleOrDefault(item =>
            item.Function.Symbol.DocumentId == entryDocument.Document.DocumentId &&
            item.Function.Symbol.Name.Equals("Invoke", StringComparison.Ordinal));
        if (entry is null) throw CreateSemanticFailure(semantic, "entrypoint");
        entry.Emission.RegionGraph = PowerShellLoweredRegionGraphBuilder.Remap(
            entry.Emission.RegionGraph,
            entrySource.Document.DocumentId,
            entrySource.Document.Text,
            entryDocument.SourceMappings);

        var localMethods = new List<PowerShellCSharpMethodEmission>();
        var descriptions = new List<PowerShellCompiledMethod>();
        foreach (var definition in definitions.OrderBy(static definition => definition.Path, PowerShellCompilationPathSafety.PathComparer)
                     .ThenBy(static definition => definition.Function.Extent.StartOffset))
        {
            var documentId = parsed[definition.Path].Document.DocumentId;
            var emitted = emissions.SingleOrDefault(item =>
                item.Function.Symbol.DocumentId == documentId &&
                item.Function.Symbol.Name.Equals(definition.Function.Name, StringComparison.OrdinalIgnoreCase));
            if (emitted is null) throw CreateSemanticFailure(semantic, $"local function '{definition.Function.Name}'");
            localMethods.Add(emitted.Emission);
            descriptions.Add(CreateMethodDescription(definition.Unit, emitted.Function, emitted.Emission, definition.Path));
        }

        var entryUnit = plan.Files.First(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, entryPoint))
            .Units.Single(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        var entryDescription = CreateMethodDescription(entryUnit, entry.Function, entry.Emission, entryPoint);
        descriptions.Add(entryDescription);
        var reachableCommandProviders = CollectReachableCommandProviders(semantic, entry.Function.Symbol);
        return new PowerShellTypedExecutableCompilation(
            new PowerShellTypedExecutableContract(entryDescription, entry.Function.Parameters
                .Select(static parameter => new PowerShellTypedExecutableParameter(parameter.ClrType, parameter.Contract))
                .ToArray()),
            entry.Emission,
            localMethods.ToArray(),
            descriptions.ToArray(),
            reachableCommandProviders,
            semantic.Optimization.ToPublicModel(),
            PowerShellCompilationIrSnapshotBuilder.Create(semantic));
    }

    private static PowerShellCompilationCommandProviderContract[] CollectReachableCommandProviders(
        PowerShellSemanticCompilationResult semantic,
        PowerShellSymbolId entryPoint)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { entryPoint.StableKey };
        bool changed;
        do
        {
            changed = false;
            foreach (var edge in semantic.Analyzed.CallGraph)
                if (reachable.Contains(edge.Caller.StableKey) && reachable.Add(edge.Callee.StableKey))
                    changed = true;
        } while (changed);

        return semantic.Lowered.Functions
            .Where(function => reachable.Contains(function.Symbol.StableKey))
            .SelectMany(function => PowerShellLoweredCommandProviderCollector.Collect(function.Statements))
            .GroupBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSourceClosure(string entryPoint, string[] requestedSources)
    {
        var reachableSources = PowerShellHybridDependencyResolver.DiscoverDependencies(entryPoint);
        var injected = requestedSources.Except(reachableSources, PowerShellCompilationPathSafety.PathComparer).ToArray();
        var missing = reachableSources.Except(requestedSources, PowerShellCompilationPathSafety.PathComparer).ToArray();
        if (injected.Length == 0 && missing.Length == 0) return;
        var details = injected.Length > 0
            ? $"unreachable source(s): {string.Join(", ", injected.Select(Path.GetFileName))}"
            : $"missing reachable source(s): {string.Join(", ", missing.Select(Path.GetFileName))}";
        throw new InvalidOperationException($"The typed executable compilation source set must exactly match the entrypoint's contained dot-source closure; {details}.");
    }

    private static void ValidateDefinitions(LocalDefinition[] definitions)
    {
        var duplicate = definitions.GroupBy(static definition => definition.Function.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Typed executable local function '{duplicate.Key}' is declared more than once in the source closure.");
        var generatedCollision = definitions.GroupBy(static definition => PowerShellClrSymbolMapper.MapIdentifier(definition.Function.Name), StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (generatedCollision is not null)
            throw new InvalidOperationException($"Typed executable local functions collide after CLR identifier normalization: {string.Join(", ", generatedCollision.Select(static item => item.Function.Name))}.");
        var entryPointCollision = definitions.FirstOrDefault(static definition =>
            PowerShellClrSymbolMapper.MapIdentifier(definition.Function.Name).Equals("Invoke", StringComparison.Ordinal));
        if (entryPointCollision is not null)
            throw new InvalidOperationException($"Typed executable local function '{entryPointCollision.Function.Name}' collides with the reserved generated entry-point method 'Invoke'.");
    }

    private static void ValidateDependencyTopLevels(IEnumerable<ParsedSource> sources, string entryPoint)
    {
        foreach (var source in sources.Where(source => !PowerShellCompilationPathSafety.PathEquals(source.Path, entryPoint)))
        {
            if (source.Ast.ParamBlock is not null)
                throw new InvalidOperationException($"Typed executable dependency '{source.Path}' declares a parameter block whose dot-source binding semantics are not yet supported.");
            var unsupported = source.Ast.EndBlock?.Statements.FirstOrDefault(static statement =>
                statement is not FunctionDefinitionAst && !IsTopLevelDotSource(statement));
            if (unsupported is not null)
                throw new InvalidOperationException($"Typed executable dependency '{source.Path}' contains executable module-scope statement '{unsupported.GetType().Name}'. Dependencies may declare functions and top-level literal dot-source includes only.");
        }
    }

    private static ExecutableEntryDocument CreateEntryDocument(ParsedSource entrySource, IEnumerable<StatementAst> statements, string identityRoot)
    {
        var parameterBlock = PowerShellSourceParser.GetParameterBlockSource(entrySource.Ast.ParamBlock);
        var builder = new StringBuilder().AppendLine("function Invoke {").AppendLine(parameterBlock);
        var mappings = new List<PowerShellRegionSourceRemap>();
        foreach (var statement in statements)
        {
            var startOffset = builder.Length;
            builder.Append(statement.Extent.Text);
            mappings.Add(new PowerShellRegionSourceRemap(
                startOffset,
                builder.Length,
                statement.Extent.StartOffset,
                statement.Extent.EndOffset));
            builder.AppendLine();
        }
        builder.Append('}');
        return new ExecutableEntryDocument(
            PowerShellSourceParser.Parse(builder.ToString(), entrySource.Path + ".powerforge-entry.ps1", identityRoot),
            mappings.ToArray());
    }

    private static InvalidOperationException CreateSemanticFailure(PowerShellSemanticCompilationResult result, string owner)
    {
        var diagnostic = result.Lowered.Diagnostics.FirstOrDefault(item =>
                             item.Code.Equals(PowerShellCompilationFeatureIds.FunctionGraph, StringComparison.Ordinal) ||
                             item.Message.Contains("cycle", StringComparison.OrdinalIgnoreCase)) ??
                         result.Lowered.Diagnostics.FirstOrDefault();
        return new InvalidOperationException(diagnostic is null
            ? $"The shared semantic compiler did not emit the typed executable {owner}."
            : $"The shared semantic compiler could not emit the typed executable {owner}: {diagnostic.Message}");
    }

    private static PowerShellCompiledMethod CreateMethodDescription(
        PowerShellCompilationUnitPlan unit,
        PowerShellLoweredFunction function,
        PowerShellCSharpMethodEmission method,
        string sourcePath)
    {
        var description = new PowerShellCompiledMethod(
            unit.Name,
            method.GeneratedName,
            method.ReturnType.FullName ?? method.ReturnType.Name,
            unit.Parameters,
            method.SourceSpan.StartLine,
            sourcePath,
            requiresPowerShellStreams: method.RequiresPowerShellStreams,
            requiresPowerShellCommandRegions: false,
            aliases: function.Aliases.ToArray(),
            requiresPowerShellBoundParameters: method.RequiresPowerShellBoundParameters,
            isAdvancedFunction: function.CommandBinding.IsAdvancedFunction,
            commandBinding: function.CommandBinding,
            requiresPowerShellRuntimeState: method.RequiresPowerShellRuntimeState,
            declaredOutputType: method.DeclaredOutputTypeName,
            sourceColumn: method.SourceSpan.StartColumn,
            sourceEndLine: method.SourceSpan.EndLine,
            sourceEndColumn: method.SourceSpan.EndColumn,
            sourceMap: method.SourceMap,
            commandProviders: method.CommandProviders,
            outputCardinality: method.OutputCardinality,
            outputValueStates: method.OutputValueStates,
            collectionElementType: method.CollectionElementType,
            outputScalarization: method.OutputScalarization,
            hostedRegionSiteCount: method.HostedRegionSiteCount,
            requiresProviderCancellation: method.RequiresProviderCancellation);
        description.DocumentId = function.Symbol.DocumentId;
        description.DeclaredOutputTypeIsSemanticContract = method.DeclaredOutputType is not null;
        description.RequiresPowerShellModuleState = method.RequiresPowerShellModuleState;
        description.RequiresPowerShellModuleStateRead = method.RequiresPowerShellModuleStateRead;
        description.RequiresPowerShellModuleStateWrite = method.RequiresPowerShellModuleStateWrite;
        description.RequiredPowerShellModuleVariables = method.ModuleStateVariableNames;
        description.PowerShellModuleStateReadSiteCount = method.ModuleStateReadSiteCount;
        description.WrittenPowerShellModuleVariables = method.WrittenModuleStateVariableNames;
        description.PowerShellModuleStateWriteSiteCount = method.ModuleStateWriteSiteCount;
        description.RegionGraph = method.RegionGraph;
        return description;
    }

    private static ParsedSource Parse(string path, string identityRoot)
    {
        var fullPath = Path.GetFullPath(path);
        var document = PowerShellSourceParser.ParseFile(fullPath, identityRoot);
        if (document.Errors.Length > 0)
            throw new InvalidOperationException($"Typed executable source '{fullPath}' could not be parsed.");
        return new ParsedSource(fullPath, document);
    }

    private static IEnumerable<FunctionDefinitionAst> GetTopLevelFunctions(ParsedSource source)
        => source.Ast.EndBlock?.Statements.OfType<FunctionDefinitionAst>() ?? Enumerable.Empty<FunctionDefinitionAst>();

    private static PowerShellCompilationUnitPlan GetUnit(PowerShellCompilationPlan plan, string path, string name)
        => plan.Files.First(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, path))
            .Units.Single(unit => unit.Kind == PowerShellCompilationUnitKind.Function && unit.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static bool IsTopLevelDotSource(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst { InvocationOperator: TokenKind.Dot };

    private static void ValidateEntryPointDeclarationOrder(ParsedSource entrySource)
    {
        var executableStatementSeen = false;
        foreach (var statement in entrySource.Ast.EndBlock?.Statements.AsEnumerable() ?? Enumerable.Empty<StatementAst>())
        {
            if (statement is FunctionDefinitionAst || IsTopLevelDotSource(statement))
            {
                if (executableStatementSeen)
                    throw new InvalidOperationException(
                        $"Typed executable declaration '{statement.Extent.Text}' at {entrySource.Path}:{statement.Extent.StartLineNumber} appears after executable code. Local functions and dot-source includes must execute before the compiled entrypoint body.");
                continue;
            }
            executableStatementSeen = true;
        }
    }

    private static InvalidOperationException CreatePlanFailure(PowerShellCompilationPlan plan)
    {
        var blockers = plan.Files.SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics)))
            .Select(static diagnostic => diagnostic.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new InvalidOperationException(blockers.Length == 0
            ? "Strict typed executable generation requires every source-closure unit to be eligible for direct CLR compilation."
            : $"Strict typed executable generation requires every source-closure unit to be eligible. Blockers: {string.Join(" ", blockers)}");
    }

    private sealed class ParsedSource
    {
        internal ParsedSource(string path, ParsedSourceDocument document) { Path = path; Document = document; }
        internal string Path { get; }
        internal ParsedSourceDocument Document { get; }
        internal ScriptBlockAst Ast => Document.SyntaxRoot;
    }

    private sealed class ExecutableEntryDocument
    {
        internal ExecutableEntryDocument(ParsedSourceDocument document, PowerShellRegionSourceRemap[] sourceMappings)
        {
            Document = document;
            SourceMappings = sourceMappings;
        }

        internal ParsedSourceDocument Document { get; }
        internal PowerShellRegionSourceRemap[] SourceMappings { get; }
    }

    private sealed class LocalDefinition
    {
        internal LocalDefinition(string path, FunctionDefinitionAst function, PowerShellCompilationUnitPlan unit)
        { Path = path; Function = function; Unit = unit; }
        internal string Path { get; }
        internal FunctionDefinitionAst Function { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
    }

    private sealed class SemanticEmission
    {
        internal SemanticEmission(PowerShellLoweredFunction function, PowerShellCSharpMethodEmission emission)
        {
            Function = function;
            Emission = emission;
        }

        internal PowerShellLoweredFunction Function { get; }
        internal PowerShellCSharpMethodEmission Emission { get; }
    }
}

internal sealed class PowerShellTypedExecutableCompilation
{
    internal PowerShellTypedExecutableCompilation(
        PowerShellTypedExecutableContract entryPoint,
        PowerShellCSharpMethodEmission entryPointMethod,
        PowerShellCSharpMethodEmission[] localMethods,
        PowerShellCompiledMethod[] methods,
        PowerShellCompilationCommandProviderContract[] reachableCommandProviders,
        PowerShellCompilationOptimizationEvidence optimization,
        PowerShellCompilationIrSnapshotBundle irSnapshots)
    {
        EntryPoint = entryPoint;
        EntryPointMethod = entryPointMethod;
        LocalMethods = localMethods;
        Methods = methods;
        ReachableCommandProviders = reachableCommandProviders;
        Optimization = optimization;
        IrSnapshots = irSnapshots;
    }

    internal PowerShellTypedExecutableContract EntryPoint { get; }
    internal PowerShellCSharpMethodEmission EntryPointMethod { get; }
    internal PowerShellCSharpMethodEmission[] LocalMethods { get; }
    internal PowerShellCompiledMethod[] Methods { get; }
    internal PowerShellCompilationCommandProviderContract[] ReachableCommandProviders { get; }
    internal PowerShellCompilationOptimizationEvidence Optimization { get; }
    internal PowerShellCompilationIrSnapshotBundle IrSnapshots { get; }
}

internal sealed class PowerShellTypedExecutableContract
{
    internal PowerShellTypedExecutableContract(
        PowerShellCompiledMethod method,
        PowerShellTypedExecutableParameter[] parameters)
    {
        Method = method;
        Parameters = parameters ?? Array.Empty<PowerShellTypedExecutableParameter>();
    }

    internal PowerShellCompiledMethod Method { get; }
    internal PowerShellTypedExecutableParameter[] Parameters { get; }
}

internal sealed class PowerShellTypedExecutableParameter
{
    internal PowerShellTypedExecutableParameter(Type clrType, PowerShellCompilationParameter contract)
    {
        ClrType = clrType;
        Contract = contract;
    }

    internal Type ClrType { get; }
    internal PowerShellCompilationParameter Contract { get; }
}

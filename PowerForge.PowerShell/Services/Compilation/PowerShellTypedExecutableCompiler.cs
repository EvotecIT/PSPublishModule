using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Compiles one entry script and its contained dot-source closure through the shared semantic pipeline.</summary>
internal static class PowerShellTypedExecutableCompiler
{
    private const PowerShellCompilationCapability Capabilities = PowerShellCompilationCapabilities.TypedExecutable;

    internal static PowerShellTypedExecutableCompilation Compile(
        string entryPointPath,
        IEnumerable<string> sourcePaths,
        PowerShellCompilationPlan plan,
        string targetFramework)
    {
        if (!plan.CanProceed) throw CreatePlanFailure(plan);

        var entryPoint = Path.GetFullPath(entryPointPath);
        var requestedSources = sourcePaths.Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        ValidateSourceClosure(entryPoint, requestedSources);

        var parsed = requestedSources.Select(Parse)
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

        var byName = definitions.ToDictionary(static definition => definition.Function.Name, StringComparer.OrdinalIgnoreCase);
        var statements = entrySource.Ast.EndBlock?.Statements
            .Where(static statement => statement is not FunctionDefinitionAst && !IsTopLevelDotSource(statement))
            .ToArray() ?? Array.Empty<StatementAst>();
        ValidateCommands(entrySource.Path, statements, byName);

        var entryDocument = CreateEntryDocument(entrySource, statements);
        var semantic = new PowerShellSemanticCompilationPipeline().Compile(
            parsed.Values.Select(static source => source.Document).Append(entryDocument),
            targetFramework,
            Capabilities);
        var emissions = semantic.Lowered.Functions
            .Zip(semantic.Emitted.Methods, static (function, emission) => new SemanticEmission(function, emission))
            .ToArray();
        var entry = emissions.SingleOrDefault(item =>
            item.Function.Symbol.DocumentId == entryDocument.DocumentId &&
            item.Function.Symbol.Name.Equals("Invoke", StringComparison.Ordinal));
        if (entry is null) throw CreateSemanticFailure(semantic, "entrypoint");

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
            descriptions.Add(CreateMethodDescription(definition.Unit, emitted.Emission, definition.Path));
        }

        var entryUnit = plan.Files.First(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, entryPoint))
            .Units.Single(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        descriptions.Add(CreateMethodDescription(entryUnit, entry.Emission, entryPoint));
        return new PowerShellTypedExecutableCompilation(
            entrySource.Ast,
            entryUnit,
            entry.Emission,
            localMethods.ToArray(),
            descriptions.ToArray());
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

    private static ParsedSourceDocument CreateEntryDocument(ParsedSource entrySource, IEnumerable<StatementAst> statements)
    {
        var parameterBlock = entrySource.Ast.ParamBlock?.Extent.Text ?? string.Empty;
        var body = string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
        var source = $"function Invoke {{{Environment.NewLine}{parameterBlock}{Environment.NewLine}{body}{Environment.NewLine}}}";
        return PowerShellSourceParser.Parse(source, entrySource.Path + ".powerforge-entry.ps1");
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
        PowerShellCSharpMethodEmission method,
        string sourcePath)
        => new(
            unit.Name,
            method.GeneratedName,
            method.ReturnType.FullName ?? method.ReturnType.Name,
            unit.Parameters,
            method.SourceSpan.StartLine,
            sourcePath,
            requiresPowerShellStreams: false,
            requiresPowerShellCommandRegions: false,
            aliases: null,
            requiresPowerShellBoundParameters: method.RequiresPowerShellBoundParameters,
            isAdvancedFunction: false,
            commandBinding: null,
            requiresPowerShellRuntimeState: method.RequiresPowerShellRuntimeState,
            declaredOutputType: method.DeclaredOutputType?.FullName);

    private static void ValidateCommands(
        string path,
        IEnumerable<StatementAst> statements,
        IReadOnlyDictionary<string, LocalDefinition> definitions)
    {
        foreach (var command in statements
                     .SelectMany(static statement => statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false))
                     .Cast<CommandAst>())
        {
            var name = command.GetCommandName();
            if (command.InvocationOperator == TokenKind.Dot || name is null || !definitions.ContainsKey(name))
                throw new InvalidOperationException($"{path}:{command.Extent.StartLineNumber}: command '{name ?? command.Extent.Text}' is not a statically known local function in this Strict executable.");
        }
    }

    private static ParsedSource Parse(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var document = PowerShellSourceParser.ParseFile(fullPath);
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

    private sealed class LocalDefinition
    {
        internal LocalDefinition(string path, FunctionDefinitionAst function, PowerShellCompilationUnitPlan unit)
        { Path = path; Function = function; Unit = unit; }
        internal string Path { get; }
        internal FunctionDefinitionAst Function { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
    }

    private sealed record SemanticEmission(PowerShellLoweredFunction Function, PowerShellCSharpMethodEmission Emission);
}

internal sealed class PowerShellTypedExecutableCompilation
{
    internal PowerShellTypedExecutableCompilation(
        ScriptBlockAst entryPoint,
        PowerShellCompilationUnitPlan entryPointUnit,
        PowerShellCSharpMethodEmission entryPointMethod,
        PowerShellCSharpMethodEmission[] localMethods,
        PowerShellCompiledMethod[] methods)
    {
        EntryPoint = entryPoint;
        EntryPointUnit = entryPointUnit;
        EntryPointMethod = entryPointMethod;
        LocalMethods = localMethods;
        Methods = methods;
    }

    internal ScriptBlockAst EntryPoint { get; }
    internal PowerShellCompilationUnitPlan EntryPointUnit { get; }
    internal PowerShellCSharpMethodEmission EntryPointMethod { get; }
    internal PowerShellCSharpMethodEmission[] LocalMethods { get; }
    internal PowerShellCompiledMethod[] Methods { get; }
}

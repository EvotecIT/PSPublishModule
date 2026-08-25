using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Compiles one entry script and its contained dot-source closure into direct CLR methods.</summary>
internal static class PowerShellTypedExecutableCompiler
{
    internal static PowerShellTypedExecutableCompilation Compile(
        string entryPointPath,
        IEnumerable<string> sourcePaths,
        PowerShellCompilationPlan plan,
        string targetFramework)
    {
        if (!plan.CanProceed)
            throw CreatePlanFailure(plan);

        var entryPoint = Path.GetFullPath(entryPointPath);
        var requestedSources = sourcePaths
            .Select(Path.GetFullPath)
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        var reachableSources = PowerShellHybridDependencyResolver.DiscoverDependencies(entryPoint);
        var injectedSources = requestedSources.Except(reachableSources, PowerShellCompilationPathSafety.PathComparer).ToArray();
        var missingSources = reachableSources.Except(requestedSources, PowerShellCompilationPathSafety.PathComparer).ToArray();
        if (injectedSources.Length > 0 || missingSources.Length > 0)
        {
            var details = injectedSources.Length > 0
                ? $"unreachable source(s): {string.Join(", ", injectedSources.Select(Path.GetFileName))}"
                : $"missing reachable source(s): {string.Join(", ", missingSources.Select(Path.GetFileName))}";
            throw new InvalidOperationException($"The typed executable compilation source set must exactly match the entrypoint's contained dot-source closure; {details}.");
        }

        var parsed = requestedSources
            .Select(Parse)
            .ToDictionary(static source => source.Path, PowerShellCompilationPathSafety.PathComparer);
        if (!parsed.TryGetValue(entryPoint, out var entrySource))
            throw new InvalidOperationException("The typed executable entrypoint is not present in its compilation source closure.");

        var definitions = parsed.Values
            .SelectMany(source => GetTopLevelFunctions(source).Select(function => new LocalDefinition(source.Path, function, GetUnit(plan, source.Path, function.Name))))
            .ToArray();
        var duplicate = definitions.GroupBy(static definition => definition.Function.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Typed executable local function '{duplicate.Key}' is declared more than once in the source closure.");
        var generatedCollision = definitions.GroupBy(static definition => PowerShellCSharpMethodEmitter.SanitizeIdentifier(definition.Function.Name), StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (generatedCollision is not null)
            throw new InvalidOperationException($"Typed executable local functions collide after CLR identifier normalization: {string.Join(", ", generatedCollision.Select(static item => item.Function.Name))}.");
        var entryPointCollision = definitions.FirstOrDefault(static definition =>
            PowerShellCSharpMethodEmitter.SanitizeIdentifier(definition.Function.Name).Equals("Invoke", StringComparison.Ordinal));
        if (entryPointCollision is not null)
            throw new InvalidOperationException($"Typed executable local function '{entryPointCollision.Function.Name}' collides with the reserved generated entry-point method 'Invoke'.");

        foreach (var source in parsed.Values.Where(source => !PowerShellCompilationPathSafety.PathEquals(source.Path, entryPoint)))
        {
            if (source.Ast.ParamBlock is not null)
                throw new InvalidOperationException($"Typed executable dependency '{source.Path}' declares a parameter block whose dot-source binding semantics are not yet supported.");
            var unsupported = source.Ast.EndBlock?.Statements.FirstOrDefault(static statement => statement is not FunctionDefinitionAst && !IsTopLevelDotSource(statement));
            if (unsupported is not null)
                throw new InvalidOperationException($"Typed executable dependency '{source.Path}' contains executable module-scope statement '{unsupported.GetType().Name}'. Dependencies may declare functions and top-level literal dot-source includes only.");
        }

        ValidateEntryPointDeclarationOrder(entrySource);

        var byName = definitions.ToDictionary(static definition => definition.Function.Name, StringComparer.OrdinalIgnoreCase);
        var knownNames = byName.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signatures = new Dictionary<string, PowerShellLocalFunctionSignature>(StringComparer.OrdinalIgnoreCase);
        var methods = new List<PowerShellCSharpMethodEmission>();
        var methodDescriptions = new List<PowerShellCompiledMethod>();
        var states = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        var provisionalSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
            EmitFunction(definition, byName, knownNames, signatures, methods, methodDescriptions, states, provisionalSignatures, targetFramework);

        var statements = entrySource.Ast.EndBlock?.Statements
            .Where(static statement => statement is not FunctionDefinitionAst && !IsTopLevelDotSource(statement))
            .ToArray() ?? Array.Empty<StatementAst>();
        ValidateCommands(entrySource.Path, statements, byName);
        var entryUnit = plan.Files
            .First(file => PowerShellCompilationPathSafety.PathEquals(file.FullPath, entryPoint))
            .Units.Single(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
        var entryMethod = new PowerShellCSharpMethodEmitter(
            entrySource.Path,
            entrySource.Ast,
            "<script>",
            "Invoke",
            statements,
            targetFramework,
            PowerShellCompilationCapability.LocalFunctionCalls |
            PowerShellCompilationCapability.BoundParameters,
            signatures,
            entryUnit.Parameters).Emit();
        methodDescriptions.Add(CreateMethodDescription(entryUnit, entryMethod, entryPoint));
        return new PowerShellTypedExecutableCompilation(entrySource.Ast, entryUnit, entryMethod, methods.ToArray(), methodDescriptions.ToArray());
    }

    private static void EmitFunction(
        LocalDefinition definition,
        IReadOnlyDictionary<string, LocalDefinition> definitions,
        ISet<string> knownNames,
        Dictionary<string, PowerShellLocalFunctionSignature> signatures,
        List<PowerShellCSharpMethodEmission> methods,
        List<PowerShellCompiledMethod> methodDescriptions,
        Dictionary<string, VisitState> states,
        ISet<string> provisionalSignatures,
        string targetFramework)
    {
        var name = definition.Function.Name;
        if (states.TryGetValue(name, out var state))
        {
            if (state == VisitState.Complete) return;
            if (provisionalSignatures.Contains(name)) return;
            throw new InvalidOperationException($"Typed executable local function cycle reaches '{name}'. Mutual or uncontracted recursive calls require PowerShell runtime semantics.");
        }
        const PowerShellCompilationCapability capabilities =
            PowerShellCompilationCapability.LocalFunctionCalls |
            PowerShellCompilationCapability.BoundParameters;
        if (PowerShellRecursiveFunctionPolicy.TryGetDeclaredReturnType(
                definition.Function,
                definition.Unit,
                knownNames,
                targetFramework,
                capabilities,
                out var declaredReturnType) && declaredReturnType is not null)
        {
            signatures[name] = CreateSignature(definition, PowerShellCSharpMethodEmitter.SanitizeIdentifier(name), declaredReturnType, false);
            provisionalSignatures.Add(name);
        }
        states[name] = VisitState.Active;
        var commands = definition.Function.Body.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>().ToArray();
        foreach (var command in commands)
        {
            if (command.InvocationOperator == TokenKind.Dot)
                throw new InvalidOperationException($"{definition.Path}:{command.Extent.StartLineNumber}: dot-sourcing inside a typed local function is not supported; include dependencies at entrypoint scope.");
            var commandName = command.GetCommandName();
            if (commandName is null || !definitions.TryGetValue(commandName, out var dependency))
                throw new InvalidOperationException($"{definition.Path}:{command.Extent.StartLineNumber}: command '{commandName ?? command.Extent.Text}' is not a statically known local function in this Strict executable.");
            EmitFunction(dependency, definitions, knownNames, signatures, methods, methodDescriptions, states, provisionalSignatures, targetFramework);
        }

        var method = new PowerShellCSharpMethodEmitter(
            definition.Path,
            definition.Function,
            targetFramework,
            capabilities,
            signatures,
            definition.Unit.Parameters).Emit();
        if (provisionalSignatures.Contains(name) && signatures[name].ReturnType != method.ReturnType)
            throw new InvalidOperationException(
                $"Declared OutputType '{signatures[name].ReturnType.FullName}' does not match inferred recursive return type '{method.ReturnType.FullName}' for '{name}'.");
        signatures[name] = CreateSignature(definition, method.GeneratedName, method.ReturnType, method.RequiresPowerShellBoundParameters);
        provisionalSignatures.Remove(name);
        methods.Add(method);
        methodDescriptions.Add(CreateMethodDescription(definition.Unit, method, definition.Path));
        states[name] = VisitState.Complete;
    }

    private static PowerShellLocalFunctionSignature CreateSignature(
        LocalDefinition definition,
        string generatedName,
        Type returnType,
        bool requiresPowerShellBoundParameters)
    {
        var parameters = definition.Function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>();
        return new PowerShellLocalFunctionSignature(
            definition.Function.Name,
            generatedName,
            returnType,
            parameters.Select(parameter => CreateParameter(parameter, definition.Unit)).ToArray(),
            PowerShellAdvancedFunctionPolicy.IsAdvanced(definition.Function),
            requiresPowerShellBoundParameters,
            requiresPowerShellStreams: false,
            requiresPowerShellCommandRegions: false,
            commandBinding: PowerShellAdvancedFunctionPolicy.GetBinding(definition.Function.Body.ParamBlock));
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
            unit.StartLine,
            sourcePath,
            requiresPowerShellStreams: false,
            requiresPowerShellCommandRegions: false,
            aliases: null,
            requiresPowerShellBoundParameters: method.RequiresPowerShellBoundParameters);

    private static PowerShellLocalFunctionParameter CreateParameter(ParameterAst parameter, PowerShellCompilationUnitPlan unit)
    {
        var metadata = unit.Parameters.Single(item => item.Name.Equals(parameter.Name.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase));
        var type = parameter.StaticType == typeof(System.Management.Automation.SwitchParameter) ? typeof(bool) : parameter.StaticType;
        return new PowerShellLocalFunctionParameter(
            metadata.Name,
            type,
            metadata.IsMandatory,
            metadata.IsSwitch,
            metadata.Aliases,
            metadata.AllowNull,
            metadata.Validations,
            metadata.Bindings);
    }

    private static void ValidateCommands(string path, IEnumerable<StatementAst> statements, IReadOnlyDictionary<string, LocalDefinition> definitions)
    {
        foreach (var command in statements.SelectMany(static statement => statement.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false)).Cast<CommandAst>())
        {
            var name = command.GetCommandName();
            if (command.InvocationOperator == TokenKind.Dot || name is null || !definitions.ContainsKey(name))
                throw new InvalidOperationException($"{path}:{command.Extent.StartLineNumber}: command '{name ?? command.Extent.Text}' is not a statically known local function in this Strict executable.");
        }
    }

    private static ParsedSource Parse(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var ast = Parser.ParseFile(fullPath, out _, out ParseError[] errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Typed executable source '{fullPath}' could not be parsed.");
        return new ParsedSource(fullPath, ast);
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
        var statements = entrySource.Ast.EndBlock?.Statements.AsEnumerable() ?? Enumerable.Empty<StatementAst>();
        foreach (var statement in statements)
        {
            if (statement is FunctionDefinitionAst || IsTopLevelDotSource(statement))
            {
                if (executableStatementSeen)
                {
                    throw new InvalidOperationException(
                        $"Typed executable declaration '{statement.Extent.Text}' at {entrySource.Path}:{statement.Extent.StartLineNumber} appears after executable code. Local functions and dot-source includes must execute before the compiled entrypoint body.");
                }
                continue;
            }
            executableStatementSeen = true;
        }
    }

    private static InvalidOperationException CreatePlanFailure(PowerShellCompilationPlan plan)
    {
        var blocker = plan.Files.SelectMany(static file => file.Diagnostics.Concat(file.Units.SelectMany(static unit => unit.Diagnostics))).FirstOrDefault();
        return new InvalidOperationException(blocker is null
            ? "Strict typed executable generation requires every source-closure unit to be eligible for direct CLR compilation."
            : $"Strict typed executable generation requires every source-closure unit to be eligible. First blocker: {blocker.Message}");
    }

    private sealed class ParsedSource
    {
        internal ParsedSource(string path, ScriptBlockAst ast) { Path = path; Ast = ast; }
        internal string Path { get; }
        internal ScriptBlockAst Ast { get; }
    }

    private sealed class LocalDefinition
    {
        internal LocalDefinition(string path, FunctionDefinitionAst function, PowerShellCompilationUnitPlan unit)
        { Path = path; Function = function; Unit = unit; }
        internal string Path { get; }
        internal FunctionDefinitionAst Function { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
    }
    private enum VisitState { Active, Complete }
}

internal sealed class PowerShellTypedExecutableCompilation
{
    internal PowerShellTypedExecutableCompilation(ScriptBlockAst entryPoint, PowerShellCompilationUnitPlan entryPointUnit, PowerShellCSharpMethodEmission entryPointMethod, PowerShellCSharpMethodEmission[] localMethods, PowerShellCompiledMethod[] methods)
    { EntryPoint = entryPoint; EntryPointUnit = entryPointUnit; EntryPointMethod = entryPointMethod; LocalMethods = localMethods; Methods = methods; }
    internal ScriptBlockAst EntryPoint { get; }
    internal PowerShellCompilationUnitPlan EntryPointUnit { get; }
    internal PowerShellCSharpMethodEmission EntryPointMethod { get; }
    internal PowerShellCSharpMethodEmission[] LocalMethods { get; }
    internal PowerShellCompiledMethod[] Methods { get; }
}

internal sealed class PowerShellLocalFunctionSignature
{
    internal PowerShellLocalFunctionSignature(
        string sourceName,
        string generatedName,
        Type returnType,
        PowerShellLocalFunctionParameter[] parameters,
        bool isAdvancedFunction,
        bool requiresPowerShellBoundParameters = false,
        bool requiresPowerShellStreams = false,
        bool requiresPowerShellCommandRegions = false,
        PowerShellCompilationCommandBinding? commandBinding = null)
    {
        SourceName = sourceName;
        GeneratedName = generatedName;
        ReturnType = returnType;
        Parameters = parameters;
        IsAdvancedFunction = isAdvancedFunction;
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        CommandBinding = commandBinding ?? new PowerShellCompilationCommandBinding(isAdvancedFunction);
    }
    internal string SourceName { get; }
    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal PowerShellLocalFunctionParameter[] Parameters { get; }
    internal bool IsAdvancedFunction { get; }
    internal bool RequiresPowerShellBoundParameters { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal PowerShellCompilationCommandBinding CommandBinding { get; }
}

internal sealed class PowerShellLocalFunctionParameter
{
    internal PowerShellLocalFunctionParameter(
        string name,
        Type type,
        bool isMandatory,
        bool isSwitch,
        string[] aliases,
        bool allowNull,
        PowerShellCompilationValidation[] validations,
        PowerShellCompilationParameterBinding[]? bindings = null)
    {
        Name = name;
        Type = type;
        IsMandatory = isMandatory;
        IsSwitch = isSwitch;
        Aliases = aliases;
        AllowNull = allowNull;
        Validations = validations;
        Bindings = bindings ?? new[] { new PowerShellCompilationParameterBinding(mandatory: isMandatory) };
    }
    internal string Name { get; }
    internal Type Type { get; }
    internal bool IsMandatory { get; }
    internal bool IsSwitch { get; }
    internal string[] Aliases { get; }
    internal bool AllowNull { get; }
    internal PowerShellCompilationValidation[] Validations { get; }
    internal PowerShellCompilationParameterBinding[] Bindings { get; }
}

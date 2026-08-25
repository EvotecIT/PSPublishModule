using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>
/// Translates structurally eligible PowerShell functions into readable typed C# source.
/// </summary>
public sealed class PowerShellTypedCompilationTranspiler
{
    private const string TemplateResourceName = "PowerForge.PowerShell.Compilation.TypedLibrary.cs.template";

    /// <summary>Translates all eligible functions in one PowerShell source file.</summary>
    public PowerShellTypedCompilationResult Transpile(
        string sourcePath,
        string namespaceName = "PowerForge.Compiled",
        string typeName = "CompiledPowerShell",
        string? targetFramework = null)
        => TranspileCore(new[] { sourcePath }, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapabilities.StaticRuntimeFacts);

    /// <summary>Translates eligible functions from files sharing one PowerShell module scope.</summary>
    public PowerShellTypedCompilationResult Transpile(
        IEnumerable<string> sourcePaths,
        string namespaceName = "PowerForge.Compiled",
        string typeName = "CompiledPowerShell",
        string? targetFramework = null)
        => TranspileCore(sourcePaths, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapabilities.StaticRuntimeFacts);

    internal PowerShellTypedCompilationResult TranspileForBinaryModule(
        IEnumerable<string> sourcePaths,
        string namespaceName,
        string typeName,
        string? targetFramework)
        => TranspileCore(
            sourcePaths,
            namespaceName,
            typeName,
            targetFramework,
            excludedMethods: null,
            PowerShellCompilationCapabilities.BinaryModule);

    internal PowerShellTypedCompilationResult TranspileExcluding(
        IEnumerable<string> sourcePaths,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string> excludedMethods,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
        => TranspileCore(sourcePaths, namespaceName, typeName, targetFramework, excludedMethods, capabilities);

    private static PowerShellTypedCompilationResult TranspileCore(
        IEnumerable<string> sourcePaths,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string>? excludedMethods,
        PowerShellCompilationCapability capabilities)
    {
        if (sourcePaths is null)
            throw new ArgumentNullException(nameof(sourcePaths));
        if (string.IsNullOrWhiteSpace(namespaceName))
            throw new ArgumentException("A generated namespace is required.", nameof(namespaceName));
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A generated type name is required.", nameof(typeName));

        var fullPaths = sourcePaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .ToArray();
        if (fullPaths.Length == 0)
            throw new ArgumentException("At least one PowerShell source path is required.", nameof(sourcePaths));

        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var parsedFiles = new List<ParsedSource>();
        var basePath = Path.GetDirectoryName(fullPaths[0]) ?? Directory.GetCurrentDirectory();
        var combinedPlan = new PowerShellCompilationAnalyzer().AnalyzeFiles(
            PowerShellCompilationMode.Analyze,
            fullPaths,
            basePath,
            targetFramework,
            capabilities);
        foreach (var fullPath in fullPaths)
        {
            var filePlan = combinedPlan.Files.Single(file =>
                PowerShellCompilationPathSafety.PathEquals(file.FullPath, fullPath));
            diagnostics.AddRange(filePlan.Diagnostics);
            diagnostics.AddRange(filePlan.Units.SelectMany(static unit => unit.Diagnostics));

            Token[] tokens;
            ParseError[] parseErrors;
            var ast = Parser.ParseFile(fullPath, out tokens, out parseErrors);
            if (parseErrors.Length == 0)
                parsedFiles.Add(new ParsedSource(fullPath, ast, filePlan));
        }
        if (parsedFiles.Count != fullPaths.Length)
            return CreateResult(fullPaths, namespaceName, typeName, Array.Empty<PowerShellCompiledMethod>(), Array.Empty<string>(), diagnostics);
        typeName = ResolveCollisionFreeTypeName(typeName, parsedFiles.Select(static file => file.Ast));

        var duplicateFunctions = parsedFiles
            .SelectMany(file => file.Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(function => new { file.Path, Function = function }))
            .GroupBy(static item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(group => group.Select(item =>
            {
                diagnostics.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Function '{item.Function.Name}' is declared more than once and has multiple retained definitions across the resolved module source scope.",
                    item.Path,
                    item.Function.Extent.StartLineNumber,
                    item.Function.Extent.StartColumnNumber));
                return GetMethodKey(item.Path, item.Function.Name, item.Function.Body.Extent.StartLineNumber);
            }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declaredFunctionNames = parsedFiles
            .SelectMany(parsed => parsed.Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(static function => function.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var functionSources = parsedFiles
            .SelectMany(parsed => parsed.Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .OrderBy(static function => function.Extent.StartOffset)
                .Select(function => new
                {
                    Parsed = parsed,
                    Function = function,
                    Unit = parsed.Plan.Units.FirstOrDefault(unit =>
                        unit.Kind == PowerShellCompilationUnitKind.Function &&
                        unit.Name.Equals(function.Name, StringComparison.OrdinalIgnoreCase) &&
                        unit.StartLine == function.Body.Extent.StartLineNumber)
                })
                .Where(static source => source.Unit is not null)
                .Select(static source => new FunctionSource(source.Parsed, source.Function, source.Unit!)))
            .ToArray();

        var collidingFunctions = functionSources
            .Where(source => source.Unit.IsCompilable &&
                             !duplicateFunctions.Contains(GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber)))
            .GroupBy(static source =>
                PowerShellCSharpMethodEmitter.SanitizeIdentifier(source.Function.Name) + "\0" +
                string.Join("\0", source.Unit.Parameters.Select(static parameter => parameter.TypeName)),
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(group => group.Select(source =>
            {
                diagnostics.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Function '{source.Function.Name}' collides with another generated CLR method signature '{PowerShellCSharpMethodEmitter.SanitizeIdentifier(source.Function.Name)}'.",
                    source.Parsed.Path,
                    source.Function.Extent.StartLineNumber,
                    source.Function.Extent.StartColumnNumber));
                return GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Extent.StartLineNumber);
            }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var methods = new List<PowerShellCompiledMethod>();
        var methodSources = new List<string>();
        if (capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls))
        {
            EmitFunctionGraph(
                functionSources,
                declaredFunctionNames,
                duplicateFunctions,
                collidingFunctions,
                excludedMethods,
                targetFramework,
                capabilities,
                methods,
                methodSources,
                diagnostics);
        }
        else
        {
            foreach (var source in functionSources)
            {
                var key = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Extent.StartLineNumber);
                var duplicateKey = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
                if (!source.Unit.IsCompilable || duplicateFunctions.Contains(duplicateKey) || collidingFunctions.Contains(key) || excludedMethods?.Contains(key) == true)
                    continue;
                TryEmitIndependent(source, targetFramework, capabilities, methods, methodSources, diagnostics);
            }
        }

        var collidingMethodIndexes = methods
            .Select((method, index) => new
            {
                Method = method,
                Index = index,
                Signature = method.GeneratedName + "\0" + string.Join("\0", method.Parameters.Select(static parameter => parameter.TypeName))
            })
            .GroupBy(static item => item.Signature, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(group => group.Select(item =>
            {
                diagnostics.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Function '{item.Method.SourceName}' collides with another generated CLR method signature '{item.Method.GeneratedName}'.",
                    item.Method.SourcePath,
                    item.Method.SourceLine,
                    1));
                return item.Index;
            }))
            .OrderByDescending(static index => index)
            .ToArray();
        foreach (var index in collidingMethodIndexes)
        {
            methods.RemoveAt(index);
            methodSources.RemoveAt(index);
        }

        return CreateResult(fullPaths, namespaceName, typeName, methods.ToArray(), methodSources.ToArray(), diagnostics);
    }

    private static void EmitFunctionGraph(
        IReadOnlyList<FunctionSource> sources,
        ISet<string> knownNames,
        ISet<string> duplicateFunctions,
        ISet<string> collidingFunctions,
        ISet<string>? excludedMethods,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        List<PowerShellCompiledMethod> methods,
        List<string> methodSources,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        var definitions = sources
            .GroupBy(static source => source.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var candidates = definitions.Values
            .Where(source =>
            {
                var key = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Extent.StartLineNumber);
                var duplicateKey = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
                return source.Unit.IsCompilable &&
                       !duplicateFunctions.Contains(duplicateKey) &&
                       !collidingFunctions.Contains(key) &&
                       excludedMethods?.Contains(key) != true;
            })
            .ToDictionary(static source => source.Function.Name, StringComparer.OrdinalIgnoreCase);
        var signatures = new Dictionary<string, PowerShellLocalFunctionSignature>(StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, FunctionVisitState>(StringComparer.OrdinalIgnoreCase);
        var provisionalSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var traversal = new List<string>();
        var recursiveCycleFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in candidates.Values.OrderBy(static source => source.Parsed.Path, PowerShellCompilationPathSafety.PathComparer).ThenBy(static source => source.Function.Extent.StartOffset))
        {
            TryEmitGraphFunction(
                source,
                knownNames,
                candidates,
                signatures,
                states,
                provisionalSignatures,
                traversal,
                recursiveCycleFunctions,
                targetFramework,
                capabilities,
                methods,
                methodSources,
                diagnostics);
        }
    }

    private static bool TryEmitGraphFunction(
        FunctionSource source,
        ISet<string> knownNames,
        IReadOnlyDictionary<string, FunctionSource> candidates,
        Dictionary<string, PowerShellLocalFunctionSignature> signatures,
        Dictionary<string, FunctionVisitState> states,
        ISet<string> provisionalSignatures,
        List<string> traversal,
        ISet<string> recursiveCycleFunctions,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        List<PowerShellCompiledMethod> methods,
        List<string> methodSources,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        var name = source.Function.Name;
        if (states.TryGetValue(name, out var state))
        {
            if (state == FunctionVisitState.Complete) return true;
            if (state == FunctionVisitState.Failed) return false;
            if (provisionalSignatures.Contains(name)) return true;
            var cycleStart = traversal.FindIndex(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (cycleStart >= 0)
            {
                foreach (var participant in traversal.Skip(cycleStart)) recursiveCycleFunctions.Add(participant);
            }
            AddRecursiveCycleDiagnostic(source, diagnostics);
            states[name] = FunctionVisitState.Failed;
            return false;
        }

        if (PowerShellRecursiveFunctionPolicy.TryGetDeclaredReturnType(
                source.Function,
                source.Unit,
                knownNames,
                targetFramework,
                capabilities,
                out var declaredReturnType) && declaredReturnType is not null)
        {
            signatures[name] = CreateProvisionalSignature(source, declaredReturnType, targetFramework, capabilities);
            provisionalSignatures.Add(name);
        }
        states[name] = FunctionVisitState.Active;
        traversal.Add(name);
        foreach (var command in source.Function.Body.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>())
        {
            var dependencyName = command.GetCommandName();
            if (dependencyName is null || !knownNames.Contains(dependencyName))
                continue;
            if (command.InvocationOperator == TokenKind.Unknown && candidates.TryGetValue(dependencyName, out var dependency))
            {
                if (TryEmitGraphFunction(dependency, knownNames, candidates, signatures, states, provisionalSignatures,
                        traversal, recursiveCycleFunctions, targetFramework, capabilities, methods, methodSources, diagnostics))
                    continue;
                if (recursiveCycleFunctions.Contains(name))
                {
                    AddRecursiveCycleDiagnostic(source, diagnostics);
                    states[name] = FunctionVisitState.Failed;
                    traversal.RemoveAt(traversal.Count - 1);
                    return false;
                }
            }
            if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams))
                continue;
            else
            {
                diagnostics.Add(CreateDiagnostic(
                    source,
                    command,
                    $"Function '{name}' depends on local function '{dependencyName}', which is not eligible for the same typed function graph."));
                states[name] = FunctionVisitState.Failed;
                traversal.RemoveAt(traversal.Count - 1);
                return false;
            }
        }

        if (states.TryGetValue(name, out state) && state == FunctionVisitState.Failed)
        {
            traversal.RemoveAt(traversal.Count - 1);
            return false;
        }

        try
        {
            var emitted = new PowerShellCSharpMethodEmitter(
                source.Parsed.Path,
                source.Function,
                targetFramework,
                capabilities,
                signatures,
                source.Unit.Parameters).Emit();
            if (provisionalSignatures.Contains(name) && signatures[name].ReturnType != emitted.ReturnType)
                throw new PowerShellCSharpEmissionException(
                    source.Function,
                    $"Declared OutputType '{signatures[name].ReturnType.FullName}' does not match inferred recursive return type '{emitted.ReturnType.FullName}'.");
            signatures[name] = CreateSignature(source, emitted, targetFramework, capabilities);
            provisionalSignatures.Remove(name);
            methodSources.Add(emitted.Source);
            methods.Add(CreateCompiledMethod(source, emitted));
            states[name] = FunctionVisitState.Complete;
            traversal.RemoveAt(traversal.Count - 1);
            return true;
        }
        catch (PowerShellCSharpEmissionException ex)
        {
            if (provisionalSignatures.Remove(name)) signatures.Remove(name);
            diagnostics.Add(CreateDiagnostic(source, ex.Node, ex.Message));
            states[name] = FunctionVisitState.Failed;
            traversal.RemoveAt(traversal.Count - 1);
            return false;
        }
    }

    private static void AddRecursiveCycleDiagnostic(
        FunctionSource source,
        ICollection<PowerShellCompilationDiagnostic> diagnostics)
    {
        if (diagnostics.Any(diagnostic =>
                diagnostic.FeatureId == PowerShellCompilationFeatureIds.FunctionGraph &&
                PowerShellCompilationPathSafety.PathEquals(diagnostic.FilePath, source.Parsed.Path) &&
                diagnostic.Line == source.Function.Extent.StartLineNumber &&
                diagnostic.Column == source.Function.Extent.StartColumnNumber))
            return;
        diagnostics.Add(new PowerShellCompilationDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            $"Function '{source.Function.Name}' participates in a recursive local-call cycle and remains in PowerShell fallback.",
            source.Parsed.Path,
            source.Function.Extent.StartLineNumber,
            source.Function.Extent.StartColumnNumber,
            PowerShellCompilationFeatureIds.FunctionGraph));
    }

    private static void TryEmitIndependent(
        FunctionSource source,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        List<PowerShellCompiledMethod> methods,
        List<string> methodSources,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        try
        {
            var emitted = new PowerShellCSharpMethodEmitter(
                source.Parsed.Path,
                source.Function,
                targetFramework,
                capabilities,
                parameterMetadata: source.Unit.Parameters).Emit();
            methodSources.Add(emitted.Source);
            methods.Add(CreateCompiledMethod(source, emitted));
        }
        catch (PowerShellCSharpEmissionException ex)
        {
            diagnostics.Add(CreateDiagnostic(source, ex.Node, ex.Message));
        }
    }

    private static PowerShellCompiledMethod CreateCompiledMethod(FunctionSource source, PowerShellCSharpMethodEmission emitted)
        => new(
            source.Function.Name,
            emitted.GeneratedName,
            emitted.ReturnType.FullName ?? emitted.ReturnType.Name,
            source.Unit.Parameters,
            source.Function.Extent.StartLineNumber,
            source.Parsed.Path,
            emitted.RequiresPowerShellStreams,
            emitted.RequiresPowerShellCommandRegions,
            GetFunctionAliases(source.Function),
            emitted.RequiresPowerShellBoundParameters,
            PowerShellAdvancedFunctionPolicy.IsAdvanced(source.Function),
            PowerShellAdvancedFunctionPolicy.GetBinding(source.Function.Body.ParamBlock),
            emitted.RequiresPowerShellRuntimeState,
            emitted.DeclaredOutputType?.FullName ?? string.Empty);

    private static PowerShellLocalFunctionSignature CreateSignature(
        FunctionSource source,
        PowerShellCSharpMethodEmission emitted,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => CreateSignature(
            source,
            emitted.GeneratedName,
            emitted.ReturnType,
            emitted.RequiresPowerShellBoundParameters,
            emitted.RequiresPowerShellStreams,
            emitted.RequiresPowerShellCommandRegions,
            emitted.RequiresPowerShellRuntimeState,
            PowerShellRuntimeStateIntrinsicPolicy.RequiresShouldProcessHostBinding(source.Function.Body, targetFramework, capabilities));

    private static PowerShellLocalFunctionSignature CreateProvisionalSignature(
        FunctionSource source,
        Type returnType,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => CreateSignature(
            source,
            PowerShellCSharpMethodEmitter.SanitizeIdentifier(source.Function.Name),
            returnType,
            requiresPowerShellBoundParameters: false,
            requiresPowerShellStreams: false,
            requiresPowerShellCommandRegions: false,
            requiresPowerShellRuntimeState: capabilities.HasFlag(PowerShellCompilationCapability.RuntimeStateIntrinsics) &&
                PowerShellRuntimeStateIntrinsicPolicy.RequiresHostBinding(
                    source.Function.Body.EndBlock?.Statements.AsEnumerable() ?? Enumerable.Empty<StatementAst>(),
                    source.Function.Body,
                    targetFramework,
                    capabilities),
            requiresPowerShellShouldProcess: PowerShellRuntimeStateIntrinsicPolicy.RequiresShouldProcessHostBinding(
                source.Function.Body,
                targetFramework,
                capabilities));

    private static PowerShellLocalFunctionSignature CreateSignature(
        FunctionSource source,
        string generatedName,
        Type returnType,
        bool requiresPowerShellBoundParameters,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        bool requiresPowerShellRuntimeState,
        bool requiresPowerShellShouldProcess)
        => new(
            source.Function.Name,
            generatedName,
            returnType,
            (source.Function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>())
                .Select(parameter =>
                {
                    var metadata = source.Unit.Parameters.Single(item => item.Name.Equals(parameter.Name.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase));
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
                })
                .ToArray(),
            PowerShellAdvancedFunctionPolicy.IsAdvanced(source.Function),
            requiresPowerShellBoundParameters,
            requiresPowerShellStreams,
            requiresPowerShellCommandRegions,
            PowerShellAdvancedFunctionPolicy.GetBinding(source.Function.Body.ParamBlock),
            requiresPowerShellRuntimeState,
            requiresPowerShellShouldProcess);

    private static PowerShellCompilationDiagnostic CreateDiagnostic(FunctionSource source, Ast node, string message)
        => new(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            message,
            source.Parsed.Path,
            node.Extent.StartLineNumber,
            node.Extent.StartColumnNumber);

    private static PowerShellTypedCompilationResult CreateResult(
        string[] sourcePaths,
        string namespaceName,
        string typeName,
        PowerShellCompiledMethod[] methods,
        string[] methodSources,
        IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
    {
        var template = ReadTemplate();
        var source = template
            .Replace("{{NAMESPACE}}", SanitizeQualifiedName(namespaceName))
            .Replace("{{TYPE_NAME}}", PowerShellCSharpMethodEmitter.SanitizeIdentifier(typeName))
            .Replace("{{METHODS}}", string.Join(Environment.NewLine + Environment.NewLine, methodSources));
        return new PowerShellTypedCompilationResult(
            sourcePaths[0],
            SanitizeQualifiedName(namespaceName),
            PowerShellCSharpMethodEmitter.SanitizeIdentifier(typeName),
            source,
            methods,
            diagnostics
                .GroupBy(static diagnostic => new { diagnostic.FilePath, diagnostic.Code, diagnostic.Line, diagnostic.Column, diagnostic.Message })
                .Select(static group => group.First())
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            sourcePaths);
    }

    private static string ReadTemplate()
    {
        using var stream = typeof(PowerShellTypedCompilationTranspiler).Assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded compilation template '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string SanitizeQualifiedName(string value)
        => string.Join(".", value.Split('.').Select(PowerShellCSharpMethodEmitter.SanitizeIdentifier));

    private static string GetMethodKey(string sourcePath, string name, int sourceLine)
        => Path.GetFullPath(sourcePath) + "\0" + name + "\0" + sourceLine;

    private static string[] GetFunctionAliases(FunctionDefinitionAst function)
        => function.Body.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute =>
                attribute.TypeName.Name.Equals("Alias", StringComparison.OrdinalIgnoreCase) ||
                attribute.TypeName.Name.Equals("AliasAttribute", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static attribute => attribute.PositionalArguments.OfType<StringConstantExpressionAst>())
            .Select(static alias => alias.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

    private static string ResolveCollisionFreeTypeName(string requestedTypeName, IEnumerable<ScriptBlockAst> asts)
    {
        var generatedMethods = asts.SelectMany(ast => ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>())
            .Select(static function => PowerShellCSharpMethodEmitter.SanitizeIdentifier(function.Name))
            .ToHashSet(StringComparer.Ordinal);
        var candidate = PowerShellCSharpMethodEmitter.SanitizeIdentifier(requestedTypeName);
        while (generatedMethods.Contains(candidate))
            candidate = PowerShellCSharpMethodEmitter.SanitizeIdentifier("_" + candidate.TrimStart('@'));
        return candidate;
    }

    private sealed class FunctionSource
    {
        internal FunctionSource(ParsedSource parsed, FunctionDefinitionAst function, PowerShellCompilationUnitPlan unit)
        {
            Parsed = parsed;
            Function = function;
            Unit = unit;
        }

        internal ParsedSource Parsed { get; }
        internal FunctionDefinitionAst Function { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
    }

    private enum FunctionVisitState
    {
        Active,
        Complete,
        Failed
    }

    private sealed class ParsedSource
    {
        internal ParsedSource(string path, ScriptBlockAst ast, PowerShellCompilationFilePlan plan)
        {
            Path = path;
            Ast = ast;
            Plan = plan;
        }

        internal string Path { get; }
        internal ScriptBlockAst Ast { get; }
        internal PowerShellCompilationFilePlan Plan { get; }
    }
}

internal sealed class PowerShellCSharpEmissionException : Exception
{
    internal PowerShellCSharpEmissionException(Ast node, string message) : base(message)
    {
        Node = node;
    }

    internal Ast Node { get; }
}

internal sealed class PowerShellCSharpMethodEmission
{
    internal PowerShellCSharpMethodEmission(
        string generatedName,
        Type returnType,
        string source,
        bool requiresPowerShellStreams = false,
        bool requiresPowerShellCommandRegions = false,
        bool requiresPowerShellBoundParameters = false,
        bool requiresPowerShellRuntimeState = false,
        Type? declaredOutputType = null)
    {
        GeneratedName = generatedName;
        ReturnType = returnType;
        Source = source;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
        DeclaredOutputType = declaredOutputType;
    }

    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal string Source { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal bool RequiresPowerShellBoundParameters { get; }
    internal bool RequiresPowerShellRuntimeState { get; }
    internal Type? DeclaredOutputType { get; }
}

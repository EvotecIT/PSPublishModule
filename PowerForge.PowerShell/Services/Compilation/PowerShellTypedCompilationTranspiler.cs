using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>
/// Translates structurally eligible PowerShell functions into readable typed C# source.
/// </summary>
public sealed class PowerShellTypedCompilationTranspiler
{
    private const string TemplateResourceName = "PowerForge.PowerShell.Compilation.TypedLibrary.cs.template";
    private readonly PowerShellCommandSemanticRegistry _commandRegistry;
    private readonly string _semanticProfileId;

    /// <summary>Creates a transpiler with the built-in deterministic command providers.</summary>
    public PowerShellTypedCompilationTranspiler()
        : this(Array.Empty<PowerShellCompilationCommandProviderContract>())
    {
    }

    /// <summary>Creates a transpiler with additional compile-time-only command providers.</summary>
    public PowerShellTypedCompilationTranspiler(IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders)
        : this(commandProviders, PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
    }

    /// <summary>Creates a transpiler with additional providers under one exact named semantic profile.</summary>
    public PowerShellTypedCompilationTranspiler(IEnumerable<PowerShellCompilationCommandProviderContract> commandProviders, string semanticProfileId)
    {
        _commandRegistry = PowerShellCommandSemanticRegistry.Create(commandProviders);
        _semanticProfileId = PowerShellCompilationSemanticOracleCatalog.Get(semanticProfileId).ProfileId;
    }

    /// <summary>Translates all eligible functions in one PowerShell source file.</summary>
    public PowerShellTypedCompilationResult Transpile(
        string sourcePath,
        string namespaceName = "PowerForge.Compiled",
        string typeName = "CompiledPowerShell",
        string? targetFramework = null)
        => TranspileCore(new[] { sourcePath }, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapabilities.StaticRuntimeFacts, _commandRegistry, _semanticProfileId);

    /// <summary>Translates eligible functions from files sharing one PowerShell module scope.</summary>
    public PowerShellTypedCompilationResult Transpile(
        IEnumerable<string> sourcePaths,
        string namespaceName = "PowerForge.Compiled",
        string typeName = "CompiledPowerShell",
        string? targetFramework = null)
        => TranspileCore(sourcePaths, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapabilities.StaticRuntimeFacts, _commandRegistry, _semanticProfileId);

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
            PowerShellCompilationCapabilities.BinaryModule,
            _commandRegistry,
            _semanticProfileId);

    internal PowerShellTypedCompilationResult TranspileExcluding(
        IEnumerable<string> sourcePaths,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string> excludedMethods,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
        => TranspileCore(sourcePaths, namespaceName, typeName, targetFramework, excludedMethods, capabilities, _commandRegistry, _semanticProfileId);

    private static PowerShellTypedCompilationResult TranspileCore(
        IEnumerable<string> sourcePaths,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string>? excludedMethods,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticRegistry commandRegistry,
        string semanticProfileId)
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
        var combinedPlan = new PowerShellCompilationAnalyzer(commandRegistry, semanticProfileId).AnalyzeFiles(
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
                parsedFiles.Add(new ParsedSource(fullPath, ast, filePlan, PowerShellSourceParser.ParseFile(fullPath, basePath)));
        }
        if (parsedFiles.Count != fullPaths.Length)
            return CreateResult(fullPaths, namespaceName, typeName, Array.Empty<PowerShellCompiledMethod>(), Array.Empty<string>(), diagnostics, parsedFiles, targetFramework);
        var boundResult = CreateBoundEmissionIndex(parsedFiles, targetFramework, capabilities, diagnostics, commandRegistry, semanticProfileId);
        var boundEmissions = boundResult.Emissions;
        typeName = ResolveCollisionFreeTypeName(typeName, parsedFiles.Select(static file => file.Ast));

        var duplicateFunctions = parsedFiles
            .SelectMany(file => file.Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Select(function => new { file.Path, Function = function }))
            .GroupBy(static item => item.Function.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(group => group.Select(item =>
            {
                if (!diagnostics.Any(diagnostic =>
                        PowerShellCompilationPathSafety.PathEquals(diagnostic.FilePath, item.Path) &&
                        diagnostic.Message.Contains("multiple retained definitions", StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostics.Add(new PowerShellCompilationDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        $"Function '{item.Function.Name}' has multiple retained definitions across the resolved module source scope.",
                        item.Path,
                        item.Function.Extent.StartLineNumber,
                        item.Function.Extent.StartColumnNumber));
                }
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
                PowerShellCSharpSymbolRenderer.Identifier(source.Function.Name) + "\0" +
                string.Join("\0", source.Unit.Parameters.Select(static parameter => parameter.TypeName)),
                StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .SelectMany(group => group.Select(source =>
            {
                diagnostics.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    $"Function '{source.Function.Name}' collides with another generated CLR method signature '{PowerShellCSharpSymbolRenderer.Identifier(source.Function.Name)}'.",
                    source.Parsed.Path,
                    source.Function.Extent.StartLineNumber,
                    source.Function.Extent.StartColumnNumber));
                return GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
            }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var methods = new List<PowerShellCompiledMethod>();
        var methodSources = new List<string>();
        if (capabilities.HasFlag(PowerShellCompilationCapability.LocalFunctionCalls))
        {
            EmitFunctionGraph(
                functionSources,
                boundEmissions,
                duplicateFunctions,
                collidingFunctions,
                excludedMethods,
                capabilities,
                methods,
                methodSources,
                diagnostics);
        }
        else
        {
            foreach (var source in functionSources)
            {
                var key = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
                var duplicateKey = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
                if (!source.Unit.IsCompilable || duplicateFunctions.Contains(duplicateKey) || collidingFunctions.Contains(key) || excludedMethods?.Contains(key) == true)
                    continue;
                TryEmitIndependent(source, targetFramework, capabilities, boundEmissions, methods, methodSources, diagnostics);
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

        return CreateResult(fullPaths, namespaceName, typeName, methods.ToArray(), methodSources.ToArray(), diagnostics, parsedFiles, targetFramework, boundResult.Optimization, boundResult.IrSnapshots);
    }

    private static void EmitFunctionGraph(
        IReadOnlyList<FunctionSource> sources,
        IReadOnlyDictionary<string, PowerShellCSharpMethodEmission> boundEmissions,
        ISet<string> duplicateFunctions,
        ISet<string> collidingFunctions,
        ISet<string>? excludedMethods,
        PowerShellCompilationCapability capabilities,
        List<PowerShellCompiledMethod> methods,
        List<string> methodSources,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        foreach (var source in sources
                     .Where(source =>
                     {
                         var key = GetMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Body.Extent.StartLineNumber);
                         return source.Unit.IsCompilable &&
                                !duplicateFunctions.Contains(key) &&
                                !collidingFunctions.Contains(key) &&
                                excludedMethods?.Contains(key) != true;
                     })
                     .OrderBy(static source => source.Parsed.Path, PowerShellCompilationPathSafety.PathComparer)
                     .ThenBy(static source => source.Function.Extent.StartOffset))
        {
            TryEmitIndependent(source, null, capabilities, boundEmissions, methods, methodSources, diagnostics);
        }
    }

    private static void TryEmitIndependent(
        FunctionSource source,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        IReadOnlyDictionary<string, PowerShellCSharpMethodEmission> boundEmissions,
        List<PowerShellCompiledMethod> methods,
        List<string> methodSources,
        List<PowerShellCompilationDiagnostic> diagnostics)
    {
        try
        {
            var emitted = TryEmitBoundIndependent(source, boundEmissions) ??
                          throw new PowerShellCSharpEmissionException(source.Function, $"Function '{source.Function.Name}' is not eligible in the shared semantic compilation result.");
            EnsureBasicFunctionBinarySurfacePreserved(source, emitted, capabilities);
            methodSources.Add(emitted.Source);
            methods.Add(CreateCompiledMethod(source, emitted));
        }
        catch (PowerShellCSharpEmissionException ex)
        {
            diagnostics.Add(CreateDiagnostic(source, ex.Node, ex.Message));
        }
    }

    private static PowerShellCSharpMethodEmission? TryEmitBoundIndependent(
        FunctionSource source,
        IReadOnlyDictionary<string, PowerShellCSharpMethodEmission> boundEmissions)
    {
        return boundEmissions.TryGetValue(
            GetSemanticMethodKey(source.Parsed.Path, source.Function.Name, source.Function.Extent.StartLineNumber),
            out var emitted)
            ? emitted
            : null;
    }

    private static BoundEmissionIndex CreateBoundEmissionIndex(
        IReadOnlyList<ParsedSource> sources,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellCompilationDiagnostic> diagnostics,
        PowerShellCommandSemanticRegistry commandRegistry,
        string semanticProfileId)
    {
        var result = new PowerShellSemanticCompilationPipeline(commandRegistry, semanticProfileId).Compile(
            sources.Select(static source => source.Document),
            targetFramework,
            capabilities);
        var paths = sources.ToDictionary(static source => source.Document.DocumentId, static source => source.Path, StringComparer.Ordinal);
        foreach (var diagnostic in result.Lowered.Diagnostics
                     .Where(static diagnostic => diagnostic.Code != "PSB1002")
                     .GroupBy(static item => item.Code + "\0" + item.Message + "\0" + item.Span.DocumentId + "\0" + item.Span.StartOffset, StringComparer.Ordinal)
                     .Select(static group => group.First()))
        {
            var path = paths.TryGetValue(diagnostic.Span.DocumentId, out var resolvedPath)
                ? resolvedPath
                : diagnostic.Span.DocumentId;
            if (diagnostics.Any(existing =>
                    existing.Message.Equals(diagnostic.Message, StringComparison.Ordinal) &&
                    PowerShellCompilationPathSafety.PathEquals(existing.FilePath, path) &&
                    existing.Line == diagnostic.Span.StartLine &&
                    existing.Column == diagnostic.Span.StartColumn))
                continue;
            diagnostics.Add(new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                diagnostic.Message,
                path,
                diagnostic.Span.StartLine,
                diagnostic.Span.StartColumn,
                diagnostic.Code));
        }
        var emissions = new Dictionary<string, PowerShellCSharpMethodEmission>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < result.Lowered.Functions.Length && index < result.Emitted.Methods.Length; index++)
        {
            var function = result.Lowered.Functions[index];
            if (!paths.TryGetValue(function.Symbol.DocumentId, out var path)) continue;
            emissions[GetSemanticMethodKey(path, function.Symbol.Name, function.Symbol.Declaration.StartLine)] = result.Emitted.Methods[index];
        }
        return new BoundEmissionIndex(emissions, result.Optimization.ToPublicModel(), PowerShellCompilationIrSnapshotBuilder.Create(result));
    }

    private static string GetSemanticMethodKey(string path, string name, int definitionStartLine)
        => path + "\0" + name + "\0" + definitionStartLine;

    private static void EnsureBasicFunctionBinarySurfacePreserved(
        FunctionSource source,
        PowerShellCSharpMethodEmission emitted,
        PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) ||
            PowerShellAdvancedFunctionPolicy.IsAdvanced(source.Function) ||
            !emitted.RequiresPowerShellStreams && !emitted.RequiresPowerShellCommandRegions ||
            emitted.SupportsBasicCommandQuerySurface)
            return;
        throw new PowerShellCSharpEmissionException(
            source.Function,
            $"Basic function '{source.Function.Name}' uses PowerShell stream or command-host behavior that cannot preserve loose handling of generated binary-cmdlet common-parameter names.");
    }

    private static PowerShellCompiledMethod CreateCompiledMethod(FunctionSource source, PowerShellCSharpMethodEmission emitted)
    {
        var method = new PowerShellCompiledMethod(
            source.Function.Name,
            emitted.GeneratedName,
            emitted.ReturnType.FullName ?? emitted.ReturnType.Name,
            source.Unit.Parameters,
            emitted.SourceSpan.StartLine,
            source.Parsed.Path,
            emitted.RequiresPowerShellStreams,
            emitted.RequiresPowerShellCommandRegions,
            emitted.Aliases,
            emitted.RequiresPowerShellBoundParameters,
            emitted.CommandBinding.IsAdvancedFunction,
            emitted.CommandBinding,
            emitted.RequiresPowerShellRuntimeState,
            emitted.DeclaredOutputTypeName,
            emitted.SourceSpan.StartColumn,
            emitted.SourceSpan.EndLine,
            emitted.SourceSpan.EndColumn,
            emitted.SourceMap,
            emitted.CommandProviders,
            emitted.OutputCardinality,
            emitted.OutputValueStates,
            emitted.CollectionElementType,
            emitted.OutputScalarization,
            emitted.HostedRegionSiteCount,
            emitted.RequiresProviderCancellation);
        method.DocumentId = emitted.SourceSpan.DocumentId;
        method.DeclaredOutputTypeIsSemanticContract = emitted.DeclaredOutputType is not null;
        method.Help = emitted.Help ?? PowerShellCommentHelpBinder.Bind(source.Function)?.ToPublicModel();
        return method;
    }

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
        IEnumerable<PowerShellCompilationDiagnostic> diagnostics,
        IEnumerable<ParsedSource> parsedFiles,
        string? targetFramework,
        PowerShellCompilationOptimizationEvidence? optimization = null,
        PowerShellCompilationIrSnapshotBundle? irSnapshots = null)
    {
        var template = ReadTemplate();
        var source = template
            .Replace("{{NAMESPACE}}", SanitizeQualifiedName(namespaceName))
            .Replace("{{TYPE_NAME}}", PowerShellCSharpSymbolRenderer.Identifier(typeName))
            .Replace("{{METHODS}}", string.Join(Environment.NewLine + Environment.NewLine, methodSources));
        return new PowerShellTypedCompilationResult(
            sourcePaths[0],
            SanitizeQualifiedName(namespaceName),
            PowerShellCSharpSymbolRenderer.Identifier(typeName),
            source,
            methods,
            diagnostics
                .GroupBy(static diagnostic => new { diagnostic.FilePath, diagnostic.Code, diagnostic.Line, diagnostic.Column, diagnostic.Message })
                .Select(static group => group.First())
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            sourcePaths,
            parsedFiles.SelectMany(parsed => PowerShellLifecycleSourceBinder.Bind(parsed.Document, targetFramework)).ToArray(),
            optimization,
            irSnapshots);
    }

    private sealed class BoundEmissionIndex
    {
        internal BoundEmissionIndex(
            IReadOnlyDictionary<string, PowerShellCSharpMethodEmission> emissions,
            PowerShellCompilationOptimizationEvidence optimization,
            PowerShellCompilationIrSnapshotBundle irSnapshots)
        {
            Emissions = emissions;
            Optimization = optimization;
            IrSnapshots = irSnapshots;
        }

        internal IReadOnlyDictionary<string, PowerShellCSharpMethodEmission> Emissions { get; }
        internal PowerShellCompilationOptimizationEvidence Optimization { get; }
        internal PowerShellCompilationIrSnapshotBundle IrSnapshots { get; }
    }

    private static string ReadTemplate()
    {
        using var stream = typeof(PowerShellTypedCompilationTranspiler).Assembly.GetManifestResourceStream(TemplateResourceName)
            ?? throw new InvalidOperationException($"Embedded compilation template '{TemplateResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string SanitizeQualifiedName(string value)
        => string.Join(".", value.Split('.').Select(PowerShellCSharpSymbolRenderer.Identifier));

    private static string GetMethodKey(string sourcePath, string name, int sourceLine)
        => Path.GetFullPath(sourcePath) + "\0" + name + "\0" + sourceLine;

    private static string ResolveCollisionFreeTypeName(string requestedTypeName, IEnumerable<ScriptBlockAst> asts)
    {
        var generatedMethods = asts.SelectMany(ast => ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>())
            .Select(static function => PowerShellCSharpSymbolRenderer.Identifier(function.Name))
            .ToHashSet(StringComparer.Ordinal);
        var candidate = PowerShellCSharpSymbolRenderer.Identifier(requestedTypeName);
        while (generatedMethods.Contains(candidate))
            candidate = PowerShellCSharpSymbolRenderer.Identifier("_" + candidate.TrimStart('@'));
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

    private sealed class ParsedSource
    {
        internal ParsedSource(string path, ScriptBlockAst ast, PowerShellCompilationFilePlan plan, ParsedSourceDocument document)
        {
            Path = path;
            Ast = ast;
            Plan = plan;
            Document = document;
        }

        internal string Path { get; }
        internal ScriptBlockAst Ast { get; }
        internal PowerShellCompilationFilePlan Plan { get; }
        internal ParsedSourceDocument Document { get; }
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
        SourceSpan sourceSpan,
        bool requiresPowerShellStreams = false,
        bool requiresProviderCancellation = false,
        bool requiresPowerShellCommandRegions = false,
        bool requiresPowerShellBoundParameters = false,
        bool requiresPowerShellRuntimeState = false,
        Type? declaredOutputType = null,
        string? declaredOutputTypeName = null,
        PowerShellCompilationHelp? help = null,
        string[]? aliases = null,
        PowerShellCompilationCommandBinding? commandBinding = null,
        PowerShellCompilationSourceMapEntry[]? sourceMap = null,
        PowerShellCompilationCommandProviderContract[]? commandProviders = null,
        string? outputCardinality = null,
        string[]? outputValueStates = null,
        string? collectionElementType = null,
        string? outputScalarization = null,
        int hostedRegionSiteCount = 0,
        bool supportsBasicCommandQuerySurface = false)
    {
        GeneratedName = generatedName;
        ReturnType = returnType;
        Source = source;
        SourceSpan = sourceSpan;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresProviderCancellation = requiresProviderCancellation;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
        DeclaredOutputType = declaredOutputType;
        DeclaredOutputTypeName = declaredOutputTypeName ?? declaredOutputType?.FullName ?? string.Empty;
        Help = help;
        Aliases = aliases ?? Array.Empty<string>();
        CommandBinding = commandBinding ?? new PowerShellCompilationCommandBinding();
        SourceMap = sourceMap ?? Array.Empty<PowerShellCompilationSourceMapEntry>();
        CommandProviders = commandProviders ?? Array.Empty<PowerShellCompilationCommandProviderContract>();
        OutputCardinality = outputCardinality ?? string.Empty;
        OutputValueStates = outputValueStates ?? Array.Empty<string>();
        CollectionElementType = collectionElementType ?? string.Empty;
        OutputScalarization = outputScalarization ?? string.Empty;
        HostedRegionSiteCount = hostedRegionSiteCount;
        SupportsBasicCommandQuerySurface = supportsBasicCommandQuerySurface;
    }

    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal string Source { get; }
    internal SourceSpan SourceSpan { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresProviderCancellation { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
    internal bool RequiresPowerShellBoundParameters { get; }
    internal bool RequiresPowerShellRuntimeState { get; }
    internal Type? DeclaredOutputType { get; }
    internal string DeclaredOutputTypeName { get; }
    internal PowerShellCompilationHelp? Help { get; }
    internal string[] Aliases { get; }
    internal PowerShellCompilationCommandBinding CommandBinding { get; }
    internal PowerShellCompilationSourceMapEntry[] SourceMap { get; }
    internal PowerShellCompilationCommandProviderContract[] CommandProviders { get; }
    internal string OutputCardinality { get; }
    internal string[] OutputValueStates { get; }
    internal string CollectionElementType { get; }
    internal string OutputScalarization { get; }
    internal int HostedRegionSiteCount { get; }
    internal bool SupportsBasicCommandQuerySurface { get; }
}

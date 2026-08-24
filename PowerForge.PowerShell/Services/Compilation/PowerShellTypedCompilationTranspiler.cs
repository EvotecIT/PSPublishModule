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
        => TranspileCore(new[] { sourcePath }, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapability.None);

    /// <summary>Translates eligible functions from files sharing one PowerShell module scope.</summary>
    public PowerShellTypedCompilationResult Transpile(
        IEnumerable<string> sourcePaths,
        string namespaceName = "PowerForge.Compiled",
        string typeName = "CompiledPowerShell",
        string? targetFramework = null)
        => TranspileCore(sourcePaths, namespaceName, typeName, targetFramework, excludedMethods: null, PowerShellCompilationCapability.None);

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
            PowerShellCompilationCapability.PowerShellStreams);

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
            .Distinct(GetPathComparer())
            .ToArray();
        if (fullPaths.Length == 0)
            throw new ArgumentException("At least one PowerShell source path is required.", nameof(sourcePaths));

        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var parsedFiles = new List<ParsedSource>();
        foreach (var fullPath in fullPaths)
        {
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                fullPath,
                targetFramework: targetFramework,
                capabilities: capabilities));
            var filePlan = plan.Files.Single();
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
        var methods = new List<PowerShellCompiledMethod>();
        var methodSources = new List<string>();
        foreach (var parsed in parsedFiles)
        {
            var eligible = parsed.Plan.Units
                .Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function &&
                               unit.IsCompilable &&
                               !duplicateFunctions.Contains(GetMethodKey(parsed.Path, unit.Name, unit.StartLine)))
                .ToDictionary(static unit => (unit.Name, unit.StartLine));
            foreach (var function in parsed.Ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                         .Cast<FunctionDefinitionAst>()
                         .OrderBy(static function => function.Extent.StartOffset))
            {
                if (!eligible.TryGetValue((function.Name, function.Body.Extent.StartLineNumber), out var unit))
                    continue;
                if (excludedMethods?.Contains(GetMethodKey(parsed.Path, function.Name, function.Extent.StartLineNumber)) == true)
                    continue;

                try
                {
                    var emitted = new PowerShellCSharpMethodEmitter(parsed.Path, function, targetFramework, capabilities).Emit();
                    methodSources.Add(emitted.Source);
                    methods.Add(new PowerShellCompiledMethod(
                        function.Name,
                        emitted.GeneratedName,
                        emitted.ReturnType.FullName ?? emitted.ReturnType.Name,
                        unit.Parameters,
                        function.Extent.StartLineNumber,
                        parsed.Path,
                        emitted.RequiresPowerShellStreams,
                        emitted.RequiresPowerShellCommandRegions));
                }
                catch (PowerShellCSharpEmissionException ex)
                {
                    diagnostics.Add(new PowerShellCompilationDiagnostic(
                        PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                        ex.Message,
                        parsed.Path,
                        ex.Node.Extent.StartLineNumber,
                        ex.Node.Extent.StartColumnNumber));
                }
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

    private static StringComparer GetPathComparer()
        => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
        bool requiresPowerShellCommandRegions = false)
    {
        GeneratedName = generatedName;
        ReturnType = returnType;
        Source = source;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
    }

    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal string Source { get; }
    internal bool RequiresPowerShellStreams { get; }
    internal bool RequiresPowerShellCommandRegions { get; }
}

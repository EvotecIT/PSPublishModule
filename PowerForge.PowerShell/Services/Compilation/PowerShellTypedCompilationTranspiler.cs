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
        => TranspileCore(sourcePath, namespaceName, typeName, targetFramework, excludedMethods: null);

    internal PowerShellTypedCompilationResult TranspileExcluding(
        string sourcePath,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string> excludedMethods)
        => TranspileCore(sourcePath, namespaceName, typeName, targetFramework, excludedMethods);

    private static PowerShellTypedCompilationResult TranspileCore(
        string sourcePath,
        string namespaceName,
        string typeName,
        string? targetFramework,
        ISet<string>? excludedMethods)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("A PowerShell source path is required.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(namespaceName))
            throw new ArgumentException("A generated namespace is required.", nameof(namespaceName));
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("A generated type name is required.", nameof(typeName));

        var fullPath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(fullPath, targetFramework: targetFramework));
        var filePlan = plan.Files.Single();
        var diagnostics = new List<PowerShellCompilationDiagnostic>(filePlan.Diagnostics);
        diagnostics.AddRange(filePlan.Units.SelectMany(static unit => unit.Diagnostics));

        Token[] tokens;
        ParseError[] parseErrors;
        var ast = Parser.ParseFile(fullPath, out tokens, out parseErrors);
        if (parseErrors.Length > 0)
            return CreateResult(fullPath, namespaceName, typeName, Array.Empty<PowerShellCompiledMethod>(), Array.Empty<string>(), diagnostics);
        typeName = ResolveCollisionFreeTypeName(typeName, ast);

        var eligible = filePlan.Units
            .Where(unit => unit.Kind == PowerShellCompilationUnitKind.Function &&
                           unit.IsCompilable)
            .ToDictionary(static unit => (unit.Name, unit.StartLine));
        var methods = new List<PowerShellCompiledMethod>();
        var methodSources = new List<string>();
        foreach (var function in ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                     .Cast<FunctionDefinitionAst>()
                     .OrderBy(static function => function.Extent.StartOffset))
        {
            if (excludedMethods is not null &&
                excludedMethods.Contains(GetMethodKey(function.Name, function.Extent.StartLineNumber)))
                continue;
            if (!eligible.TryGetValue((function.Name, function.Body.Extent.StartLineNumber), out var unit))
                continue;

            try
            {
                var emitted = new PowerShellCSharpMethodEmitter(fullPath, function, targetFramework).Emit();
                methodSources.Add(emitted.Source);
                methods.Add(new PowerShellCompiledMethod(
                    function.Name,
                    emitted.GeneratedName,
                    emitted.ReturnType.FullName ?? emitted.ReturnType.Name,
                    unit.Parameters,
                    function.Extent.StartLineNumber));
            }
            catch (PowerShellCSharpEmissionException ex)
            {
                diagnostics.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    ex.Message,
                    fullPath,
                    ex.Node.Extent.StartLineNumber,
                    ex.Node.Extent.StartColumnNumber));
            }
        }

        return CreateResult(fullPath, namespaceName, typeName, methods.ToArray(), methodSources.ToArray(), diagnostics);
    }

    private static PowerShellTypedCompilationResult CreateResult(
        string sourcePath,
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
            sourcePath,
            SanitizeQualifiedName(namespaceName),
            PowerShellCSharpMethodEmitter.SanitizeIdentifier(typeName),
            source,
            methods,
            diagnostics
                .GroupBy(static diagnostic => new { diagnostic.Code, diagnostic.Line, diagnostic.Column, diagnostic.Message })
                .Select(static group => group.First())
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray());
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

    private static string GetMethodKey(string name, int sourceLine)
        => name + "\0" + sourceLine;

    private static string ResolveCollisionFreeTypeName(string requestedTypeName, ScriptBlockAst ast)
    {
        var generatedMethods = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .Select(static function => PowerShellCSharpMethodEmitter.SanitizeIdentifier(function.Name))
            .ToHashSet(StringComparer.Ordinal);
        var candidate = PowerShellCSharpMethodEmitter.SanitizeIdentifier(requestedTypeName);
        while (generatedMethods.Contains(candidate))
            candidate = PowerShellCSharpMethodEmitter.SanitizeIdentifier("_" + candidate.TrimStart('@'));
        return candidate;
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
    internal PowerShellCSharpMethodEmission(string generatedName, Type returnType, string source)
    {
        GeneratedName = generatedName;
        ReturnType = returnType;
        Source = source;
    }

    internal string GeneratedName { get; }
    internal Type ReturnType { get; }
    internal string Source { get; }
}

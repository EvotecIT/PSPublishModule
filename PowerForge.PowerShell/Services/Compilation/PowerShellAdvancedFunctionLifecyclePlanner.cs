using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Creates explicit hosted lifecycle contracts for Hybrid binary cmdlets.</summary>
internal static class PowerShellAdvancedFunctionLifecyclePlanner
{
    internal static bool HasNamedLifecycle(IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths)
        {
            var ast = Parser.ParseFile(sourcePath, out _, out var errors);
            if (errors.Length > 0) continue;
            if (ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .Any(static function => function.Body.BeginBlock is not null || function.Body.ProcessBlock is not null || GetCleanBlock(function.Body) is not null))
                return true;
        }
        return false;
    }

    internal static PowerShellTypedCompilationResult AddHostedLifecycleMethods(
        PowerShellTypedCompilationResult typed,
        string? targetFramework)
    {
        var existing = typed.Methods.Select(static method => MethodKey(method.SourcePath, method.SourceName, method.SourceLine))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lifecycleMethods = new List<PowerShellCompiledMethod>();
        foreach (var sourcePath in typed.SourcePaths.OrderBy(static path => path, PowerShellCompilationPathSafety.PathComparer))
        {
            var ast = Parser.ParseFile(sourcePath, out _, out var errors);
            if (errors.Length > 0) continue;
            foreach (var function in ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                         .Cast<FunctionDefinitionAst>()
                         .OrderBy(static function => function.Extent.StartOffset))
            {
                var body = function.Body;
                var clean = GetCleanBlock(body);
                if (body.BeginBlock is null && body.ProcessBlock is null && clean is null) continue;
                if (body.DynamicParamBlock is not null) continue;
                var key = MethodKey(sourcePath, function.Name, body.Extent.StartLineNumber);
                if (existing.Contains(key)) continue;
                lifecycleMethods.Add(CreateMethod(sourcePath, function, clean, targetFramework));
            }
        }
        if (lifecycleMethods.Count == 0) return typed;
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
            typed.SourcePaths);
    }

    private static PowerShellCompiledMethod CreateMethod(
        string sourcePath,
        FunctionDefinitionAst function,
        NamedBlockAst? clean,
        string? targetFramework)
    {
        var parameters = function.Body.ParamBlock?.Parameters
            .Select(parameter => PowerShellParameterContractBinder.Bind(parameter, targetFramework))
            .ToArray() ?? Array.Empty<PowerShellCompilationParameter>();
        var binding = PowerShellAdvancedFunctionPolicy.GetBinding(function.Body.ParamBlock);
        var pipelineNames = parameters
            .Where(static parameter => parameter.Bindings.Any(binding => binding.ValueFromPipeline || binding.ValueFromPipelineByPropertyName))
            .Select(static parameter => parameter.Name)
            .ToArray();
        var source = function.Extent.Text;
        var method = new PowerShellCompiledMethod(
            function.Name,
            PowerShellCSharpSymbolRenderer.Identifier(function.Name) + "HostedLifecycle",
            typeof(object).FullName!,
            parameters,
            function.Body.Extent.StartLineNumber,
            sourcePath,
            requiresPowerShellStreams: true,
            requiresPowerShellCommandRegions: false,
            aliases: PowerShellAdvancedFunctionPolicy.GetAliases(function),
            requiresPowerShellBoundParameters: true,
            isAdvancedFunction: true,
            commandBinding: binding,
            requiresPowerShellRuntimeState: binding.SupportsShouldProcess,
            declaredOutputType: string.Empty,
            sourceColumn: function.Body.Extent.StartColumnNumber,
            sourceEndLine: function.Body.Extent.EndLineNumber,
            sourceEndColumn: function.Body.Extent.EndColumnNumber,
            commandProviders: new[] { PowerShellCommandSemanticRegistry.HostedRegionContract("<advanced-lifecycle>") });
        method.Help = PowerShellCommentHelpBinder.Bind(function)?.ToPublicModel();
        method.HostedLifecycleSource = function.Body.Extent.Text;
        method.Lifecycle = new PowerShellCompilationLifecycleContract
        {
            Execution = PowerShellCompilationLifecycleExecution.HostedSteppablePipeline,
            HasBegin = function.Body.BeginBlock is not null,
            HasProcess = function.Body.ProcessBlock is not null,
            HasEnd = function.Body.EndBlock is not null,
            HasClean = clean is not null,
            ValueFromPipeline = parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromPipeline)),
            ValueFromPipelineByPropertyName = parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromPipelineByPropertyName)),
            ValueFromRemainingArguments = parameters.Any(static parameter => parameter.Bindings.Any(static binding => binding.ValueFromRemainingArguments)),
            CommonParameters = binding.IsAdvancedFunction,
            SupportsShouldProcess = binding.SupportsShouldProcess,
            ConfirmImpact = binding.ConfirmImpact,
            SourceSha256 = ComputeSha256(source),
            PipelineParameterNames = pipelineNames,
            HostingReason = "Named advanced-function blocks retain PowerShell lifecycle semantics through a generated steppable-pipeline cmdlet; the artifact is Hybrid and is not runtime-free."
        };
        return method;
    }

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst body)
        => body.GetType().GetProperty("CleanBlock")?.GetValue(body) as NamedBlockAst;

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string MethodKey(string path, string name, int line)
        => Path.GetFullPath(path) + "\0" + name + "\0" + line.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

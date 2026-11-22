using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Presents body-param and function-header parameters through one front-end view while preserving
/// the original AST nodes, source extents, and parameter attributes.
/// </summary>
internal sealed class PowerShellParameterSyntax
{
    private PowerShellParameterSyntax(ScriptBlockAst body)
    {
        Body = body;
        Parameters = GetParameters(body);
        Attributes = body.ParamBlock?.Attributes ?? (IReadOnlyList<AttributeAst>)Array.Empty<AttributeAst>();
        Extent = body.ParamBlock?.Extent ?? body.Extent;
    }

    internal ScriptBlockAst Body { get; }
    internal IReadOnlyList<ParameterAst> Parameters { get; }
    internal IReadOnlyList<AttributeAst> Attributes { get; }
    internal IScriptExtent Extent { get; }

    internal static PowerShellParameterSyntax Create(ScriptBlockAst body)
        => new(body ?? throw new ArgumentNullException(nameof(body)));

    internal static IReadOnlyList<ParameterAst> GetParameters(ScriptBlockAst? body)
    {
        if (body?.ParamBlock is { } block) return block.Parameters;
        if (body?.Parent is FunctionDefinitionAst function && ReferenceEquals(function.Body, body))
            return function.Parameters ?? (IReadOnlyList<ParameterAst>)Array.Empty<ParameterAst>();
        return Array.Empty<ParameterAst>();
    }

    /// <summary>Preserves header parameter binding when a hosted lifecycle invokes the body as a script block.</summary>
    internal static string GetInvocableBodySource(FunctionDefinitionAst function)
    {
        var source = function.Body.Extent.Text;
        if (function.Body.ParamBlock is not null || function.Parameters is not { Count: > 0 })
            return source;
        return "{" + Environment.NewLine + "param(" +
               string.Join("," + Environment.NewLine, function.Parameters.Select(static parameter => parameter.Extent.Text)) +
               ")" + Environment.NewLine + source.Substring(1);
    }
}

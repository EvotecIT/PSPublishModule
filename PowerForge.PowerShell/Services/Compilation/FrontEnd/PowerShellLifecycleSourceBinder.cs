using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Binds advanced-function lifecycle syntax into parser-independent front-end contracts.</summary>
internal static class PowerShellLifecycleSourceBinder
{
    internal static PowerShellCompilationLifecycleSource[] Bind(
        ParsedSourceDocument document,
        string? targetFramework,
        string semanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId)
    {
        if (document.Errors.Length > 0) return Array.Empty<PowerShellCompilationLifecycleSource>();
        return document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .OrderBy(static function => function.Extent.StartOffset)
            .Where(static function => function.Body.DynamicParamBlock is null)
            .Select(function => Create(document.Path, function, targetFramework, semanticProfileId))
            .Where(static source => source is not null)
            .Cast<PowerShellCompilationLifecycleSource>()
            .ToArray();
    }

    private static PowerShellCompilationLifecycleSource? Create(
        string sourcePath,
        FunctionDefinitionAst function,
        string? targetFramework,
        string semanticProfileId)
    {
        var clean = GetCleanBlock(function.Body);
        if (function.Body.BeginBlock is null && function.Body.ProcessBlock is null && clean is null)
            return null;
        var source = function.Extent.Text;
        return new PowerShellCompilationLifecycleSource
        {
            Name = function.Name,
            SourcePath = sourcePath,
            SourceLine = function.Body.Extent.StartLineNumber,
            SourceColumn = function.Body.Extent.StartColumnNumber,
            SourceEndLine = function.Body.Extent.EndLineNumber,
            SourceEndColumn = function.Body.Extent.EndColumnNumber,
            HostedBodySource = function.Body.Extent.Text,
            SourceSha256 = ComputeSha256(source),
            HasBegin = function.Body.BeginBlock is not null,
            HasProcess = function.Body.ProcessBlock is not null,
            HasEnd = function.Body.EndBlock is not null,
            HasClean = clean is not null,
            MinimumPowerShellVersion = clean is null ? "5.1" : "7.3",
            Parameters = function.Body.ParamBlock?.Parameters
                .Select(parameter => PowerShellParameterContractBinder.Bind(
                    parameter,
                    targetFramework,
                    semanticProfileId: semanticProfileId))
                .ToArray() ?? Array.Empty<PowerShellCompilationParameter>(),
            CommandBinding = PowerShellAdvancedFunctionPolicy.GetBinding(function.Body.ParamBlock),
            Aliases = PowerShellAdvancedFunctionPolicy.GetAliases(function),
            Help = PowerShellCommentHelpBinder.Bind(function)?.ToPublicModel()
        };
    }

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst body)
        => body.GetType().GetProperty("CleanBlock")?.GetValue(body) as NamedBlockAst;

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

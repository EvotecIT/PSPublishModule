using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Selects a conservative terminal suffix from already-bound statements. This owner never inspects
/// generated C# or the final disposition ledger and therefore cannot create a second semantic path.
/// </summary>
internal static class PowerShellBoundRegionCandidateSelector
{
    internal static bool TryCreate(
        ParsedSourceDocument document,
        System.Management.Automation.Language.FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> locals,
        IReadOnlyList<System.Management.Automation.Language.StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        int lastFailedStatementIndex,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (HasNamedLifecycle(syntax.Body)) return false;
        var suffix = bindings
            .Where(binding => binding.AuthoredStatementIndex > lastFailedStatementIndex)
            .OrderBy(static binding => binding.AuthoredStatementIndex)
            .ToArray();
        if (suffix.Length == 0 ||
            suffix[0].AuthoredStatementIndex != lastFailedStatementIndex + 1 ||
            suffix[suffix.Length - 1].AuthoredStatementIndex != authoredStatements.Count - 1 ||
            !AlwaysReturns(suffix[suffix.Length - 1].Statement))
            return false;

        var first = suffix[0].Statement.Span;
        var last = suffix[suffix.Length - 1].Statement.Span;
        var span = new SourceSpan(
            document.DocumentId,
            first.StartOffset,
            last.EndOffset,
            first.StartLine,
            first.StartColumn,
            last.EndLine,
            last.EndColumn);
        var statements = suffix.Select(static binding => binding.Statement).ToArray();
        if (PowerShellSemanticAnalyzer.EnumerateStatements(new PowerShellBoundBlock(first, statements))
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .Any(static expression => expression is PowerShellBoundInvocationExpression))
            return false;
        var usedSymbols = CollectUsedSymbolKeys(statements);
        var selectedParameters = parameters.Where(parameter => usedSymbols.Contains(parameter.Symbol.StableKey)).ToArray();
        if (selectedParameters.Any(static parameter =>
                parameter.Contract.IsSwitch ||
                !IsSimpleVariableName(parameter.Symbol.Name) ||
                !PowerShellStableScalarTypePolicy.IsSupported(parameter.Type.ClrType)))
            return false;
        var selectedLocals = locals.Where(local => usedSymbols.Contains(local.Symbol.StableKey)).ToArray();
        if (selectedLocals.Any(static local =>
                local.Type.Provenance == PowerShellTypeFactProvenance.Unknown ||
                !PowerShellStableScalarTypePolicy.IsSupported(local.Type.ClrType)))
            return false;

        var helperName = CreateHelperName(sourceFunction, span);
        var helperSymbol = new PowerShellSymbolId(
            PowerShellSymbolKind.Function,
            document.DocumentId,
            helperName,
            span,
            sourceFunction.Name + "/region/" + span.StartOffset + "/" + span.EndOffset);
        var helperParameters = selectedParameters.Select(parameter => new PowerShellBoundParameter(
            parameter.Symbol,
            parameter.Type,
            new PowerShellCompilationParameter(
                parameter.Symbol.Name,
                parameter.Type.ClrType.FullName ?? parameter.Type.ClrType.Name,
                hasDefaultValue: false))).ToArray();
        var scopeSymbols = helperParameters.Select(static parameter => parameter.Symbol)
            .Concat(selectedLocals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        var body = new PowerShellBoundBlock(span, statements);
        var helper = new PowerShellBoundFunction(
            helperSymbol,
            helperParameters,
            selectedLocals,
            new PowerShellLexicalScope(helperSymbol, scopeSymbols),
            help: null,
            aliases: Array.Empty<string>(),
            commandBinding: new PowerShellCompilationCommandBinding(),
            declaredOutputType: null,
            declaredOutputTypeName: string.Empty,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            body.Effects,
            body.Capabilities,
            PowerShellExecutionDisposition.Typed);
        candidate = new PowerShellBoundRegionCandidate(
            "region:" + document.DocumentId + ":" + span.StartOffset + ":" + span.EndOffset,
            ComputeSha256(document.Text.Substring(span.StartOffset, span.EndOffset - span.StartOffset)),
            ComputeSha256(document.Text),
            document.Path,
            sourceFunction.Name,
            syntax.Body.Extent.StartLineNumber,
            helper,
            helperParameters.Select(static parameter => parameter.Contract).ToArray());
        return true;
    }

    private static bool AlwaysReturns(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement => true,
            PowerShellBoundIfStatement conditional => conditional.ElseBlock is not null &&
                conditional.Clauses.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)) &&
                conditional.ElseBlock.Statements.LastOrDefault() is { } otherwise && AlwaysReturns(otherwise),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.DefaultBlock is not null &&
                switchStatement.Clauses.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)) &&
                switchStatement.DefaultBlock.Statements.LastOrDefault() is { } otherwise && AlwaysReturns(otherwise),
            PowerShellBoundTryStatement tryStatement =>
                tryStatement.Body.Statements.LastOrDefault() is { } body && AlwaysReturns(body) &&
                tryStatement.Catches.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)),
            _ => false
        };

    private static HashSet<string> CollectUsedSymbolKeys(IEnumerable<PowerShellBoundStatement> statements)
    {
        var block = new PowerShellBoundBlock(
            statements.First().Span,
            statements.ToArray());
        var keys = PowerShellSemanticAnalyzer.EnumerateStatements(block)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .SelectMany(static expression => expression switch
            {
                PowerShellBoundVariableExpression variable => new[] { variable.Symbol.StableKey },
                PowerShellBoundMutationExpression mutation => new[] { mutation.Target.StableKey },
                _ => Array.Empty<string>()
            })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(block))
        {
            if (statement is PowerShellBoundAssignmentStatement assignment) keys.Add(assignment.Target.StableKey);
            if (statement is PowerShellBoundCommandCaptureStatement capture) keys.Add(capture.Target.StableKey);
            if (statement is PowerShellBoundForEachStatement forEach) keys.Add(forEach.Variable.StableKey);
            if (statement is PowerShellBoundForStatement { Initializer: not null } forLoop) keys.Add(forLoop.Initializer.Target.StableKey);
        }
        return keys;
    }

    private static string CreateHelperName(PowerShellSymbolId function, SourceSpan span)
    {
        using var sha = SHA256.Create();
        var identity = function.StableKey + "\0" + span.StartOffset + "\0" + span.EndOffset;
        var suffix = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))
            .Take(12)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return "__PowerForgeRegion_" + suffix;
    }

    private static bool IsSimpleVariableName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           (char.IsLetter(value[0]) || value[0] == '_') &&
           value.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static bool HasNamedLifecycle(System.Management.Automation.Language.ScriptBlockAst body)
        => body.DynamicParamBlock is not null ||
           body.BeginBlock is not null ||
           body.ProcessBlock is not null ||
           body.EndBlock is { Unnamed: false } ||
           body.GetType().GetProperty("CleanBlock")?.GetValue(body) is not null;

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

internal sealed class PowerShellBoundStatementBinding
{
    internal PowerShellBoundStatementBinding(int authoredStatementIndex, PowerShellBoundStatement statement)
    {
        AuthoredStatementIndex = authoredStatementIndex;
        Statement = statement;
    }

    internal int AuthoredStatementIndex { get; }
    internal PowerShellBoundStatement Statement { get; }
}

internal sealed class PowerShellBoundRegionCandidate
{
    internal PowerShellBoundRegionCandidate(
        string regionId,
        string sourceSha256,
        string sourceDocumentSha256,
        string sourcePath,
        string sourceName,
        int sourceLine,
        PowerShellBoundFunction regionFunction,
        PowerShellCompilationParameter[] inputParameters)
    {
        RegionId = regionId;
        SourceSha256 = sourceSha256;
        SourceDocumentSha256 = sourceDocumentSha256;
        SourcePath = sourcePath;
        SourceName = sourceName;
        SourceLine = sourceLine;
        RegionFunction = regionFunction;
        InputParameters = inputParameters ?? Array.Empty<PowerShellCompilationParameter>();
    }

    internal string RegionId { get; }
    internal string SourceSha256 { get; }
    internal string SourceDocumentSha256 { get; }
    internal string SourcePath { get; }
    internal string SourceName { get; }
    internal int SourceLine { get; }
    internal PowerShellBoundFunction RegionFunction { get; }
    internal PowerShellImmutableArray<PowerShellCompilationParameter> InputParameters { get; }
}

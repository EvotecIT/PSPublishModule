using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Finds maximal contiguous runs that the canonical binder represented inside an otherwise
/// rejected function. The runs are analysis inputs only; this selector never approves emission.
/// </summary>
internal static class PowerShellBoundRegionOpportunitySelector
{
    internal static PowerShellBoundRegionOpportunity[] Discover(
        ParsedSourceDocument document,
        System.Management.Automation.Language.FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> locals,
        IReadOnlyList<System.Management.Automation.Language.StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings)
    {
        if (bindings.Count == 0) return Array.Empty<PowerShellBoundRegionOpportunity>();
        var ordered = bindings.OrderBy(static item => item.AuthoredStatementIndex).ToArray();
        var runs = new List<List<PowerShellBoundStatementBinding>>();
        foreach (var binding in ordered)
        {
            if (runs.Count == 0 || binding.AuthoredStatementIndex > runs[runs.Count - 1][runs[runs.Count - 1].Count - 1].AuthoredStatementEndIndex + 1)
                runs.Add(new List<PowerShellBoundStatementBinding>());
            runs[runs.Count - 1].Add(binding);
        }

        return runs.Select(run => Create(
                document,
                syntax,
                sourceFunction,
                parameters,
                locals,
                authoredStatements,
                ordered,
                run))
            .OrderBy(static item => item.RegionFunction.Body.Span.StartOffset)
            .ToArray();
    }

    internal static string SymbolIdentity(PowerShellSymbolId symbol)
        => symbol.Kind + ":" + symbol.Name.ToUpperInvariant();

    internal static IEnumerable<PowerShellSymbolId> EnumerateReadSymbols(IEnumerable<PowerShellBoundStatement> statements)
    {
        var items = statements.ToArray();
        if (items.Length == 0) yield break;
        var block = CreateBlock(items);
        foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(block))
        {
            foreach (var expression in PowerShellSemanticAnalyzer.EnumerateDirectExpressions(statement))
            foreach (var read in PowerShellSemanticAnalyzer.EnumerateVariableReads(expression))
                yield return read.Symbol;
            if (statement is PowerShellBoundCommandRegionStatement region)
            foreach (var argument in region.Arguments)
                yield return argument.Symbol;
            if (statement is PowerShellBoundCommandCaptureStatement capture)
            foreach (var argument in capture.Arguments)
                yield return argument.Symbol;
        }
    }

    internal static IEnumerable<PowerShellSymbolId> EnumerateWrittenSymbols(IEnumerable<PowerShellBoundStatement> statements)
    {
        var items = statements.ToArray();
        if (items.Length == 0) yield break;
        var block = CreateBlock(items);
        foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(block))
        {
            switch (statement)
            {
                case PowerShellBoundAssignmentStatement assignment:
                    yield return assignment.Target;
                    break;
                case PowerShellBoundCommandCaptureStatement capture:
                    yield return capture.Target;
                    break;
                case PowerShellBoundForEachStatement loop:
                    yield return loop.Variable;
                    break;
                case PowerShellBoundForStatement { Initializer: not null } loop:
                    yield return loop.Initializer.Target;
                    break;
            }
            foreach (var expression in PowerShellSemanticAnalyzer.EnumerateDirectExpressions(statement)
                         .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
                         .OfType<PowerShellBoundMutationExpression>())
                yield return expression.Target;
        }
    }

    internal static string[] EnumerateLocalCalls(IEnumerable<PowerShellBoundStatement> statements)
    {
        var items = statements.ToArray();
        if (items.Length == 0) return Array.Empty<string>();
        return CreateBlock(items).Statements
            .SelectMany(statement => PowerShellSemanticAnalyzer.EnumerateStatements(new PowerShellBoundBlock(statement.Span, new[] { statement })))
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .OfType<PowerShellBoundInvocationExpression>()
            .Select(static invocation => SymbolIdentity(invocation.Target))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool CanFallThrough(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement or PowerShellBoundThrowStatement => false,
            PowerShellBoundIfStatement conditional => conditional.ElseBlock is null ||
                conditional.Clauses.Any(static clause => BlockCanFallThrough(clause.Body)) ||
                BlockCanFallThrough(conditional.ElseBlock),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.DefaultBlock is null ||
                switchStatement.Clauses.Any(static clause => BlockCanFallThrough(clause.Body)) ||
                BlockCanFallThrough(switchStatement.DefaultBlock),
            PowerShellBoundTryStatement tryStatement when tryStatement.FinallyBlock is not null &&
                                                       !BlockCanFallThrough(tryStatement.FinallyBlock) => false,
            PowerShellBoundTryStatement tryStatement => BlockCanFallThrough(tryStatement.Body) ||
                                                        tryStatement.Catches.Any(static clause => BlockCanFallThrough(clause.Body)),
            _ => true
        };

    internal static bool CanFallThrough(IEnumerable<PowerShellBoundStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (!CanFallThrough(statement)) return false;
        }
        return true;
    }

    private static PowerShellBoundRegionOpportunity Create(
        ParsedSourceDocument document,
        System.Management.Automation.Language.FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> locals,
        IReadOnlyList<System.Management.Automation.Language.StatementAst> authoredStatements,
        PowerShellBoundStatementBinding[] allBindings,
        List<PowerShellBoundStatementBinding> run)
    {
        var statements = run.Select(static item => item.Statement).ToArray();
        var first = statements[0].Span;
        var last = statements[statements.Length - 1].Span;
        var span = new SourceSpan(
            document.DocumentId,
            first.StartOffset,
            last.EndOffset,
            first.StartLine,
            first.StartColumn,
            last.EndLine,
            last.EndColumn);
        var types = parameters.Select(static item => new SymbolFact(item.Symbol, item.Type, item.Contract))
            .Concat(locals.Select(static item => new SymbolFact(item.Symbol, item.Type, null)))
            .GroupBy(static item => item.Symbol.StableKey, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var used = EnumerateReadSymbols(statements)
            .Concat(EnumerateWrittenSymbols(statements))
            .Distinct()
            .Where(symbol => types.ContainsKey(symbol.StableKey))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        var inputs = used.Select(symbol =>
        {
            var fact = types[symbol.StableKey];
            return new PowerShellBoundParameter(
                symbol,
                fact.Type,
                fact.Contract ?? new PowerShellCompilationParameter(
                    symbol.Name,
                    fact.Type.ClrType.FullName ?? fact.Type.ClrType.Name,
                    hasDefaultValue: false));
        }).ToArray();
        var helperName = CreateHelperName(sourceFunction, span);
        var helperSymbol = new PowerShellSymbolId(
            PowerShellSymbolKind.Function,
            document.DocumentId,
            helperName,
            span,
            sourceFunction.Name + "/opportunity/" + span.StartOffset + "/" + span.EndOffset);
        var body = new PowerShellBoundBlock(span, statements);
        var function = new PowerShellBoundFunction(
            helperSymbol,
            inputs,
            Array.Empty<PowerShellBoundLocal>(),
            new PowerShellLexicalScope(helperSymbol, used),
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
        return new PowerShellBoundRegionOpportunity(
            "opportunity-run:" + document.DocumentId + ":" + span.StartOffset + ":" + span.EndOffset,
            ComputeSha256(document.Text),
            document.Path,
            document.Text,
            sourceFunction.Name,
            syntax.Body.Extent.StartLineNumber,
            authoredStatements.Count,
            run[0].AuthoredStatementIndex,
            run[run.Count - 1].AuthoredStatementEndIndex,
            function,
            types.Values.ToArray(),
            allBindings);
    }

    private static PowerShellBoundBlock CreateBlock(IEnumerable<PowerShellBoundStatement> statements)
    {
        var items = statements.ToArray();
        return new PowerShellBoundBlock(items[0].Span, items);
    }

    private static bool BlockCanFallThrough(PowerShellBoundBlock block)
        => CanFallThrough(block.Statements);

    private static string CreateHelperName(PowerShellSymbolId function, SourceSpan span)
    {
        using var sha = SHA256.Create();
        var identity = function.StableKey + "\0opportunity\0" + span.StartOffset + "\0" + span.EndOffset;
        var suffix = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))
            .Take(12)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return "__PowerForgeOpportunity_" + suffix;
    }

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    internal sealed class SymbolFact
    {
        internal SymbolFact(PowerShellSymbolId symbol, PowerShellTypeFact type, PowerShellCompilationParameter? contract)
        {
            Symbol = symbol;
            Type = type;
            Contract = contract;
        }

        internal PowerShellSymbolId Symbol { get; }
        internal PowerShellTypeFact Type { get; }
        internal PowerShellCompilationParameter? Contract { get; }
    }
}

internal sealed class PowerShellBoundRegionOpportunity
{
    internal PowerShellBoundRegionOpportunity(
        string opportunityId,
        string sourceDocumentSha256,
        string sourcePath,
        string sourceText,
        string sourceName,
        int sourceLine,
        int authoredStatementCount,
        int startStatementIndex,
        int endStatementIndex,
        PowerShellBoundFunction regionFunction,
        PowerShellBoundRegionOpportunitySelector.SymbolFact[] symbolFacts,
        PowerShellBoundStatementBinding[] allBindings)
    {
        OpportunityId = opportunityId;
        SourceDocumentSha256 = sourceDocumentSha256;
        SourcePath = sourcePath;
        SourceText = sourceText;
        SourceName = sourceName;
        SourceLine = sourceLine;
        AuthoredStatementCount = authoredStatementCount;
        StartStatementIndex = startStatementIndex;
        EndStatementIndex = endStatementIndex;
        RegionFunction = regionFunction;
        SymbolFacts = symbolFacts ?? Array.Empty<PowerShellBoundRegionOpportunitySelector.SymbolFact>();
        AllBindings = allBindings ?? Array.Empty<PowerShellBoundStatementBinding>();
    }

    internal string OpportunityId { get; }
    internal string SourceDocumentSha256 { get; }
    internal string SourcePath { get; }
    internal string SourceText { get; }
    internal string SourceName { get; }
    internal int SourceLine { get; }
    internal int AuthoredStatementCount { get; }
    internal int StartStatementIndex { get; }
    internal int EndStatementIndex { get; }
    internal PowerShellBoundFunction RegionFunction { get; }
    internal PowerShellImmutableArray<PowerShellBoundRegionOpportunitySelector.SymbolFact> SymbolFacts { get; }
    internal PowerShellImmutableArray<PowerShellBoundStatementBinding> AllBindings { get; }
}

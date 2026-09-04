namespace PowerForge;

/// <summary>
/// Derives coarse execution and transfer evidence from canonical lowered IR. Backends and ledgers
/// consume this graph instead of rediscovering boundary sites from syntax or generated source.
/// </summary>
internal static class PowerShellLoweredRegionGraphBuilder
{
    private static readonly string[] AllPowerShellStreams =
    {
        "Success", "Error", "Warning", "Verbose", "Debug", "Information", "Host"
    };
    private static readonly string[] NonSuccessPowerShellStreams =
    {
        "Error", "Warning", "Verbose", "Debug", "Information", "Host"
    };

    internal static PowerShellCompilationRegionGraph Create(PowerShellLoweredFunction function)
    {
        if (function is null) throw new ArgumentNullException(nameof(function));
        var accumulators = new List<RegionAccumulator>();
        foreach (var statement in function.Statements)
        {
            var execution = Classify(statement);
            if (execution == PowerShellCompilationRegionExecution.Typed &&
                accumulators.LastOrDefault()?.Execution == execution)
            {
                accumulators[accumulators.Count - 1].Add(statement);
            }
            else
            {
                accumulators.Add(new RegionAccumulator(execution, statement));
            }
        }

        var facts = accumulators.Select(CreateFacts).ToArray();
        var regions = new PowerShellCompilationRegion[facts.Length];
        for (var index = 0; index < facts.Length; index++)
        {
            var fact = facts[index];
            var laterInputs = facts.Skip(index + 1)
                .SelectMany(static later => later.Inputs)
                .ToHashSet(StringComparer.Ordinal);
            var outputs = fact.Mutations.Where(laterInputs.Contains).ToList();
            if (fact.Streams.Contains("Success", StringComparer.Ordinal)) outputs.Add("stream:Success");
            regions[index] = new PowerShellCompilationRegion(
                CreateRegionId(fact.Span, fact.Execution),
                index,
                fact.Execution,
                fact.Span.StartOffset,
                fact.Span.EndOffset,
                fact.Span.StartLine,
                fact.Span.StartColumn,
                fact.Span.EndLine,
                fact.Span.EndColumn,
                fact.Inputs,
                outputs.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
                fact.Mutations,
                fact.Streams,
                fact.Errors,
                fact.Execution == PowerShellCompilationRegionExecution.Typed
                    ? "AuthoredSequentialSingleEvaluation"
                    : "AuthoredSequentialSingleEvaluation+PowerShellStreamOrder",
                fact.HostedCommandBoundarySites,
                fact.ModuleStateReadBoundarySites,
                fact.ModuleStateWriteBoundarySites);
        }
        return new PowerShellCompilationRegionGraph(regions);
    }

    internal static PowerShellCompilationRegionGraph Remap(
        PowerShellCompilationRegionGraph graph,
        string authoredDocumentId,
        string authoredText,
        IReadOnlyList<PowerShellRegionSourceRemap> mappings)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));
        if (authoredText is null) throw new ArgumentNullException(nameof(authoredText));
        if (mappings is null) throw new ArgumentNullException(nameof(mappings));
        var regions = graph.Regions.Select(region =>
        {
            var startOffset = MapOffset(region.StartOffset, mappings);
            var endOffset = MapOffset(region.EndOffset, mappings);
            var start = GetLineColumn(authoredText, startOffset);
            var end = GetLineColumn(authoredText, endOffset);
            var span = new SourceSpan(
                authoredDocumentId,
                startOffset,
                endOffset,
                start.Line,
                start.Column,
                end.Line,
                end.Column);
            return new PowerShellCompilationRegion(
                CreateRegionId(span, region.Execution),
                region.Ordinal,
                region.Execution,
                startOffset,
                endOffset,
                start.Line,
                start.Column,
                end.Line,
                end.Column,
                region.Inputs,
                region.Outputs,
                region.Mutations,
                region.Streams,
                region.Errors,
                region.Ordering,
                region.HostedCommandBoundarySites,
                region.ModuleStateReadBoundarySites,
                region.ModuleStateWriteBoundarySites);
        }).ToArray();
        return new PowerShellCompilationRegionGraph(regions);
    }

    internal static int CountHostedCommandBoundarySites(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredTreeEnumerator.EnumerateStatements(statements).Count(static statement =>
               statement is PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement) +
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements).Count(static expression =>
               expression is PowerShellLoweredCommandAvailabilityExpression or PowerShellLoweredHostedBooleanCommandExpression ||
               expression is PowerShellLoweredInvocationExpression { RequiresPowerShellCommandRegions: true });

    private static PowerShellCompilationRegionExecution Classify(PowerShellLoweredStatement statement)
    {
        if (statement is PowerShellLoweredCommandRegionStatement or PowerShellLoweredCommandCaptureStatement)
            return PowerShellCompilationRegionExecution.Hosted;
        return CountStaticBoundarySites(new[] { statement }) == 0
            ? PowerShellCompilationRegionExecution.Typed
            : PowerShellCompilationRegionExecution.Mixed;
    }

    private static RegionFacts CreateFacts(RegionAccumulator accumulator)
    {
        var statements = accumulator.Statements.ToArray();
        var readOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var writeOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var expression in PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements))
        {
            if (expression is PowerShellLoweredVariableExpression variable)
                RecordFirst(readOffsets, Symbol(variable.Symbol), expression.Span.StartOffset);
            if (expression is PowerShellLoweredMutationExpression mutation)
            {
                RecordFirst(writeOffsets, Symbol(mutation.Target), expression.Span.EndOffset);
                RecordFirst(readOffsets, Symbol(mutation.Target), expression.Span.StartOffset);
            }
            if (expression is PowerShellLoweredInvocationExpression invocation)
            {
                var callBoundary = "LocalCall:" + invocation.Target.Name.ToUpperInvariant();
                if (invocation.RequiresPowerShellModuleStateRead)
                    RecordFirst(readOffsets, callBoundary + "/ModuleStateRead", expression.Span.StartOffset);
                if (invocation.RequiresPowerShellModuleStateWrite)
                    RecordFirst(writeOffsets, callBoundary + "/ModuleStateWrite", expression.Span.EndOffset);
            }
            if (expression is PowerShellLoweredClrInvocationExpression
                {
                    InvocationKind: PowerShellClrInvocationKind.InstanceMethod,
                    Receiver: not null
                } clrInvocation)
                RecordFirst(writeOffsets, MutationTarget(clrInvocation.Receiver, ".*"), expression.Span.EndOffset);
        }
        foreach (var statement in PowerShellLoweredTreeEnumerator.EnumerateStatements(statements))
        {
            switch (statement)
            {
                case PowerShellLoweredAssignmentStatement assignment:
                    RecordFirst(writeOffsets, Symbol(assignment.Target), statement.Span.EndOffset);
                    if (assignment.Operation != PowerShellBoundMutationOperator.Assign)
                        RecordFirst(readOffsets, Symbol(assignment.Target), statement.Span.StartOffset);
                    break;
                case PowerShellLoweredLocalDeclarationStatement declaration:
                    RecordFirst(writeOffsets, Symbol(declaration.Symbol), statement.Span.StartOffset);
                    break;
                case PowerShellLoweredCommandCaptureStatement capture:
                    RecordFirst(writeOffsets, Symbol(capture.Target), statement.Span.EndOffset);
                    foreach (var argument in capture.Arguments)
                        RecordFirst(readOffsets, Symbol(argument.Symbol), statement.Span.StartOffset);
                    break;
                case PowerShellLoweredCommandRegionStatement region:
                    foreach (var argument in region.Arguments)
                        RecordFirst(readOffsets, Symbol(argument.Symbol), statement.Span.StartOffset);
                    break;
                case PowerShellLoweredModuleVariableAssignmentStatement moduleWrite:
                    RecordFirst(writeOffsets, ModuleState(moduleWrite.Name), statement.Span.EndOffset);
                    break;
                case PowerShellLoweredForEachStatement loop:
                    RecordFirst(writeOffsets, Symbol(loop.Variable), statement.Span.StartOffset);
                    break;
                case PowerShellLoweredIndexAssignmentStatement index:
                    RecordFirst(writeOffsets, MutationTarget(index.Target, "[*]"), statement.Span.EndOffset);
                    break;
                case PowerShellLoweredClrMemberAssignmentStatement member:
                    RecordFirst(writeOffsets, member.Receiver is null
                        ? "static:" + TypeName(member.DeclaringType) + "." + member.MemberName
                        : MutationTarget(member.Receiver, "." + member.MemberName), statement.Span.EndOffset);
                    break;
            }
        }
        foreach (var read in PowerShellLoweredModuleStateCollector.EnumerateReadSites(statements))
        {
            if (read.Arguments.FirstOrDefault() is PowerShellLoweredLiteralExpression { Value: string name } &&
                !string.IsNullOrWhiteSpace(name))
                RecordFirst(readOffsets, ModuleState(name), read.Span.StartOffset);
        }

        var inputs = readOffsets
            .Where(pair => !writeOffsets.TryGetValue(pair.Key, out var writeOffset) || writeOffset > pair.Value)
            .Select(static pair => pair.Key)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        var mutations = writeOffsets.Keys.OrderBy(static item => item, StringComparer.Ordinal).ToArray();
        var streams = GetStreams(statements);
        var errors = GetErrors(statements);
        return new RegionFacts(
            accumulator.Execution,
            accumulator.Span,
            inputs,
            mutations,
            streams,
            errors,
            CountHostedCommandBoundarySites(statements),
            CountModuleStateReadBoundarySites(statements),
            CountModuleStateWriteBoundarySites(statements));
    }

    private static string[] GetStreams(PowerShellLoweredStatement[] statements)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (CountHostedCommandBoundarySites(statements) > 0) result.UnionWith(NonSuccessPowerShellStreams);
        foreach (var statement in PowerShellLoweredTreeEnumerator.EnumerateStatements(statements))
        {
            if (statement is PowerShellLoweredCommandRegionStatement) result.Add("Success");
            if (statement is PowerShellLoweredReturnStatement { EmitsValue: true } ||
                statement is PowerShellLoweredExpressionStatement { DiscardValue: false })
                result.Add("Success");
            if (statement is PowerShellLoweredStreamWriteStatement stream)
                result.Add(stream.Kind.ToString());
        }
        if (PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
            .OfType<PowerShellLoweredInvocationExpression>()
            .Any(static invocation => invocation.RequiresPowerShellStreams))
            result.UnionWith(AllPowerShellStreams);
        return AllPowerShellStreams.Where(result.Contains).ToArray();
    }

    private static string[] GetErrors(PowerShellLoweredStatement[] statements)
    {
        var result = new List<string>();
        if (CountHostedCommandBoundarySites(statements) > 0) result.Add("PowerShellErrorRecord");
        if (CountModuleStateReadBoundarySites(statements) + CountModuleStateWriteBoundarySites(statements) > 0)
            result.Add("PowerShellModuleStateErrorRecord");
        if (PowerShellLoweredTreeEnumerator.EnumerateStatements(statements)
            .Any(static statement => statement is PowerShellLoweredThrowStatement))
            result.Add("TypedThrow");
        if (PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
            .OfType<PowerShellLoweredConversionExpression>()
            .Any(static conversion => conversion.UsePowerShellLanguageRuntime))
            result.Add("PowerShellLanguageRuntimeError");
        if (PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements).Any(CanThrowClr) ||
            PowerShellLoweredTreeEnumerator.EnumerateStatements(statements).Any(static statement =>
                statement is PowerShellLoweredIndexAssignmentStatement or PowerShellLoweredClrMemberAssignmentStatement))
            result.Add("ClrException");
        return result.Distinct(StringComparer.Ordinal).OrderBy(static item => item, StringComparer.Ordinal).ToArray();
    }

    private static bool CanThrowClr(PowerShellLoweredExpression expression)
        => expression is PowerShellLoweredConversionExpression
            or PowerShellLoweredBinaryExpression
            or PowerShellLoweredUnaryExpression
            or PowerShellLoweredRegexExpression
            or PowerShellLoweredWildcardExpression
            or PowerShellLoweredMembershipExpression
            or PowerShellLoweredStringSplitExpression
            or PowerShellLoweredStringJoinExpression
            or PowerShellLoweredArrayConcatenationExpression
            or PowerShellLoweredDictionaryExpression
            or PowerShellLoweredIndexExpression
            or PowerShellLoweredClrMemberExpression
            or PowerShellLoweredClrInvocationExpression;

    private static int CountModuleStateReadBoundarySites(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredModuleStateCollector.CountReadSites(statements) +
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
               .OfType<PowerShellLoweredInvocationExpression>()
               .Count(static invocation => invocation.RequiresPowerShellModuleStateRead);

    private static int CountModuleStateWriteBoundarySites(IEnumerable<PowerShellLoweredStatement> statements)
        => PowerShellLoweredModuleStateCollector.CountWriteSites(statements) +
           PowerShellLoweredTreeEnumerator.EnumerateExpressions(statements)
               .OfType<PowerShellLoweredInvocationExpression>()
               .Count(static invocation => invocation.RequiresPowerShellModuleStateWrite);

    private static int CountStaticBoundarySites(IEnumerable<PowerShellLoweredStatement> statements)
        => CountHostedCommandBoundarySites(statements) +
           CountModuleStateReadBoundarySites(statements) +
           CountModuleStateWriteBoundarySites(statements);

    private static string MutationTarget(PowerShellLoweredExpression expression, string suffix)
        => expression is PowerShellLoweredVariableExpression variable
            ? Symbol(variable.Symbol)
            : "object" + suffix;

    private static string Symbol(PowerShellSymbolId symbol) => symbol.Kind + ":" + symbol.Name.ToUpperInvariant();
    private static string ModuleState(string name) => "ModuleState:" + name.ToUpperInvariant();
    private static string TypeName(Type type) => type.FullName ?? type.Name;
    internal static string CreateRegionId(SourceSpan span, PowerShellCompilationRegionExecution execution)
        => "region:" + span.DocumentId + ":" + span.StartOffset + ":" + span.EndOffset + ":" + execution;

    private static void RecordFirst(IDictionary<string, int> values, string name, int offset)
    {
        if (!values.TryGetValue(name, out var existing) || offset < existing) values[name] = offset;
    }

    private static int MapOffset(int syntheticOffset, IReadOnlyList<PowerShellRegionSourceRemap> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (syntheticOffset < mapping.SyntheticStartOffset || syntheticOffset > mapping.SyntheticEndOffset)
                continue;
            var relativeOffset = Math.Min(
                syntheticOffset - mapping.SyntheticStartOffset,
                mapping.AuthoredEndOffset - mapping.AuthoredStartOffset);
            return mapping.AuthoredStartOffset + relativeOffset;
        }
        throw new InvalidOperationException(
            $"A lowered executable region offset ({syntheticOffset}) has no authored source mapping.");
    }

    private static (int Line, int Column) GetLineColumn(string text, int offset)
    {
        if (offset < 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return (line, column);
    }

    private sealed class RegionAccumulator
    {
        internal RegionAccumulator(PowerShellCompilationRegionExecution execution, PowerShellLoweredStatement statement)
        {
            Execution = execution;
            Statements.Add(statement);
            Span = statement.Span;
        }

        internal PowerShellCompilationRegionExecution Execution { get; }
        internal List<PowerShellLoweredStatement> Statements { get; } = new();
        internal SourceSpan Span { get; private set; }

        internal void Add(PowerShellLoweredStatement statement)
        {
            Statements.Add(statement);
            var beginsEarlier = statement.Span.StartOffset < Span.StartOffset;
            var endsLater = statement.Span.EndOffset > Span.EndOffset;
            Span = new SourceSpan(
                Span.DocumentId,
                beginsEarlier ? statement.Span.StartOffset : Span.StartOffset,
                endsLater ? statement.Span.EndOffset : Span.EndOffset,
                beginsEarlier ? statement.Span.StartLine : Span.StartLine,
                beginsEarlier ? statement.Span.StartColumn : Span.StartColumn,
                endsLater ? statement.Span.EndLine : Span.EndLine,
                endsLater ? statement.Span.EndColumn : Span.EndColumn);
        }
    }

    private sealed record RegionFacts(
        PowerShellCompilationRegionExecution Execution,
        SourceSpan Span,
        string[] Inputs,
        string[] Mutations,
        string[] Streams,
        string[] Errors,
        int HostedCommandBoundarySites,
        int ModuleStateReadBoundarySites,
        int ModuleStateWriteBoundarySites);
}

internal readonly struct PowerShellRegionSourceRemap
{
    internal PowerShellRegionSourceRemap(
        int syntheticStartOffset,
        int syntheticEndOffset,
        int authoredStartOffset,
        int authoredEndOffset)
    {
        SyntheticStartOffset = syntheticStartOffset;
        SyntheticEndOffset = syntheticEndOffset;
        AuthoredStartOffset = authoredStartOffset;
        AuthoredEndOffset = authoredEndOffset;
    }

    internal int SyntheticStartOffset { get; }
    internal int SyntheticEndOffset { get; }
    internal int AuthoredStartOffset { get; }
    internal int AuthoredEndOffset { get; }
}

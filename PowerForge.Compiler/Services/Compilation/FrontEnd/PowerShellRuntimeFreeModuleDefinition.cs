using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Declares one managed module's persistent storage and authored initializer syntax.</summary>
/// <remarks>Initializer operations still pass through the ordinary binder, analysis, and lowering.</remarks>
internal sealed class PowerShellRuntimeFreeModuleDefinition
{
    private PowerShellRuntimeFreeModuleDefinition(ParsedSourceDocument document,
        PowerShellBoundLocal[] fields, FunctionDefinitionAst initializer)
    {
        Document = document;
        Fields = fields;
        Initializer = initializer;
    }

    internal ParsedSourceDocument Document { get; }
    internal PowerShellImmutableArray<PowerShellBoundLocal> Fields { get; }
    internal FunctionDefinitionAst Initializer { get; }

    internal static PowerShellRuntimeFreeModuleDefinition? Discover(
        IReadOnlyList<ParsedSourceDocument> documents, ICollection<PowerShellSemanticDiagnostic> diagnostics,
        PowerShellCommandSemanticResolver? commandResolver = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapabilities.TypedLibrary)
    {
        commandResolver ??= new PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry.Default);
        var localFunctionNames = documents.SelectMany(static document => document.SyntaxRoot.FindAll(
                static syntax => syntax is FunctionDefinitionAst, false).Cast<FunctionDefinitionAst>())
            .Select(static function => function.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Analyzer-only wrappers are views of these same statements, not additional modules.
        var candidates = documents.Where(static document => document.Errors.Length == 0 &&
                !document.Path.EndsWith(".powerforge-analysis.ps1", StringComparison.OrdinalIgnoreCase))
            .Select(document => (Document: document, Statements: GetInitializerStatements(document.SyntaxRoot, commandResolver, localFunctionNames, capabilities)))
            .Where(static candidate => candidate.Statements.Length > 0).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1)
        {
            foreach (var candidate in candidates)
                Report(candidate.Document, candidate.Document.SyntaxRoot,
                    "A managed module requires one explicit initialization unit; ordering multiple initialization files is not yet qualified.", diagnostics);
            return null;
        }
        var source = candidates[0].Document;
        var statements = candidates[0].Statements;
        var root = source.SyntaxRoot;
        foreach (var parameter in root.ParamBlock?.Parameters ?? Enumerable.Empty<ParameterAst>())
        {
            if (!IsSupportedFieldType(parameter.StaticType))
                Report(source, parameter,
                    "Managed module constructor parameters require explicitly typed immutable scalar values.", diagnostics);
        }
        if (root.BeginBlock is not null || root.ProcessBlock is not null || root.DynamicParamBlock is not null ||
            root.GetType().GetProperty("CleanBlock")?.GetValue(root) is not null || root.EndBlock?.Traps?.Count > 0)
        {
            Report(source, root, "Managed module initialization requires an ordinary statement body without lifecycle clauses or traps.", diagnostics);
            return null;
        }
        var fields = new List<PowerShellBoundLocal>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in statements)
        {
            if (statement is not AssignmentStatementAst assignment) continue;
            if (assignment.Operator != TokenKind.Equals || assignment.Left is not ConvertExpressionAst
                { Child: VariableExpressionAst variable } declaration || !variable.VariablePath.IsScript ||
                !IsSupportedFieldType(declaration.StaticType))
            {
                Report(source, statement,
                    "Persistent module declarations require one explicitly typed scalar '$script:Name = value' assignment.", diagnostics);
                continue;
            }
            var name = variable.VariablePath.UserPath.Substring(7);
            if (PowerShellAssignmentTargetPolicy.IsReadOnlyAutomaticVariable(name) || name.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                Report(source, declaration, "Automatic variables and the null discard target cannot define managed module fields.", diagnostics);
                continue;
            }
            if (!IsSimpleName(name) || !names.Add(name))
            {
                Report(source, declaration, "Managed module fields require unique simple names under PowerShell's case-insensitive rules.", diagnostics);
                continue;
            }
            var span = PowerShellSourceParser.GetSpan(source, declaration.Extent);
            fields.Add(new PowerShellBoundLocal(new PowerShellSymbolId(PowerShellSymbolKind.ModuleState,
                source.DocumentId, name, span), new PowerShellTypeFact(declaration.StaticType,
                PowerShellTypeFactProvenance.Explicit, "An authored module declaration fixes this instance field's CLR representation.")));
        }
        foreach (var function in documents.SelectMany(static document => document.SyntaxRoot.FindAll(
                     static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)).Cast<FunctionDefinitionAst>())
        {
            var mapped = PowerShellClrSymbolMapper.MapIdentifier(function.Name);
            if (IsReservedMemberName(mapped))
                Report(source, root, "The authored function collides with a reserved managed module lifetime member.", diagnostics);
        }
        PowerShellRuntimeFreeModuleScopePolicy.Validate(documents, fields, diagnostics);
        var initializerName = "__PowerForgeInitializeModule_" + source.DocumentId.Substring(0, 16);
        if (documents.Any(document => document.SyntaxRoot.Find(static node => node is FunctionDefinitionAst, false) is not null &&
            document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst, false).Cast<FunctionDefinitionAst>()
                .Any(function => function.Name.Equals(initializerName, StringComparison.OrdinalIgnoreCase))))
        {
            Report(source, root, "The authored function name collides with the managed module initializer identity.", diagnostics);
            return null;
        }
        var body = new ScriptBlockAst(root.Extent, (ParamBlockAst?)root.ParamBlock?.Copy(),
            new StatementBlockAst(root.Extent, statements.Select(static statement => (StatementAst)statement.Copy()), null), false);
        var initializer = new FunctionDefinitionAst(root.Extent, false, false, initializerName, null, body);
        return new PowerShellRuntimeFreeModuleDefinition(source, fields.ToArray(), initializer);
    }

    private static StatementAst[] GetInitializerStatements(ScriptBlockAst root,
        PowerShellCommandSemanticResolver resolver, ISet<string> localFunctions, PowerShellCompilationCapability capabilities)
        => (root.EndBlock?.Statements ?? Enumerable.Empty<StatementAst>())
            .Where(statement => statement is not FunctionDefinitionAst && !IsDeclarationDirective(statement, resolver, localFunctions, capabilities)).ToArray();

    private static bool IsDeclarationDirective(StatementAst statement, PowerShellCommandSemanticResolver resolver,
        ISet<string> localFunctions, PowerShellCompilationCapability capabilities)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst command &&
           command.InvocationOperator == TokenKind.Unknown && command.Redirections.Count == 0 &&
           command.GetCommandName()?.Equals("Export-ModuleMember", StringComparison.OrdinalIgnoreCase) == true &&
           resolver.Resolve(command, localFunctions, capabilities).Origin == PowerShellCommandSemanticOrigin.Missing &&
           command.CommandElements.Skip(1).All(IsLiteralExportArgument);

    // Only static export metadata can be omitted from the initializer. Dynamic arguments and
    // dot sourcing are executable syntax and must remain visible to canonical binding.
    private static bool IsLiteralExportArgument(CommandElementAst element)
        => element switch
        {
            StringConstantExpressionAst => true,
            ArrayLiteralAst array => array.Elements.All(static value => value is StringConstantExpressionAst),
            CommandParameterAst parameter =>
                (parameter.ParameterName.Equals("Function", StringComparison.OrdinalIgnoreCase) ||
                 parameter.ParameterName.Equals("Alias", StringComparison.OrdinalIgnoreCase) ||
                 parameter.ParameterName.Equals("Variable", StringComparison.OrdinalIgnoreCase) ||
                 parameter.ParameterName.Equals("Cmdlet", StringComparison.OrdinalIgnoreCase)) &&
                (parameter.Argument is null || IsLiteralExportArgument(parameter.Argument)),
            _ => false
        };

    private static bool IsSimpleName(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') &&
           name.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    internal static bool IsReservedMemberName(string name)
        => name is "Dispose" or "Reset" or "__ResetCore" or "__ThrowIfDisposed" or "__ThrowIfActive" or "__ModuleState" or
            "__moduleSync" or "__moduleState" or "__moduleDisposed" or "__moduleActiveCalls";

    private static bool IsSupportedFieldType(Type type)
        => type == typeof(string) || type == typeof(bool) || type == typeof(char) ||
           type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
           type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
           type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
           type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid);

    private static void Report(ParsedSourceDocument document, Ast syntax, string message,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2950", message,
            PowerShellSourceParser.GetSpan(document, syntax.Extent)));
}

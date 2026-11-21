using System.Management.Automation.Language;
using PowerForge.Generated.Runtime;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private bool TryBindStrictDataCommand(ParsedSourceDocument document, CommandAst command,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        out PowerShellBoundExpression? expression)
    {
        expression = null;
        var names = functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (PowerShellStrictCsvPolicy.TryGetRead(command, names, out var csvPath, out var headers, out var literalCsvPath))
        {
            var path = BindString(csvPath);
            if (path is null) return true;
            var headerArray = new PowerShellBoundArrayExpression(span, typeof(string[]), PowerShellBoundArrayKind.Literal,
                headers.Select(header => (PowerShellBoundExpression)Literal(header)).ToArray());
            expression = BindIntrinsic(typeof(PowerShellStrictCsvReader), nameof(PowerShellStrictCsvReader.ReadFile),
                PowerShellStrictCsvPolicy.GetCollectionType(headers), path, headerArray, Literal(literalCsvPath),
                Literal(PowerShellStrictCatchObservation.GetAuthoredLine(document, command.Extent)));
            return true;
        }
        if (PowerShellStrictFileCommandPolicy.TryGetResolvePath(command, names, out var authoredPath, out var literalPath))
        {
            var assignment = (AssignmentStatementAst)command.Parent.Parent;
            var target = (VariableExpressionAst)assignment.Left;
            if (!symbols.TryGetValue(target.VariablePath.UserPath, out var binding) ||
                binding.Type.ClrType != typeof(string) || binding.Type.Provenance != PowerShellTypeFactProvenance.Explicit)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("command.resolve-path", "Strict Resolve-Path requires an assignment to a constrained string variable.", span));
                return true;
            }
            var path = BindString(authoredPath);
            if (path is null) return true;
            var source = document.AuthoredProjection?.Select(document, new[] { command.Extent }) ??
                new PowerShellCommandRegionSourceSelection(document.Path, document.Text, new[] { span });
            var authored = source.Statements.Single();
            var line = source.Document.Split('\n')[authored.StartLine - 1].TrimEnd('\r');
            var length = authored.StartLine == authored.EndLine ? authored.EndColumn - authored.StartColumn :
                line.Length - authored.StartColumn + 1;
            expression = BindIntrinsic(typeof(PowerShellStrictFileSystemReader), nameof(PowerShellStrictFileSystemReader.ResolvePath),
                StrictPipelineType(typeof(string)), path, Literal(literalPath), Literal(Path.GetFileName(source.Path)),
                Literal(authored.StartLine), Literal(authored.StartColumn), Literal(length), Literal(line),
                Literal(command.GetCommandName()!),
                Literal(_semanticProfile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51));
            return true;
        }
        if (PowerShellStrictFileCommandPolicy.TryGetProgress(command, names, out var activity, out var completed))
        {
            var label = BindString(activity);
            if (label is null) return true;
            expression = BindIntrinsic(typeof(PowerShellStrictConsoleProgress), nameof(PowerShellStrictConsoleProgress.Write),
                StrictPipelineType(typeof(void)), label, Literal(completed));
            return true;
        }
        return false;

        PowerShellBoundExpression? BindString(ExpressionAst operand)
        {
            var value = BindExpression(document, operand, symbols, functions, diagnostics, typeof(string),
                targetFramework, PowerShellCompilationCapabilities.TypedExecutable);
            if (value is null || value.Type.ClrType == typeof(string)) return value;
            diagnostics.Add(new PowerShellSemanticDiagnostic(PowerShellCompilationFeatureIds.ForCommand(command.GetCommandName()!),
                "The Strict command requires a typed string operand.", value.Span));
            return null;
        }

        PowerShellBoundLiteralExpression Literal(object value) => new(span, value,
            LiteralType(value.GetType(), "The authored command determines this argument."), PowerShellValueState.Known);

        PowerShellBoundClrInvocationExpression BindIntrinsic(Type owner, string name, PowerShellTypeFact result,
            params PowerShellBoundExpression[] arguments) => new(span, owner, name, PowerShellClrInvocationKind.StaticMethod,
            null, PowerShellClrReceiverBehavior.None, arguments, arguments.Select(static item => item.Type.ClrType).ToArray(), result);
    }
}

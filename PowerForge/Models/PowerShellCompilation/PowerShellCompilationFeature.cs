using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PowerForge;

/// <summary>Stable identifiers used to aggregate compilation blockers by missing engine capability.</summary>
public static class PowerShellCompilationFeatureIds
{
    /// <summary>Input discovery or access.</summary>
    public const string Input = "input.discovery";
    /// <summary>PowerShell parser acceptance.</summary>
    public const string Parser = "parser.syntax";
    /// <summary>Static parameter type support.</summary>
    public const string ParameterType = "parameter.type";
    /// <summary>Parameter default-value lowering.</summary>
    public const string ParameterDefault = "parameter.default";
    /// <summary>Parameter attributes and validation metadata.</summary>
    public const string ParameterMetadata = "parameter.metadata";
    /// <summary>Parameter name and alias binding.</summary>
    public const string ParameterBinding = "parameter.binding";
    /// <summary>Dynamic command resolution.</summary>
    public const string DynamicCommand = "command.dynamic";
    /// <summary>Nested script blocks and lambdas.</summary>
    public const string ScriptBlock = "scriptblock.typed";
    /// <summary>Dynamic PowerShell scope and automatic state.</summary>
    public const string RuntimeScope = "runtime.scope";
    /// <summary>PowerShell conversion semantics.</summary>
    public const string Conversion = "expression.conversion";
    /// <summary>Expandable-string token and interpolation semantics.</summary>
    public const string ExpandableString = "expression.expandable-string";
    /// <summary>Assignment to indexed, member, or otherwise rich targets.</summary>
    public const string AssignmentTarget = "assignment.target";
    /// <summary>Assignment to automatic or runtime-owned variables.</summary>
    public const string AutomaticVariableAssignment = "assignment.automatic-variable";
    /// <summary>Switch matching flags.</summary>
    public const string SwitchFlags = "control-flow.switch-flags";
    /// <summary>Typed catch-filter resolution.</summary>
    public const string CatchFilter = "exception.catch-filter";
    /// <summary>PowerShell begin/process/clean pipeline lifecycle blocks.</summary>
    public const string PipelineLifecycle = "pipeline.lifecycle";
    /// <summary>Runtime-bearing using statements.</summary>
    public const string RuntimeUsing = "source.using-runtime";
    /// <summary>PowerShell class or enum declarations whose runtime type identity remains hosted.</summary>
    public const string TypeDefinition = "source.type-definition";
    /// <summary>Source #requires directives.</summary>
    public const string RequiresDirective = "source.requires";
    /// <summary>Filter function pipeline semantics.</summary>
    public const string FilterFunction = "function.filter";
    /// <summary>CLR identifier collisions between source functions.</summary>
    public const string FunctionNameCollision = "function.name-collision";
    /// <summary>Conservative whole-function graph emission.</summary>
    public const string FunctionGraph = "function.graph";
    /// <summary>Authored comment-based help on generated binary cmdlets.</summary>
    public const string CommentBasedHelp = "function.comment-help";
    /// <summary>Generated binary-cmdlet shape requirements.</summary>
    public const string BinaryCmdletShape = "binary-module.cmdlet-shape";
    /// <summary>Typed dictionary values flowing beyond supported lookup and mutation contexts.</summary>
    public const string DictionaryFlow = "collection.dictionary-flow";

    /// <summary>Returns a stable feature id for one statically named PowerShell command.</summary>
    public static string ForCommand(string commandName) => "command." + NormalizeSegment(commandName);

    /// <summary>Returns a stable feature id for one PowerShell operator.</summary>
    public static string ForOperator(string operatorName) => "operator." + NormalizeSegment(operatorName);

    /// <summary>Returns a stable feature id for one unsupported syntax-node family.</summary>
    public static string ForSyntax(string syntaxName) => "syntax." + NormalizeSegment(RemoveAstSuffix(syntaxName));

    internal static string Resolve(PowerShellCompilationDiagnosticCode code, string message, string? explicitFeatureId)
    {
        if (!string.IsNullOrWhiteSpace(explicitFeatureId))
            return NormalizeFeatureId(explicitFeatureId!);

        switch (code)
        {
            case PowerShellCompilationDiagnosticCode.InputError:
                return Input;
            case PowerShellCompilationDiagnosticCode.ParseError:
                return Parser;
            case PowerShellCompilationDiagnosticCode.UnsupportedParameterType:
                return ParameterType;
            case PowerShellCompilationDiagnosticCode.DynamicCommandInvocation:
                return DynamicCommand;
            case PowerShellCompilationDiagnosticCode.CommandInvocation:
                return TryExtractQuotedValue(message, "Command invocation '", out var command)
                    ? ForCommand(command)
                    : "command.invocation";
            case PowerShellCompilationDiagnosticCode.ScriptBlock:
                return ScriptBlock;
            case PowerShellCompilationDiagnosticCode.RuntimeScope:
                return RuntimeScope;
            case PowerShellCompilationDiagnosticCode.UnsupportedOperator:
                return TryExtractQuotedValue(message, "operator '", out var operatorName)
                    ? ForOperator(operatorName)
                    : "operator.unsupported";
            case PowerShellCompilationDiagnosticCode.UnsupportedSyntax:
                return ClassifyUnsupportedSyntax(message);
            default:
                return "diagnostic." + NormalizeSegment(code.ToString());
        }
    }

    private static string ClassifyUnsupportedSyntax(string message)
    {
        if (message.IndexOf("default value", StringComparison.OrdinalIgnoreCase) >= 0) return ParameterDefault;
        if (message.IndexOf("parameter alias", StringComparison.OrdinalIgnoreCase) >= 0) return ParameterBinding;
        if (message.IndexOf("attribute", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("validation", StringComparison.OrdinalIgnoreCase) >= 0) return ParameterMetadata;
        if (message.IndexOf("conversion", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Typed local declaration", StringComparison.OrdinalIgnoreCase) >= 0) return Conversion;
        if (message.IndexOf("Expandable string", StringComparison.OrdinalIgnoreCase) >= 0) return ExpandableString;
        if (message.IndexOf("member mutation", StringComparison.OrdinalIgnoreCase) >= 0) return AssignmentTarget;
        if (message.IndexOf("assignment target", StringComparison.OrdinalIgnoreCase) >= 0) return AssignmentTarget;
        if (message.IndexOf("read-only automatic variable", StringComparison.OrdinalIgnoreCase) >= 0) return AutomaticVariableAssignment;
        if (message.IndexOf("Switch flags", StringComparison.OrdinalIgnoreCase) >= 0) return SwitchFlags;
        if (message.IndexOf("catch filter", StringComparison.OrdinalIgnoreCase) >= 0) return CatchFilter;
        if (message.IndexOf("pipeline lifecycle", StringComparison.OrdinalIgnoreCase) >= 0) return PipelineLifecycle;
        if (message.IndexOf("runtime-bearing using", StringComparison.OrdinalIgnoreCase) >= 0) return RuntimeUsing;
        if (message.IndexOf("#requires", StringComparison.OrdinalIgnoreCase) >= 0) return RequiresDirective;
        if (message.IndexOf("Filter '", StringComparison.OrdinalIgnoreCase) >= 0) return FilterFunction;
        if (message.IndexOf("identifier normalization", StringComparison.OrdinalIgnoreCase) >= 0) return FunctionNameCollision;
        if (message.IndexOf("multiple retained definitions", StringComparison.OrdinalIgnoreCase) >= 0) return FunctionNameCollision;
        if (message.IndexOf("function-graph emission", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("local-call cycle", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("depends on local function", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (message.IndexOf("Local function", StringComparison.OrdinalIgnoreCase) >= 0 &&
             message.IndexOf("pipeline cardinality", StringComparison.OrdinalIgnoreCase) >= 0) ||
            message.IndexOf("command-availability timing", StringComparison.OrdinalIgnoreCase) >= 0) return FunctionGraph;
        if (message.IndexOf("comment-based help", StringComparison.OrdinalIgnoreCase) >= 0) return CommentBasedHelp;
        if (message.IndexOf("Typed dictionary local", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Typed hashtable literal", StringComparison.OrdinalIgnoreCase) >= 0) return DictionaryFlow;
        if (message.IndexOf("PipelineAst", StringComparison.OrdinalIgnoreCase) >= 0) return ForSyntax("PipelineAst");
        if (message.IndexOf("CLR member", StringComparison.OrdinalIgnoreCase) >= 0) return ForSyntax("MemberExpressionAst");
        if (message.IndexOf("CLR overload", StringComparison.OrdinalIgnoreCase) >= 0) return ForSyntax("InvokeMemberExpressionAst");
        if (message.IndexOf("foreach currently requires", StringComparison.OrdinalIgnoreCase) >= 0) return ForSyntax("ForEachStatementAst");
        if (message.IndexOf("must be declared at function scope", StringComparison.OrdinalIgnoreCase) >= 0) return ForSyntax("VariableExpressionAst");
        if (message.IndexOf("Increment or decrement", StringComparison.OrdinalIgnoreCase) >= 0) return ForOperator("increment");
        if (message.IndexOf("cmdlet", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("common parameters", StringComparison.OrdinalIgnoreCase) >= 0) return BinaryCmdletShape;
        if (TryExtractQuotedValue(message, "Operator '-", out var operatorName)) return ForOperator(operatorName);
        if (TryExtractQuotedValue(message, "Syntax node '", out var syntax)) return ForSyntax(syntax);
        if (TryExtractQuotedValue(message, "Expression '", out syntax)) return ForSyntax(syntax);
        if (TryExtractQuotedValue(message, "Statement '", out syntax)) return ForSyntax(syntax);
        return "syntax.unsupported";
    }

    private static bool TryExtractQuotedValue(string message, string prefix, out string value)
    {
        var start = message.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            value = string.Empty;
            return false;
        }
        start += prefix.Length;
        var end = message.IndexOf('\'', start);
        if (end <= start)
        {
            value = string.Empty;
            return false;
        }
        value = message.Substring(start, end - start);
        return value.Length > 0;
    }

    private static string NormalizeFeatureId(string value)
        => string.Join(".", value.Trim().Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).Select(NormalizeSegment));

    private static string NormalizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        var priorWasSeparator = false;
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                priorWasSeparator = false;
            }
            else if (!priorWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                priorWasSeparator = true;
            }
        }
        return builder.ToString().Trim('-');
    }

    private static string RemoveAstSuffix(string value)
        => value.EndsWith("Ast", StringComparison.Ordinal) ? value.Substring(0, value.Length - 3) : value;
}

/// <summary>Observed impact of one missing compiler capability in a census.</summary>
public sealed class PowerShellCompilationFeatureImpact
{
    /// <summary>Creates a feature-impact result.</summary>
    public PowerShellCompilationFeatureImpact(
        string featureId,
        string title,
        string recommendation,
        int occurrences,
        int affectedUnits,
        int visibleSoleBlockerUnits,
        int affectedProducts,
        int candidateCompleteProductsUnlocked,
        int currentCompilableUnits,
        int totalUnits)
    {
        FeatureId = featureId ?? string.Empty;
        Title = title ?? string.Empty;
        Recommendation = recommendation ?? string.Empty;
        Occurrences = occurrences;
        AffectedUnits = affectedUnits;
        VisibleSoleBlockerUnits = visibleSoleBlockerUnits;
        AffectedProducts = affectedProducts;
        CandidateCompleteProductsUnlocked = candidateCompleteProductsUnlocked;
        CurrentCompilableUnits = currentCompilableUnits;
        TotalUnits = totalUnits;
    }

    /// <summary>Stable compiler feature identifier.</summary>
    public string FeatureId { get; }
    /// <summary>Short user-facing capability title.</summary>
    public string Title { get; }
    /// <summary>Suggested engine area or authoring action.</summary>
    public string Recommendation { get; }
    /// <summary>Visible diagnostics assigned to the feature.</summary>
    public int Occurrences { get; }
    /// <summary>Distinct fallback units affected by the feature.</summary>
    public int AffectedUnits { get; }
    /// <summary>Units for which this is the only currently visible feature blocker.</summary>
    public int VisibleSoleBlockerUnits { get; }
    /// <summary>Distinct census products affected.</summary>
    public int AffectedProducts { get; }
    /// <summary>Complete census roots that would have no other currently visible unit blocker.</summary>
    public int CandidateCompleteProductsUnlocked { get; }
    /// <summary>Current typed units in the measured scope.</summary>
    public int CurrentCompilableUnits { get; }
    /// <summary>Total units in the measured scope.</summary>
    public int TotalUnits { get; }
    /// <summary>Candidate typed units if every currently sole-blocked unit became eligible.</summary>
    public int CandidateCompilableUnits => Math.Min(TotalUnits, CurrentCompilableUnits + VisibleSoleBlockerUnits);
    /// <summary>Candidate coverage based only on blockers visible in this run.</summary>
    public double CandidateCoveragePercentage => TotalUnits == 0 ? 0 : CandidateCompilableUnits * 100d / TotalUnits;
}

/// <summary>Two compiler features observed together in fallback units.</summary>
public sealed class PowerShellCompilationFeaturePair
{
    /// <summary>Creates a co-blocker result.</summary>
    public PowerShellCompilationFeaturePair(string firstFeatureId, string secondFeatureId, int affectedUnits)
    {
        FirstFeatureId = firstFeatureId ?? string.Empty;
        SecondFeatureId = secondFeatureId ?? string.Empty;
        AffectedUnits = affectedUnits;
    }

    /// <summary>First stable feature id.</summary>
    public string FirstFeatureId { get; }
    /// <summary>Second stable feature id.</summary>
    public string SecondFeatureId { get; }
    /// <summary>Distinct units reporting both features.</summary>
    public int AffectedUnits { get; }
}

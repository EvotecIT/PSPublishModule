using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    internal static PowerShellCompilationFilePlan[] ApplySemanticEvidence(
        PowerShellCompilationFilePlan[] structural,
        string[] sourcePaths,
        string identityRoot,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        => ApplySemanticEvidence(structural, sourcePaths, identityRoot, targetFramework, capabilities, PowerShellCommandSemanticRegistry.Default);

    private static PowerShellCompilationFilePlan[] ApplySemanticEvidence(
        PowerShellCompilationFilePlan[] structural,
        string[] sourcePaths,
        string identityRoot,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticRegistry commandRegistry)
    {
        var documents = sourcePaths.Select(path => PowerShellSourceParser.ParseFile(path, identityRoot)).ToArray();
        var documentsByPath = documents.ToDictionary(static document => document.Path, PowerShellCompilationPathSafety.PathComparer);
        var sourceDiagnosticsByPath = documents.ToDictionary(
            static document => document.Path,
            PowerShellSourceSemanticValidator.Validate,
            PowerShellCompilationPathSafety.PathComparer);
        var targets = new List<SemanticUnitTarget>();
        var compilationDocuments = new List<ParsedSourceDocument>(documents);

        foreach (var file in structural)
        {
            if (!documentsByPath.TryGetValue(Path.GetFullPath(file.FullPath), out var document) || document.Errors.Length > 0)
                continue;
            var functions = document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .OfType<FunctionDefinitionAst>()
                .Where(function => function.Parent is NamedBlockAst && ReferenceEquals(function.Parent.Parent, document.SyntaxRoot))
                .ToArray();
            foreach (var unit in file.Units.Where(static unit => unit.Kind == PowerShellCompilationUnitKind.Function))
            {
                var occurrence = file.Units.TakeWhile(candidate => !ReferenceEquals(candidate, unit)).Count(candidate =>
                    candidate.Kind == PowerShellCompilationUnitKind.Function &&
                    candidate.Name.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
                    candidate.StartLine == unit.StartLine);
                var function = functions.Where(candidate =>
                        candidate.Name.Equals(unit.Name, StringComparison.OrdinalIgnoreCase) &&
                        candidate.Body.Extent.StartLineNumber == unit.StartLine)
                    .Skip(occurrence)
                    .FirstOrDefault();
                if (function is not null)
                    targets.Add(new SemanticUnitTarget(
                        file.FullPath,
                        unit,
                        document.DocumentId,
                        function.Name,
                        function.Extent.StartOffset,
                        function.Extent.EndOffset,
                        synthetic: false));
            }

            var scriptUnit = file.Units.FirstOrDefault(static unit => unit.Kind == PowerShellCompilationUnitKind.Script);
            if (scriptUnit is null) continue;
            var statements = GetEndStatements(
                    document.SyntaxRoot,
                    excludeFunctionDefinitions: true,
                    excludeModuleExports: Path.GetExtension(file.FullPath).Equals(".psm1", StringComparison.OrdinalIgnoreCase))
                .Where(static statement => !IsTopLevelDotSource(statement))
                .ToArray();
            var symbolName = "__PowerForgeScript_" + document.DocumentId.Substring(0, 16);
            var parameterBlock = PowerShellSourceParser.GetParameterBlockSource(document.SyntaxRoot.ParamBlock);
            var body = string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
            var synthetic = PowerShellSourceParser.Parse(
                $"function {symbolName} {{{Environment.NewLine}{parameterBlock}{Environment.NewLine}{body}{Environment.NewLine}}}",
                document.Path + ".powerforge-analysis.ps1",
                identityRoot);
            compilationDocuments.Add(synthetic);
            var definition = synthetic.SyntaxRoot.EndBlock!.Statements.OfType<FunctionDefinitionAst>().Single();
            targets.Add(new SemanticUnitTarget(
                file.FullPath,
                scriptUnit,
                synthetic.DocumentId,
                symbolName,
                definition.Extent.StartOffset,
                definition.Extent.EndOffset,
                synthetic: true));
        }

        var semantic = new PowerShellSemanticCompilationPipeline(commandRegistry).Compile(compilationDocuments, targetFramework, capabilities);
        return structural.Select(file => new PowerShellCompilationFilePlan(
            file.FullPath,
            file.RelativePath,
            file.Units.Select(unit => ApplySemanticUnitEvidence(
                file.FullPath,
                unit,
                targets,
                semantic,
                sourceDiagnosticsByPath.TryGetValue(Path.GetFullPath(file.FullPath), out var sourceDiagnostics)
                    ? sourceDiagnostics
                    : Array.Empty<PowerShellSemanticDiagnostic>())).ToArray(),
            file.Diagnostics)).ToArray();
    }

    private static PowerShellCompilationUnitPlan ApplySemanticUnitEvidence(
        string filePath,
        PowerShellCompilationUnitPlan unit,
        IReadOnlyList<SemanticUnitTarget> targets,
        PowerShellSemanticCompilationResult semantic,
        IReadOnlyList<PowerShellSemanticDiagnostic> sourceDiagnostics)
    {
        if (sourceDiagnostics.Count > 0)
        {
            return new PowerShellCompilationUnitPlan(
                unit.Name,
                unit.Kind,
                unit.StartLine,
                typeof(object).FullName!,
                unit.Parameters,
                sourceDiagnostics.Select(diagnostic => CreatePublicSemanticDiagnostic(unit, filePath, null, diagnostic)).ToArray());
        }
        var target = targets.FirstOrDefault(candidate =>
            PowerShellCompilationPathSafety.PathEquals(candidate.FilePath, filePath) &&
            ReferenceEquals(candidate.Unit, unit));
        if (target is null)
            return ReplaceWithSemanticDiagnostic(unit, filePath, "semantic.target.missing", "The semantic pipeline could not identify this compilation unit.", unit.StartLine, 1);

        var lowered = semantic.Lowered.Functions.FirstOrDefault(function =>
            function.Symbol.DocumentId == target.DocumentId &&
            function.Symbol.Name.Equals(target.SymbolName, StringComparison.OrdinalIgnoreCase) &&
            function.Symbol.Declaration.StartOffset == target.DeclarationOffset);
        if (lowered is not null)
        {
            return new PowerShellCompilationUnitPlan(
                unit.Name,
                unit.Kind,
                unit.StartLine,
                lowered.ReturnType.FullName ?? lowered.ReturnType.Name,
                lowered.Parameters.Select(static parameter => parameter.Contract).ToArray(),
                Array.Empty<PowerShellCompilationDiagnostic>());
        }

        var analyzed = semantic.Analyzed.Functions.FirstOrDefault(function =>
            function.Symbol.DocumentId == target.DocumentId &&
            function.Symbol.Name.Equals(target.SymbolName, StringComparison.OrdinalIgnoreCase) &&
            function.Symbol.Declaration.StartOffset == target.DeclarationOffset);
        var semanticDiagnostics = semantic.Lowered.Diagnostics.Where(diagnostic =>
                diagnostic.Span.DocumentId == target.DocumentId &&
                diagnostic.Span.StartOffset >= target.DeclarationOffset &&
                diagnostic.Span.StartOffset <= target.EndOffset)
            .ToArray();
        var analyzedFallback = analyzed?.Disposition.Kind != PowerShellExecutionDispositionKind.Typed ? analyzed?.Disposition : null;
        if (analyzedFallback is null && semanticDiagnostics.Length == 0)
            return ReplaceWithSemanticDiagnostic(unit, filePath, "semantic.lowering.missing", "The semantic pipeline did not produce a lowered compilation contract.", unit.StartLine, 1);
        var blockers = semanticDiagnostics.Select(diagnostic => CreatePublicSemanticDiagnostic(unit, filePath, target, diagnostic)).ToList();
        if (analyzedFallback is not null)
        {
            var fallbackFeatureId = analyzedFallback.ReasonCode ?? PowerShellCompilationFeatureIds.FunctionGraph;
            if (!blockers.Any(diagnostic => diagnostic.FeatureId.Equals(fallbackFeatureId, StringComparison.Ordinal)))
            {
                blockers.Add(new PowerShellCompilationDiagnostic(
                    PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                    analyzedFallback.Explanation,
                    filePath,
                    unit.StartLine,
                    1,
                    fallbackFeatureId));
            }
        }
        var retained = blockers
            .GroupBy(static item => item.FeatureId + "\0" + item.Line + "\0" + item.Column, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToArray();
        return new PowerShellCompilationUnitPlan(
            unit.Name,
            unit.Kind,
            unit.StartLine,
            typeof(object).FullName!,
            unit.Parameters,
            retained);
    }

    private static PowerShellCompilationDiagnostic CreatePublicSemanticDiagnostic(
        PowerShellCompilationUnitPlan unit,
        string filePath,
        SemanticUnitTarget? target,
        PowerShellSemanticDiagnostic semanticDiagnostic)
    {
        var featureId = GetPublicFeatureId(semanticDiagnostic);
        var code = GetPublicDiagnosticCode(featureId, semanticDiagnostic);
        return new PowerShellCompilationDiagnostic(
            code,
            semanticDiagnostic.Message,
            filePath,
            target?.Synthetic == true ? unit.StartLine : semanticDiagnostic.Span.StartLine,
            target?.Synthetic == true ? 1 : semanticDiagnostic.Span.StartColumn,
            featureId);
    }

    private static string GetPublicFeatureId(PowerShellSemanticDiagnostic diagnostic)
        => diagnostic.Code.StartsWith("PSB", StringComparison.Ordinal)
            ? PowerShellCompilationFeatureIds.Resolve(PowerShellCompilationDiagnosticCode.UnsupportedSyntax, diagnostic.Message, null)
            : diagnostic.Code;

    private static PowerShellCompilationDiagnosticCode GetPublicDiagnosticCode(string featureId, PowerShellSemanticDiagnostic diagnostic)
    {
        if (featureId.Equals(PowerShellCompilationFeatureIds.ParameterType, StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.UnsupportedParameterType;
        if (featureId.Equals(PowerShellCompilationFeatureIds.DynamicCommand, StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.DynamicCommandInvocation;
        if (featureId.StartsWith("command.", StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.CommandInvocation;
        if (featureId.Equals(PowerShellCompilationFeatureIds.ScriptBlock, StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.ScriptBlock;
        if (featureId.Equals(PowerShellCompilationFeatureIds.RuntimeScope, StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.RuntimeScope;
        if (diagnostic.Code.Equals("PSB0001", StringComparison.Ordinal))
            return PowerShellCompilationDiagnosticCode.ParseError;
        return PowerShellCompilationDiagnosticCode.UnsupportedSyntax;
    }

    private static PowerShellCompilationUnitPlan ReplaceWithSemanticDiagnostic(
        PowerShellCompilationUnitPlan unit,
        string filePath,
        string featureId,
        string message,
        int line,
        int column)
        => ReplaceUnit(unit, typeof(object), new[]
        {
            new PowerShellCompilationDiagnostic(
                PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
                message,
                filePath,
                line,
                column,
                featureId)
        });

    private static bool IsTopLevelDotSource(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst { InvocationOperator: TokenKind.Dot };

    private sealed class SemanticUnitTarget
    {
        internal SemanticUnitTarget(
            string filePath,
            PowerShellCompilationUnitPlan unit,
            string documentId,
            string symbolName,
            int declarationOffset,
            int endOffset,
            bool synthetic)
        {
            FilePath = filePath;
            Unit = unit;
            DocumentId = documentId;
            SymbolName = symbolName;
            DeclarationOffset = declarationOffset;
            EndOffset = endOffset;
            Synthetic = synthetic;
        }

        internal string FilePath { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
        internal string DocumentId { get; }
        internal string SymbolName { get; }
        internal int DeclarationOffset { get; }
        internal int EndOffset { get; }
        internal bool Synthetic { get; }
    }
}

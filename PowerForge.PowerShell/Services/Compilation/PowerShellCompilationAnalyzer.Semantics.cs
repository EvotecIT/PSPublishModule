using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationAnalyzer
{
    private static PowerShellCompilationFilePlan[] ApplySemanticEvidence(
        PowerShellCompilationFilePlan[] structural,
        string[] sourcePaths,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var documents = sourcePaths.Select(PowerShellSourceParser.ParseFile).ToArray();
        var documentsByPath = documents.ToDictionary(static document => document.Path, PowerShellCompilationPathSafety.PathComparer);
        var localFunctionNames = documents.SelectMany(document => document.SyntaxRoot
                .FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .OfType<FunctionDefinitionAst>()
                .Select(static function => function.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                        synthetic: false,
                        skipGraphEvidence: RequiresArtifactGraphEmission(
                            GetEndStatements(function.Body, excludeFunctionDefinitions: false, excludeModuleExports: false),
                            capabilities,
                            localFunctionNames)));
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
            var parameterBlock = document.SyntaxRoot.ParamBlock?.Extent.Text ?? string.Empty;
            var body = string.Join(Environment.NewLine, statements.Select(static statement => statement.Extent.Text));
            var synthetic = PowerShellSourceParser.Parse(
                $"function {symbolName} {{{Environment.NewLine}{parameterBlock}{Environment.NewLine}{body}{Environment.NewLine}}}",
                document.Path + ".powerforge-analysis.ps1");
            compilationDocuments.Add(synthetic);
            var definition = synthetic.SyntaxRoot.EndBlock!.Statements.OfType<FunctionDefinitionAst>().Single();
            targets.Add(new SemanticUnitTarget(
                file.FullPath,
                scriptUnit,
                synthetic.DocumentId,
                symbolName,
                definition.Extent.StartOffset,
                definition.Extent.EndOffset,
                synthetic: true,
                skipGraphEvidence: RequiresArtifactGraphEmission(statements, capabilities, localFunctionNames)));
        }

        var semantic = new PowerShellSemanticCompilationPipeline().Compile(compilationDocuments, targetFramework, capabilities);
        return structural.Select(file => new PowerShellCompilationFilePlan(
            file.FullPath,
            file.RelativePath,
            file.Units.Select(unit => ApplySemanticUnitEvidence(file.FullPath, unit, targets, semantic)).ToArray(),
            file.Diagnostics)).ToArray();
    }

    private static PowerShellCompilationUnitPlan ApplySemanticUnitEvidence(
        string filePath,
        PowerShellCompilationUnitPlan unit,
        IReadOnlyList<SemanticUnitTarget> targets,
        PowerShellSemanticCompilationResult semantic)
    {
        if (!unit.IsCompilable) return unit;
        var target = targets.FirstOrDefault(candidate =>
            PowerShellCompilationPathSafety.PathEquals(candidate.FilePath, filePath) &&
            ReferenceEquals(candidate.Unit, unit));
        if (target is null) return unit;
        if (target.SkipGraphEvidence) return unit;

        var lowered = semantic.Lowered.Functions.FirstOrDefault(function =>
            function.Symbol.DocumentId == target.DocumentId &&
            function.Symbol.Name.Equals(target.SymbolName, StringComparison.OrdinalIgnoreCase) &&
            function.Symbol.Declaration.StartOffset == target.DeclarationOffset);
        if (lowered is not null)
            return ReplaceUnit(unit, lowered.ReturnType, Array.Empty<PowerShellCompilationDiagnostic>());

        var analyzed = semantic.Analyzed.Functions.FirstOrDefault(function =>
            function.Symbol.DocumentId == target.DocumentId &&
            function.Symbol.Name.Equals(target.SymbolName, StringComparison.OrdinalIgnoreCase) &&
            function.Symbol.Declaration.StartOffset == target.DeclarationOffset);
        var semanticDiagnostic = semantic.Lowered.Diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Span.DocumentId == target.DocumentId &&
            diagnostic.Span.StartOffset >= target.DeclarationOffset &&
            diagnostic.Span.StartOffset <= target.EndOffset);
        var analyzedFallback = analyzed?.Disposition.Kind != PowerShellExecutionDispositionKind.Typed ? analyzed?.Disposition : null;
        var message = analyzedFallback?.Explanation ?? semanticDiagnostic?.Message ??
            $"{(unit.Kind == PowerShellCompilationUnitKind.Script ? "Script" : "Function")} '{unit.Name}' did not produce a lowered semantic contract.";
        var featureId = analyzedFallback?.ReasonCode ?? semanticDiagnostic?.Code ?? PowerShellCompilationFeatureIds.FunctionGraph;
        var diagnostic = new PowerShellCompilationDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            message,
            filePath,
            target.Synthetic ? unit.StartLine : semanticDiagnostic?.Span.StartLine ?? unit.StartLine,
            target.Synthetic ? 1 : semanticDiagnostic?.Span.StartColumn ?? 1,
            featureId);
        return ReplaceUnit(unit, typeof(object), new[] { diagnostic });
    }

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
            bool synthetic,
            bool skipGraphEvidence)
        {
            FilePath = filePath;
            Unit = unit;
            DocumentId = documentId;
            SymbolName = symbolName;
            DeclarationOffset = declarationOffset;
            EndOffset = endOffset;
            Synthetic = synthetic;
            SkipGraphEvidence = skipGraphEvidence;
        }

        internal string FilePath { get; }
        internal PowerShellCompilationUnitPlan Unit { get; }
        internal string DocumentId { get; }
        internal string SymbolName { get; }
        internal int DeclarationOffset { get; }
        internal int EndOffset { get; }
        internal bool Synthetic { get; }
        internal bool SkipGraphEvidence { get; }
    }
}

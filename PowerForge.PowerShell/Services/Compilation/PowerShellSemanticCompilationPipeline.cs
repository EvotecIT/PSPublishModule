namespace PowerForge;

/// <summary>
/// Composes parsing, binding, deterministic analysis, lowering, and C# emission for one semantic result.
/// </summary>
internal sealed class PowerShellSemanticCompilationPipeline
{
    private readonly PowerShellSemanticBinder _binder;
    private readonly PowerShellBoundOptimizer _optimizer;
    private readonly PowerShellSemanticAnalyzer _analyzer;
    private readonly PowerShellTypedLowerer _lowerer;
    private readonly PowerShellBoundCSharpBackend _backend;

    internal PowerShellSemanticCompilationPipeline()
        : this(new PowerShellSemanticBinder(), new PowerShellBoundOptimizer(), new PowerShellSemanticAnalyzer(), new PowerShellTypedLowerer(), new PowerShellBoundCSharpBackend())
    {
    }

    internal PowerShellSemanticCompilationPipeline(PowerShellCommandSemanticRegistry commandRegistry)
        : this(new PowerShellSemanticBinder(commandRegistry), new PowerShellBoundOptimizer(), new PowerShellSemanticAnalyzer(), new PowerShellTypedLowerer(), new PowerShellBoundCSharpBackend())
    {
    }

    internal PowerShellSemanticCompilationPipeline(
        PowerShellSemanticBinder binder,
        PowerShellBoundOptimizer optimizer,
        PowerShellSemanticAnalyzer analyzer,
        PowerShellTypedLowerer lowerer,
        PowerShellBoundCSharpBackend backend)
    {
        _binder = binder;
        _optimizer = optimizer;
        _analyzer = analyzer;
        _lowerer = lowerer;
        _backend = backend;
    }

    internal PowerShellSemanticCompilationResult Compile(
        IEnumerable<ParsedSourceDocument> documents,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        var bound = _binder.Bind(documents, targetFramework, capabilities);
        var optimized = _optimizer.Optimize(bound);
        var analyzed = _analyzer.Analyze(optimized.Program);
        var lowered = _lowerer.Lower(analyzed, capabilities);
        var emitted = _backend.Emit(lowered);
        return new PowerShellSemanticCompilationResult(bound, optimized.Evidence, analyzed, lowered, emitted);
    }
}

internal sealed class PowerShellSemanticCompilationResult
{
    internal PowerShellSemanticCompilationResult(
        PowerShellBoundProgram bound,
        PowerShellBoundOptimizationEvidence optimization,
        PowerShellBoundProgram analyzed,
        PowerShellLoweredProgram lowered,
        PowerShellBoundCSharpResult emitted)
    {
        Bound = bound;
        Optimization = optimization;
        Analyzed = analyzed;
        Lowered = lowered;
        Emitted = emitted;
    }

    internal PowerShellBoundProgram Bound { get; }
    internal PowerShellBoundOptimizationEvidence Optimization { get; }
    internal PowerShellBoundProgram Analyzed { get; }
    internal PowerShellLoweredProgram Lowered { get; }
    internal PowerShellBoundCSharpResult Emitted { get; }
}

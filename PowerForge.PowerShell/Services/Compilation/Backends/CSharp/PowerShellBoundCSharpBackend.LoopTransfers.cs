using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private readonly Dictionary<SourceSpan, LoopTransferLabels> _loopTransferLabels = new();

    /// <summary>Allocates function-local destinations only for referenced authored loops.</summary>
    private void InitializeLoopTransfers(PowerShellLoweredFunction function, Func<string, string> allocate)
    {
        var statements = PowerShellLoweredTreeEnumerator.EnumerateStatements(function.Statements).ToArray();
        var loops = statements.Where(static statement => statement is PowerShellLoweredWhileStatement or
            PowerShellLoweredForStatement or PowerShellLoweredForEachStatement).Select(static statement => statement.Span).ToHashSet();
        foreach (var transfer in statements)
        {
            var target = transfer switch
            {
                PowerShellLoweredBreakStatement broken => broken.TargetLoop,
                PowerShellLoweredContinueStatement continued => continued.TargetLoop,
                _ => null
            };
            if (target is not { } span) continue;
            if (!loops.Contains(span)) throw new InvalidOperationException("A labeled transfer has no lowered loop in its function.");
            if (!_loopTransferLabels.TryGetValue(span, out var labels))
                _loopTransferLabels.Add(span, labels = new LoopTransferLabels());
            if (transfer is PowerShellLoweredBreakStatement && labels.Break is null) labels.Break = allocate("loopBreak");
            if (transfer is PowerShellLoweredContinueStatement && labels.Continue is null) labels.Continue = allocate("loopContinue");
        }
    }

    private void EmitLoopTransferTarget(StringBuilder builder, SourceSpan span, string prefix, bool isContinue)
    {
        if (!_loopTransferLabels.TryGetValue(span, out var labels)) return;
        var label = isContinue ? labels.Continue : labels.Break;
        if (label is not null) builder.Append(prefix).Append(label).AppendLine(": ;");
    }

    private void EmitLoopTransfer(StringBuilder builder, SourceSpan? target, string prefix, bool isContinue)
    {
        builder.Append(prefix);
        if (target is not { } span) { builder.AppendLine(isContinue ? "continue;" : "break;"); return; }
        if (!_loopTransferLabels.TryGetValue(span, out var labels) ||
            (isContinue ? labels.Continue : labels.Break) is not { } label)
            throw new InvalidOperationException("A labeled transfer has no allocated destination.");
        builder.Append("goto ").Append(label).AppendLine(";");
    }

    private sealed class LoopTransferLabels
    {
        internal string? Break { get; set; }
        internal string? Continue { get; set; }
    }
}

using System.Text;

namespace PowerForge;

internal static partial class PowerShellBinaryCmdletSourceGenerator
{
    private static readonly HashSet<string> ProviderCancellationMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_providerCancellation",
        "_providerCancellationGate",
        "_providerCancellationActiveCancels",
        "_providerCancellationDisposed",
        "DisposeProviderCancellation",
        "Dispose"
    };

    private static void AppendProviderCancellationMembers(StringBuilder builder)
    {
        builder.AppendLine("    private readonly global::System.Threading.CancellationTokenSource _providerCancellation = new();");
        builder.AppendLine("    private readonly object _providerCancellationGate = new();");
        builder.AppendLine("    private int _providerCancellationActiveCancels;");
        builder.AppendLine("    private bool _providerCancellationDisposed;");
        builder.AppendLine();
        builder.AppendLine("    protected override void StopProcessing()");
        builder.AppendLine("    {");
        builder.AppendLine("        var cancel = false;");
        builder.AppendLine("        lock (_providerCancellationGate)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_providerCancellationDisposed)");
        builder.AppendLine("            {");
        builder.AppendLine("                _providerCancellationActiveCancels++;");
        builder.AppendLine("                cancel = true;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        builder.AppendLine("            if (cancel) _providerCancellation.Cancel();");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            if (cancel)");
        builder.AppendLine("            {");
        builder.AppendLine("                lock (_providerCancellationGate)");
        builder.AppendLine("                {");
        builder.AppendLine("                    _providerCancellationActiveCancels--;");
        builder.AppendLine("                    if (_providerCancellationActiveCancels == 0)");
        builder.AppendLine("                        global::System.Threading.Monitor.PulseAll(_providerCancellationGate);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            base.StopProcessing();");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void DisposeProviderCancellation()");
        builder.AppendLine("    {");
        builder.AppendLine("        lock (_providerCancellationGate)");
        builder.AppendLine("        {");
        builder.AppendLine("            while (_providerCancellationActiveCancels != 0)");
        builder.AppendLine("                global::System.Threading.Monitor.Wait(_providerCancellationGate);");
        builder.AppendLine("            if (_providerCancellationDisposed) return;");
        builder.AppendLine("            _providerCancellationDisposed = true;");
        builder.AppendLine("        }");
        builder.AppendLine("        _providerCancellation.Dispose();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public void Dispose() => DisposeProviderCancellation();");
        builder.AppendLine();
    }

    private static void AppendProviderCancellationInvocation(
        StringBuilder builder,
        string invocation,
        bool returnsVoid)
    {
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        if (returnsVoid)
            builder.AppendLine($"            {invocation};");
        else
            builder.AppendLine($"            WriteObject({invocation}, enumerateCollection: true);");
        builder.AppendLine("        }");
        builder.AppendLine("        finally");
        builder.AppendLine("        {");
        builder.AppendLine("            DisposeProviderCancellation();");
        builder.AppendLine("        }");
    }
}

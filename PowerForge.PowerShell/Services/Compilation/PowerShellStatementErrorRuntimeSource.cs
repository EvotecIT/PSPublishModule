using System.Text;

namespace PowerForge;

/// <summary>Embeds the qualified command runtime owner without a compiler-assembly dependency.</summary>
internal static class PowerShellStatementErrorRuntimeSource
{
    internal static string Render()
    {
        var source = new StringBuilder("#nullable enable\n");
        foreach (var suffix in new[] { ".cs", ".Contract.cs", ".Exceptions.cs", ".Enumeration.cs", ".Finally.cs", ".Functions.cs", ".Module.cs", ".Binding.cs", ".Variables.cs", ".NativeFunction.cs" })
        {
            var resource = "PowerForge.PowerShell.Compilation.PowerShellStatementErrorContext" + suffix;
            using var stream = typeof(PowerShellStatementErrorRuntimeSource).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Missing statement-error runtime source: " + resource);
            using var reader = new StreamReader(stream);
            source.AppendLine(reader.ReadToEnd());
        }
        return source.ToString();
    }

    internal static string RenderVariableScope()
    {
        using var stream = typeof(PowerShellStatementErrorRuntimeSource).Assembly.GetManifestResourceStream(
            "PowerForge.PowerShell.Compilation.PowerShellStatementErrorContext.Variables.cs")
            ?? throw new InvalidOperationException("Missing command variable-scope runtime source.");
        using var reader = new StreamReader(stream);
        return "#nullable enable\n" + reader.ReadToEnd();
    }
}

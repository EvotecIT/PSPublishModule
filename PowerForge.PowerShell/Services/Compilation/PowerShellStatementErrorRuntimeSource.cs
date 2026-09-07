using System.Text;

namespace PowerForge;

/// <summary>Embeds the qualified command runtime owner without a compiler-assembly dependency.</summary>
internal static class PowerShellStatementErrorRuntimeSource
{
    internal static string Render()
    {
        var source = new StringBuilder();
        foreach (var suffix in new[] { ".cs", ".Contract.cs", ".Exceptions.cs", ".Functions.cs" })
        {
            var resource = "PowerForge.PowerShell.Compilation.PowerShellStatementErrorContext" + suffix;
            using var stream = typeof(PowerShellStatementErrorRuntimeSource).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("Missing statement-error runtime source: " + resource);
            using var reader = new StreamReader(stream);
            source.AppendLine(reader.ReadToEnd());
        }
        return source.ToString();
    }
}

namespace PowerForge;

internal static class PowerShellCompilationOperatorPolicy
{
    private static readonly HashSet<string> RuntimeIndependentBinary = new(StringComparer.Ordinal)
    {
        "Is", "IsNot", "Imatch", "Cmatch", "Inotmatch", "Cnotmatch",
        "Ireplace", "Creplace", "Isplit", "Csplit", "Join",
        "Band", "Bor", "Bxor", "Shl", "Shr"
    };

    private static readonly HashSet<string> PowerShellHostBinary = new(StringComparer.Ordinal)
    {
        "Ilike", "Clike", "Inotlike", "Cnotlike",
        "Icontains", "Ccontains", "Inotcontains", "Cnotcontains",
        "Iin", "Cin", "Inotin", "Cnotin"
    };

    internal static bool CanLowerBinary(string operation, PowerShellCompilationCapability capabilities)
        => RuntimeIndependentBinary.Contains(operation) ||
           capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageOperators) &&
           PowerShellHostBinary.Contains(operation);
}

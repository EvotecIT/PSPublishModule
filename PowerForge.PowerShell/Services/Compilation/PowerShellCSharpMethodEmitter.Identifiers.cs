namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private string GetTemporaryIdentifier(string purpose)
    {
        while (true)
        {
            var candidate = $"__pf_{purpose}_{_temporaryIndex++}";
            if (_temporaryIdentifiers.Contains(candidate) ||
                _variableIdentifiers.Values.Contains(candidate, StringComparer.Ordinal) ||
                _variables.Keys.Any(name => SanitizeIdentifier(name).Equals(candidate, StringComparison.Ordinal)))
                continue;
            _temporaryIdentifiers.Add(candidate);
            return candidate;
        }
    }
}

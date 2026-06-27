namespace PowerForge;

internal sealed class ModuleStateConflictFinding
{
    internal ModuleStateConflictFinding(
        ModuleStateConflictSeverity severity,
        string code,
        string message,
        string familyName,
        string[] moduleNames,
        string[] versions,
        string? scope = null,
        string? sourceRepository = null)
    {
        Severity = severity;
        Code = code;
        Message = message;
        FamilyName = familyName;
        ModuleNames = moduleNames;
        Versions = versions;
        Scope = string.IsNullOrWhiteSpace(scope) ? null : scope!.Trim();
        SourceRepository = string.IsNullOrWhiteSpace(sourceRepository) ? null : sourceRepository!.Trim();
    }

    internal ModuleStateConflictSeverity Severity { get; }

    internal string Code { get; }

    internal string Message { get; }

    internal string FamilyName { get; }

    internal string[] ModuleNames { get; }

    internal string[] Versions { get; }

    internal string? Scope { get; }

    internal string? SourceRepository { get; }
}

internal enum ModuleStateConflictSeverity
{
    Warning,
    Error
}

namespace PowerForge;

internal sealed class ModuleStateConflictFinding
{
    internal ModuleStateConflictFinding(
        ModuleStateConflictSeverity severity,
        string code,
        string message,
        string familyName,
        string[] moduleNames,
        string[] versions)
    {
        Severity = severity;
        Code = code;
        Message = message;
        FamilyName = familyName;
        ModuleNames = moduleNames;
        Versions = versions;
    }

    internal ModuleStateConflictSeverity Severity { get; }

    internal string Code { get; }

    internal string Message { get; }

    internal string FamilyName { get; }

    internal string[] ModuleNames { get; }

    internal string[] Versions { get; }
}

internal enum ModuleStateConflictSeverity
{
    Warning,
    Error
}

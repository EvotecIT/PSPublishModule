namespace PowerForge;

/// <summary>Recognizes the compiler's canonical portable-PDB form of an authored PowerShell document identity.</summary>
internal static class PowerShellCompilationDocumentIdentity
{
    private const string MappedSourcePrefix = "/_/src/";

    internal static bool TryGetMappedDocumentId(string documentPath, out string documentId)
    {
        documentId = string.Empty;
        if (!documentPath.StartsWith(MappedSourcePrefix, StringComparison.Ordinal) ||
            documentPath.Length != MappedSourcePrefix.Length + 64 ||
            !documentPath.Skip(MappedSourcePrefix.Length).All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            return false;
        documentId = documentPath.Substring(MappedSourcePrefix.Length);
        return true;
    }
}

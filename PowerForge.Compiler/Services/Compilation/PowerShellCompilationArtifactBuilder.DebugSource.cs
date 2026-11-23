using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private const string Sha256SourceChecksumGuid = "{8829d00f-11b8-4213-878b-770e8597ac16}";

    private static void WriteCompiledPowerShellSource(
        string destination,
        string generatedSource,
        string rootSourcePath,
        IEnumerable<string> compilationSourcePaths,
        bool includeSyntheticEntry = false)
    {
        var fullRootSourcePath = Path.GetFullPath(rootSourcePath);
        var identityRoot = Path.GetDirectoryName(fullRootSourcePath) ?? Directory.GetCurrentDirectory();
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourcePath in new[] { fullRootSourcePath }.Concat(compilationSourcePaths))
        {
            var fullPath = Path.GetFullPath(sourcePath);
            checksums[PowerShellSourceParser.CreateDocumentId(fullPath, identityRoot)] = ComputeSha256(fullPath);
        }
        if (includeSyntheticEntry)
        {
            var entryId = PowerShellSourceParser.CreateDocumentId(fullRootSourcePath + ".powerforge-entry.ps1", identityRoot);
            checksums[entryId] = checksums[PowerShellSourceParser.CreateDocumentId(fullRootSourcePath, identityRoot)];
        }

        var referencedIds = Regex.Matches(generatedSource, "(?m)^\\s*#line\\s+\\d+\\s+\"([a-f0-9]{64})\"")
            .Cast<Match>()
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var output = new StringBuilder();
        foreach (var documentId in referencedIds)
        {
            if (!checksums.TryGetValue(documentId, out var checksum))
                throw new InvalidDataException($"Generated source refers to unknown PowerShell document '{documentId}'.");
            output.Append("#pragma checksum \"").Append(documentId).Append("\" \"")
                .Append(Sha256SourceChecksumGuid).Append("\" \"").Append(checksum).AppendLine("\"");
        }
        output.Append(generatedSource);
        File.WriteAllText(destination, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}

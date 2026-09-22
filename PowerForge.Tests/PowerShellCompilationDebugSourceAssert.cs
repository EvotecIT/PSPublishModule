using System.Reflection.Metadata;
using System.Security.Cryptography;
using PowerForge;

namespace PowerForge.Tests;

internal static class PowerShellCompilationDebugSourceAssert
{
    internal static void HasPortableAuthoredSource(
        string pdbPath, string sourcePath, string identityRoot, bool syntheticEntry = false)
    {
        var documentId = PowerShellSourceParser.CreateDocumentId(
            syntheticEntry ? sourcePath + ".powerforge-entry.ps1" : sourcePath, identityRoot);
        using var stream = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var document = Assert.Single(reader.Documents.Select(reader.GetDocument),
            item => reader.GetString(item.Name) == "/_/src/" + documentId);
        Assert.Equal(new Guid("8829d00f-11b8-4213-878b-770e8597ac16"), reader.GetGuid(document.HashAlgorithm));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(sourcePath)), reader.GetBlobBytes(document.Hash));
        Assert.DoesNotContain(reader.Documents.Select(reader.GetDocument), item =>
            reader.GetString(item.Name).Contains("powershell-compilation/ps-", StringComparison.OrdinalIgnoreCase) ||
            reader.GetString(item.Name).Contains("powershell-compilation\\ps-", StringComparison.OrdinalIgnoreCase));
    }

    internal static void HasAuthoredSequencePoint(
        string pdbPath, string sourcePath, string identityRoot, int authoredLine)
    {
        var documentName = "/_/src/" + PowerShellSourceParser.CreateDocumentId(sourcePath, identityRoot);
        using var stream = File.OpenRead(pdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var documentHandle = Assert.Single(reader.Documents,
            handle => reader.GetString(reader.GetDocument(handle).Name) == documentName);
        var lines = new List<int>();
        foreach (var methodHandle in reader.MethodDebugInformation)
        {
            var method = reader.GetMethodDebugInformation(methodHandle);
            var currentDocument = method.Document;
            foreach (var point in method.GetSequencePoints())
            {
                if (!point.Document.IsNil) currentDocument = point.Document;
                if (!point.IsHidden && currentDocument == documentHandle)
                    lines.Add(point.StartLine);
            }
        }
        Assert.Contains(authoredLine, lines);
    }
}

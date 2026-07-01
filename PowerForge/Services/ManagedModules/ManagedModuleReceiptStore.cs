using System.Text.Json;

namespace PowerForge;

internal sealed class ManagedModuleReceiptStore
{
    internal const string ReceiptRelativePath = ".powerforge/managed-module-receipt.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string WriteReceipt(
        ManagedModuleRepository repository,
        ManagedModuleInstallResult result,
        string? previousVersion = null,
        string operation = "Install")
    {
        if (result.Download is null)
            throw new InvalidOperationException("A package download result is required before writing a managed module receipt.");

        var receipt = new ManagedModuleReceipt
        {
            Operation = operation,
            Name = result.Name,
            Version = result.Version,
            PreviousVersion = previousVersion,
            RepositoryName = result.RepositoryName,
            RepositorySource = repository.Source,
            Source = result.Download.Source,
            PackagePath = result.Download.PackagePath,
            PackageSha256 = result.Download.PackageSha256,
            ModuleRoot = result.ModuleRoot,
            ModulePath = result.ModulePath,
            Status = result.Status.ToString(),
            FileCount = result.FileCount,
            ExtractedBytes = result.ExtractedBytes,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };

        var receiptPath = GetReceiptPath(result.ModulePath);
        var fileSystemReceiptPath = GetFileSystemPath(receiptPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fileSystemReceiptPath)!);
        File.WriteAllText(fileSystemReceiptPath, JsonSerializer.Serialize(receipt, JsonOptions));

        result.Receipt = receipt;
        result.ReceiptPath = receiptPath;
        return receiptPath;
    }

    public ManagedModuleReceipt? TryReadReceipt(string modulePath)
    {
        var receiptPath = GetReceiptPath(modulePath);
        var fileSystemReceiptPath = GetFileSystemPath(receiptPath);
        if (!File.Exists(fileSystemReceiptPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ManagedModuleReceipt>(File.ReadAllText(fileSystemReceiptPath));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string GetReceiptPath(string modulePath)
    {
        var normalized = ReceiptRelativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(modulePath, normalized);
    }

    private static string GetFileSystemPath(string path)
    {
        if (Path.DirectorySeparatorChar != '\\')
            return path;

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            return fullPath;
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + fullPath.Substring(2);

        return @"\\?\" + fullPath;
    }
}

using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class PowerShellCompilationLibraryPackageBuilder
{
    // The archive writer owns archive composition; this boundary owns temporary-file lifetime
    // and publication. Cancellation before the atomic move/replace never changes the destination.
    internal static string PublishPackage(string outputPath, Action<Stream> writePackage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPackage = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Exception? failure = null;
        try
        {
            using (var stream = new FileStream(temporaryPackage, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                writePackage(stream);
            var hash = ComputeSha256(temporaryPackage, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Commit point: no cancellation checks or fallible reads of the destination after this.
            if (File.Exists(outputPath))
                File.Replace(temporaryPackage, outputPath, destinationBackupFileName: null);
            else
                File.Move(temporaryPackage, outputPath);
            return hash;
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            Cleanup(() => { if (File.Exists(temporaryPackage)) File.Delete(temporaryPackage); }, failure);
        }
    }

    internal static void CopyStream(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = source.Read(buffer, 0, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0) return;
            destination.Write(buffer, 0, count);
        }
    }

    private static string ComputeSha256(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        using var sink = new CryptoStream(Stream.Null, algorithm, CryptoStreamMode.Write);
        CopyStream(stream, sink, cancellationToken);
        sink.FlushFinalBlock();
        return string.Concat(algorithm.Hash!.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static void Cleanup(Action cleanup, Exception? primaryError)
    {
        try { cleanup(); }
        catch (Exception cleanupError) when (primaryError is not null &&
                                           cleanupError is IOException or UnauthorizedAccessException)
        {
            // Retain diagnostics without turning cancellation or the original I/O failure into
            // a misleading cleanup exception. A successful build still reports cleanup failure.
            primaryError.Data["PowerForge.PackageCleanupFailure." + primaryError.Data.Count] = cleanupError.Message;
        }
    }
}

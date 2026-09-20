using System.Text;

namespace PowerForgeStudio.Orchestrator.Explorer;

public sealed partial class FileExplorerService
{
    /// <summary>Reads a bounded text preview without following file growth into an unbounded allocation.</summary>
    public async Task<string> ReadTextPreviewAsync(string path, CancellationToken cancellationToken = default)
    {
        const int limit = 256 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > limit) return "Preview limited to files smaller than 256 KiB. Open externally to view this file.";
        var buffer = new byte[limit + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        if (count > limit) return "File grew beyond the 256 KiB preview limit. Open externally to view it.";
        // BOM-aware StreamReader supports PowerShell files saved as UTF-16 as well as UTF-8.
        using var memory = new MemoryStream(buffer, 0, count);
        using var reader = new StreamReader(memory, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        try
        {
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return text.Contains('\0') ? "Binary file · open externally to view." : text;
        }
        catch (DecoderFallbackException) { return "File encoding cannot be previewed as text. Open externally to view."; }
    }
}

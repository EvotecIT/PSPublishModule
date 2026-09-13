#if NETFRAMEWORK
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PowerForge;

internal sealed partial class RedirectedProcessOutput
{
    // Framework's process and anonymous-pipe handles are synchronous: cancelling ReadAsync
    // cannot interrupt an already-blocked ReadFile. This capture is the only reader of its
    // pipe, so read only bytes Windows reports available and remain cancellable while idle.
    private static async Task<int> ReadWindowsPipeChunkAsync(Stream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        var handle = stream is PipeStream pipe ? (SafeHandle)pipe.SafePipeHandle
            : stream is FileStream file ? file.SafeFileHandle : null;
        if (handle is null || GetFileType(handle) != 3)
            return await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 109 or 233) return 0; // broken/disconnected pipe: all writers have closed.
                throw new IOException("Cannot inspect redirected process output.", new Win32Exception(error));
            }
            if (available > 0)
                return stream.Read(bytes, 0, (int)Math.Min((uint)bytes.Length, available));
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(SafeHandle pipe, IntPtr buffer, uint bufferSize,
        IntPtr bytesRead, out uint totalBytesAvailable, IntPtr bytesLeftThisMessage);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeHandle handle);
}
#endif

using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PowerForge;

internal sealed partial class RedirectedProcessOutput
{
    // Unix PipeStream.ReadAsync can queue a blocking read to the worker pool. Keep
    // process-pipe reads on the dedicated reader thread, with poll bounding idle
    // waits even when a descendant inherits the writer. This capture is the only
    // reader, so bytes reported readable cannot be consumed by another reader.
    private sealed class UnixPipeReader : IDisposable
    {
        private readonly SafeHandle _handle;
        private readonly int _descriptor;
        private readonly bool _linux;
        private bool _ownsReference;

        private UnixPipeReader(SafeHandle handle, bool linux)
        {
            _handle = handle;
            _linux = linux;
            handle.DangerousAddRef(ref _ownsReference);
            _descriptor = handle.DangerousGetHandle().ToInt32();
        }

        internal static UnixPipeReader? TryCreate(Stream stream)
        {
            var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            if (!linux && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return null;
            // Restrict native reads to a pipe whose handle the runtime owns.
            return stream is PipeStream pipe ? new UnixPipeReader(pipe.SafePipeHandle, linux) : null;
        }

        internal int Read(byte[] bytes, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptor = new PollDescriptor { Descriptor = _descriptor, Events = 1 }; // POLLIN
                var ready = _linux ? PollLinux(ref descriptor, new UIntPtr(1), 50)
                    : PollMac(ref descriptor, 1, 50);
                if (ready < 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 4) continue; // EINTR
                    throw new IOException("Cannot poll redirected process output.", new Win32Exception(error));
                }
                if (ready == 0) continue;
                // Read also on HUP/ERR: remaining buffered bytes precede EOF.
                var count = ReadDescriptor(_descriptor, bytes, new UIntPtr((uint)bytes.Length)).ToInt64();
                if (count >= 0) return checked((int)count);
                var readError = Marshal.GetLastWin32Error();
                if (readError == 4) continue;
                throw new IOException("Cannot read redirected process output.", new Win32Exception(readError));
            }
        }

        public void Dispose()
        {
            if (_ownsReference) { _ownsReference = false; _handle.DangerousRelease(); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PollDescriptor
        {
            internal int Descriptor;
            internal short Events;
            internal short ReturnedEvents;
        }

        // Linux nfds_t is unsigned long; Darwin nfds_t is unsigned int.
        [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
        private static extern int PollLinux(ref PollDescriptor descriptor, UIntPtr count, int timeout);

        [DllImport("libc", EntryPoint = "poll", SetLastError = true)]
        private static extern int PollMac(ref PollDescriptor descriptor, uint count, int timeout);

        [DllImport("libc", EntryPoint = "read", SetLastError = true)]
        private static extern IntPtr ReadDescriptor(int descriptor, [Out] byte[] bytes, UIntPtr count);
    }
}

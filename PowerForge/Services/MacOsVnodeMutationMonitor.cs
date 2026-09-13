using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Monitors already-open private snapshot files on macOS without treating reads as writes.
/// </summary>
internal sealed class MacOsVnodeMutationMonitor : IDisposable
{
    private const short EventFilterVnode = -4;
    private const ushort EventAdd = 0x0001;
    private const ushort EventEnable = 0x0004;
    private const ushort EventClear = 0x0020;
    private const ushort EventError = 0x4000;
    private const uint NoteDelete = 0x00000001;
    private const uint NoteWrite = 0x00000002;
    private const uint NoteExtend = 0x00000004;
    private const uint NoteAttrib = 0x00000008;
    private const uint NoteLink = 0x00000010;
    private const uint NoteRename = 0x00000020;
    private const uint NoteRevoke = 0x00000040;
    private const uint MutationNotes = NoteDelete |
                                           NoteWrite |
                                           NoteExtend |
                                           NoteAttrib |
                                           NoteLink |
                                           NoteRename |
                                           NoteRevoke;

    private readonly int _queueDescriptor;
    private readonly IReadOnlyDictionary<ulong, string> _pathsByDescriptor;
    private readonly Action<string> _recordChange;
    private readonly Thread _thread;
    private int _disposed;

    internal MacOsVnodeMutationMonitor(
        IReadOnlyDictionary<string, FileStream> leasedFiles,
        Action<string> recordChange)
    {
        if (leasedFiles is null)
            throw new ArgumentNullException(nameof(leasedFiles));
        if (recordChange is null)
            throw new ArgumentNullException(nameof(recordChange));
        _recordChange = recordChange;
        _queueDescriptor = Kqueue();
        if (_queueDescriptor < 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the macOS snapshot mutation queue.");

        try
        {
            var pathsByDescriptor = new Dictionary<ulong, string>();
            foreach (KeyValuePair<string, FileStream> entry in leasedFiles)
            {
                ulong descriptor = unchecked((ulong)entry.Value.SafeFileHandle.DangerousGetHandle().ToInt64());
                var change = new NativeKevent
                {
                    Ident = new UIntPtr(descriptor),
                    Filter = EventFilterVnode,
                    Flags = EventAdd | EventEnable | EventClear,
                    FilterFlags = MutationNotes
                };
                if (RegisterKevent(_queueDescriptor, ref change, 1, IntPtr.Zero, 0, IntPtr.Zero) < 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"Could not monitor the macOS snapshot file '{entry.Key}'.");
                }
                pathsByDescriptor[descriptor] = entry.Key;
            }
            _pathsByDescriptor = pathsByDescriptor;
            _thread = new Thread(Monitor)
            {
                IsBackground = true,
                Name = "PowerForge macOS snapshot monitor"
            };
            _thread.Start();
        }
        catch
        {
            _ = Close(_queueDescriptor);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _ = Close(_queueDescriptor);
            _thread.Join(TimeSpan.FromSeconds(2));
            return;
        }
        _ = Close(_queueDescriptor);
    }

    private void Monitor()
    {
        var timeout = new NativeTimespec
        {
            Seconds = IntPtr.Zero,
            Nanoseconds = new IntPtr(100_000_000)
        };
        while (Volatile.Read(ref _disposed) == 0)
        {
            int count = ReadKevent(
                _queueDescriptor,
                IntPtr.Zero,
                0,
                out NativeKevent observed,
                1,
                ref timeout);
            if (count == 0)
                continue;
            if (count < 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (Volatile.Read(ref _disposed) == 0)
                    _recordChange($"the macOS vnode monitor failed with error {error}");
                return;
            }
            ulong descriptor = observed.Ident.ToUInt64();
            string path = _pathsByDescriptor.TryGetValue(descriptor, out string? knownPath)
                ? knownPath
                : $"file descriptor {descriptor}";
            if ((observed.Flags & EventError) != 0)
            {
                _recordChange($"the macOS vnode monitor reported error {observed.Data.ToInt64()} for '{path}'");
                return;
            }
            if ((observed.FilterFlags & MutationNotes) != 0)
            {
                _recordChange(
                    $"macOS vnode mutation 0x{observed.FilterFlags:X8} for '{path}'");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKevent
    {
        internal UIntPtr Ident;
        internal short Filter;
        internal ushort Flags;
        internal uint FilterFlags;
        internal IntPtr Data;
        internal IntPtr UserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTimespec
    {
        internal IntPtr Seconds;
        internal IntPtr Nanoseconds;
    }

    [DllImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static extern int Kqueue();

    [DllImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static extern int RegisterKevent(
        int queueDescriptor,
        ref NativeKevent changes,
        int changeCount,
        IntPtr events,
        int eventCount,
        IntPtr timeout);

    [DllImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static extern int ReadKevent(
        int queueDescriptor,
        IntPtr changes,
        int changeCount,
        out NativeKevent events,
        int eventCount,
        ref NativeTimespec timeout);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int descriptor);
}

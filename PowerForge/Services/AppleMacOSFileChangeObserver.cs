#if NET8_0_OR_GREATER
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>Preserves native FSEvents flags so APFS clone-source notifications can be distinguished from writes.</summary>
internal sealed class AppleMacOSFileChangeObserver : IDisposable
{
    private const uint CloneFile = 0x00400000 | 0x00010000;

    internal static bool IsCloneSourceCandidate(uint flags)
        => flags == CloneFile || flags == (CloneFile | 0x00000100);
    private const string Events = "/System/Library/Frameworks/CoreServices.framework/Versions/A/Frameworks/FSEvents.framework/FSEvents";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private static readonly EventCallback Callback = ReceiveEvents;
    private static readonly DispatchCallback Barrier = static _ => { };
    private readonly Action<string, uint> _changed;
    private readonly Action<Exception> _error;
    private readonly string _root;
    private GCHandle _self;
    private IntPtr _stream, _queue, _paths, _path;
    private bool _scheduled, _started, _stopped, _disposed;

    internal AppleMacOSFileChangeObserver(string root, Action<string, uint> changed, Action<Exception> error)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        _root = Path.GetFullPath(root);
        _changed = changed;
        _error = error;
        try
        {
            _self = GCHandle.Alloc(this);
            _path = CFStringCreateWithCString(IntPtr.Zero, _root, 0x08000100);
            if (_path == IntPtr.Zero) throw new IOException("Unable to create the FSEvents root string.");
            // The observer retains both objects until the stream is released; this array needs no element callbacks.
            _paths = CFArrayCreate(IntPtr.Zero, new[] { _path }, 1, IntPtr.Zero);
            if (_paths == IntPtr.Zero) throw new IOException("Unable to create the FSEvents root array.");
            var context = new StreamContext { Info = GCHandle.ToIntPtr(_self) };
            _stream = FSEventStreamCreate(IntPtr.Zero, Callback, ref context, _paths, ulong.MaxValue, 0,
                0x10 | 0x02 | 0x04); // FileEvents, NoDefer, WatchRoot.
            if (_stream == IntPtr.Zero) throw new IOException("Unable to create the native file mutation observer.");
            _queue = DispatchQueueCreate("PowerForge.AppleReleaseSource", IntPtr.Zero);
            if (_queue == IntPtr.Zero) throw new IOException("Unable to create the native file mutation queue.");
            FSEventStreamSetDispatchQueue(_stream, _queue);
            _scheduled = true;
            if (!FSEventStreamStart(_stream)) throw new IOException("Unable to start the native file mutation observer.");
            _started = true;
        }
        catch { Dispose(); throw; }
    }

    internal static WatcherChangeTypes GetChangeType(uint flags)
    {
        var change = (WatcherChangeTypes)0;
        if ((flags & 0x100) != 0) change |= WatcherChangeTypes.Created;
        if ((flags & 0x200) != 0) change |= WatcherChangeTypes.Deleted;
        if ((flags & 0x800) != 0) change |= WatcherChangeTypes.Renamed;
        if ((flags & (0x400 | 0x1000 | 0x2000 | 0x4000 | 0x8000)) != 0) change |= WatcherChangeTypes.Changed;
        // Unknown or clone-only events are not silently discarded.
        return change == 0 ? WatcherChangeTypes.Changed : change;
    }

    internal sealed class ChangeEventArgs(string root, string path, uint flags)
        : FileSystemEventArgs(GetChangeType(flags), root, Path.GetRelativePath(root, path))
    {
        internal uint Flags { get; } = flags;
    }

    private static void ReceiveEvents(IntPtr stream, IntPtr info, nuint count, IntPtr paths, IntPtr flags, IntPtr ids)
    {
        var observer = (AppleMacOSFileChangeObserver)GCHandle.FromIntPtr(info).Target!;
        try
        {
            for (nuint index = 0; index < count; index++)
            {
                var eventFlags = unchecked((uint)Marshal.ReadInt32(flags, checked((int)index * sizeof(uint))));
                if ((eventFlags & (0x01 | 0x02 | 0x04 | 0x08 | 0x20)) != 0)
                    throw new IOException($"The native file mutation observer lost continuity (flags 0x{eventFlags:X}).");
                if (eventFlags == 0x10) continue; // HistoryDone contains no pathname mutation.
                var path = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(paths, checked((int)index * IntPtr.Size)))
                    ?? throw new IOException("The native file mutation observer returned an unreadable path.");
                var relative = Path.GetRelativePath(observer._root, path);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
                    throw new IOException("The native file mutation observer reported a path outside its root.");
                observer._changed(path, eventFlags);
            }
        }
        catch (Exception exception)
        {
            // Exceptions cannot cross the native callback boundary.
            try { observer._error(exception); } catch { }
        }
    }

    /// <summary>Completes pending native delivery before the producer is armed; never called from the callback queue.</summary>
    internal void Drain()
    {
        if (!_started || _stopped) return;
        FSEventStreamFlushSync(_stream);
        DispatchSync(_queue, IntPtr.Zero, Barrier);
    }

    /// <summary>Drains native callbacks before closing the observation boundary. Called only from the owning managed thread.</summary>
    internal void Stop()
    {
        if (_stopped) return;
        if (_stream == IntPtr.Zero) return;
        Drain();
        _stopped = true;
        if (_started)
        {
            FSEventStreamStop(_stream);
        }
        if (_scheduled) FSEventStreamInvalidate(_stream);
        if (_queue != IntPtr.Zero) DispatchSync(_queue, IntPtr.Zero, Barrier);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_stream != IntPtr.Zero) FSEventStreamRelease(_stream);
        if (_queue != IntPtr.Zero) DispatchRelease(_queue);
        if (_paths != IntPtr.Zero) CFRelease(_paths);
        if (_path != IntPtr.Zero) CFRelease(_path);
        if (_self.IsAllocated) _self.Free();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StreamContext
    {
        internal nint Version;
        internal IntPtr Info, Retain, Release, CopyDescription;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EventCallback(IntPtr stream, IntPtr info, nuint count, IntPtr paths, IntPtr flags, IntPtr ids);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DispatchCallback(IntPtr context);

    [DllImport(Foundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(Foundation)]
    private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, nint count, IntPtr callbacks);
    [DllImport(Foundation)]
    private static extern void CFRelease(IntPtr value);
    [DllImport(Events)]
    private static extern IntPtr FSEventStreamCreate(IntPtr allocator, EventCallback callback, ref StreamContext context,
        IntPtr paths, ulong sinceWhen, double latency, uint flags);
    [DllImport(Events)]
    private static extern void FSEventStreamSetDispatchQueue(IntPtr stream, IntPtr queue);
    [DllImport(Events)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool FSEventStreamStart(IntPtr stream);
    [DllImport(Events)] private static extern void FSEventStreamFlushSync(IntPtr stream);
    [DllImport(Events)] private static extern void FSEventStreamStop(IntPtr stream);
    [DllImport(Events)] private static extern void FSEventStreamInvalidate(IntPtr stream);
    [DllImport(Events)] private static extern void FSEventStreamRelease(IntPtr stream);
    [DllImport(SystemLibrary, EntryPoint = "dispatch_queue_create")]
    private static extern IntPtr DispatchQueueCreate([MarshalAs(UnmanagedType.LPUTF8Str)] string label, IntPtr attributes);
    [DllImport(SystemLibrary, EntryPoint = "dispatch_sync_f")]
    private static extern void DispatchSync(IntPtr queue, IntPtr context, DispatchCallback callback);
    [DllImport(SystemLibrary, EntryPoint = "dispatch_release")]
    private static extern void DispatchRelease(IntPtr queue);
}
#endif

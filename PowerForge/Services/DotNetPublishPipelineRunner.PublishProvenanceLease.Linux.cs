using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal sealed partial class PublishProvenanceLease
    {
        private sealed class LinuxDirectoryMutationWatcher : IDisposable
        {
            private const int InCloExec = 0x80000;
            private const int InNonBlock = 0x800;
            private const uint InModify = 0x00000002;
            private const uint InAttrib = 0x00000004;
            private const uint InCloseWrite = 0x00000008;
            private const uint InMovedFrom = 0x00000040;
            private const uint InMovedTo = 0x00000080;
            private const uint InCreate = 0x00000100;
            private const uint InDelete = 0x00000200;
            private const uint InDeleteSelf = 0x00000400;
            private const uint InMoveSelf = 0x00000800;
            private const uint InQueueOverflow = 0x00004000;
            private const uint InIgnored = 0x00008000;
            private const uint WatchMask = InModify | InAttrib | InCloseWrite |
                                           InMovedFrom | InMovedTo | InCreate | InDelete |
                                           InDeleteSelf | InMoveSelf;

            private readonly int _fileDescriptor;
            private readonly IReadOnlyDictionary<int, string> _directoriesByWatchDescriptor;
            private readonly Action<string?, bool> _onMutation;
            private readonly Thread _thread;
            private int _disposed;

            private LinuxDirectoryMutationWatcher(
                int fileDescriptor,
                IReadOnlyDictionary<int, string> directoriesByWatchDescriptor,
                Action<string?, bool> onMutation)
            {
                _fileDescriptor = fileDescriptor;
                _directoriesByWatchDescriptor = directoriesByWatchDescriptor;
                _onMutation = onMutation;
                _thread = new Thread(ReadEvents)
                {
                    IsBackground = true,
                    Name = "PowerForge Linux provenance watcher"
                };
                _thread.Start();
            }

            internal static LinuxDirectoryMutationWatcher Create(
                IEnumerable<string> directories,
                Action<string?, bool> onMutation)
            {
                int fileDescriptor = InotifyInit1(InNonBlock | InCloExec);
                if (fileDescriptor < 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Linux inotify could not be initialized.");

                try
                {
                    var watched = new Dictionary<int, string>();
                    foreach (string directory in directories.Distinct(StringComparer.Ordinal))
                    {
                        string fullPath = Path.GetFullPath(directory);
                        int watchDescriptor = InotifyAddWatch(fileDescriptor, fullPath, WatchMask);
                        if (watchDescriptor < 0)
                        {
                            throw new Win32Exception(
                                Marshal.GetLastWin32Error(),
                                $"Linux inotify could not watch provenance directory '{fullPath}'.");
                        }
                        watched[watchDescriptor] = fullPath;
                    }

                    return new LinuxDirectoryMutationWatcher(fileDescriptor, watched, onMutation);
                }
                catch
                {
                    Close(fileDescriptor);
                    throw;
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                Close(_fileDescriptor);
                _thread.Join(TimeSpan.FromSeconds(1));
            }

            private void ReadEvents()
            {
                var buffer = new byte[64 * 1024];
                while (Volatile.Read(ref _disposed) == 0)
                {
                    long bytesRead = Read(_fileDescriptor, buffer, (UIntPtr)buffer.Length).ToInt64();
                    if (bytesRead < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == 4)
                            continue;
                        if (error == 11)
                        {
                            Thread.Sleep(25);
                            continue;
                        }
                        if (Volatile.Read(ref _disposed) == 0)
                            _onMutation(null, true);
                        return;
                    }

                    int offset = 0;
                    while (offset + 16 <= bytesRead)
                    {
                        int watchDescriptor = BitConverter.ToInt32(buffer, offset);
                        uint mask = BitConverter.ToUInt32(buffer, offset + 4);
                        uint nameLength = BitConverter.ToUInt32(buffer, offset + 12);
                        int eventLength = checked(16 + (int)nameLength);
                        if (eventLength < 16 || offset + eventLength > bytesRead)
                        {
                            _onMutation(null, true);
                            return;
                        }

                        if ((mask & InQueueOverflow) != 0)
                        {
                            _onMutation(null, true);
                        }
                        else if ((mask & InIgnored) != 0)
                        {
                            if (Volatile.Read(ref _disposed) == 0 &&
                                _directoriesByWatchDescriptor.TryGetValue(watchDescriptor, out string? ignoredDirectory))
                            {
                                _onMutation(ignoredDirectory, false);
                            }
                        }
                        else if (_directoriesByWatchDescriptor.TryGetValue(watchDescriptor, out string? directory))
                        {
                            string name = ReadEventName(buffer, offset + 16, (int)nameLength);
                            _onMutation(name.Length == 0 ? directory : Path.Combine(directory, name), false);
                        }

                        offset += eventLength;
                    }
                }
            }

            private static string ReadEventName(byte[] buffer, int offset, int length)
            {
                int count = 0;
                while (count < length && buffer[offset + count] != 0)
                    count++;
                return count == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, offset, count);
            }

            [DllImport("libc", SetLastError = true)]
            private static extern int inotify_init1(int flags);

            [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
            private static extern int inotify_add_watch(int fileDescriptor, string path, uint mask);

            [DllImport("libc", SetLastError = true)]
            private static extern IntPtr read(int fileDescriptor, byte[] buffer, UIntPtr count);

            [DllImport("libc", SetLastError = true)]
            private static extern int close(int fileDescriptor);

            private static int InotifyInit1(int flags) => inotify_init1(flags);

            private static int InotifyAddWatch(int fileDescriptor, string path, uint mask)
                => inotify_add_watch(fileDescriptor, path, mask);

            private static IntPtr Read(int fileDescriptor, byte[] buffer, UIntPtr count)
                => read(fileDescriptor, buffer, count);

            private static int Close(int fileDescriptor) => close(fileDescriptor);
        }
    }
}

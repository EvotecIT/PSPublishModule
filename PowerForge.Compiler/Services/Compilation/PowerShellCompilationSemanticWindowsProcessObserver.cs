using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

/// <summary>
/// Observes one Windows process tree through a Job Object completion port and retains only direct-child effects.
/// Completion packets provide event detail while Job Object accounting proves that no launch packet was missed.
/// </summary>
internal sealed class PowerShellCompilationSemanticWindowsProcessObserver : IDisposable
{
    internal const string SourceIdentity = "Windows.JobObject.ProcessTree/1";
    private const uint JobObjectBasicAccountingInformation = 1;
    private const uint JobObjectAssociateCompletionPortInformation = 7;
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint JobObjectMessageActiveProcessZero = 4;
    private const uint JobObjectMessageNewProcess = 6;
    private const uint JobObjectMessageExitProcess = 7;
    private const uint JobObjectMessageAbnormalExitProcess = 8;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;
    private const int WaitTimeout = 258;
    private const ulong JobCompletionKey = 1;
    private const ulong BarrierCompletionKey = 2;
    private const ulong ShutdownCompletionKey = 3;

    private readonly object _sync = new();
    private readonly int _maximumItems;
    private readonly IntPtr _job;
    private readonly IntPtr _completionPort;
    private readonly Task _monitor;
    private readonly List<PowerShellCompilationSemanticProcessEffectObservation> _effects = new();
    private readonly Dictionary<uint, DirectProcess> _directProcesses = new();
    private readonly HashSet<uint> _observedJobProcesses = new();
    private readonly HashSet<uint> _activeAtCompletionBoundary = new();
    private volatile bool _disposed;
    private int _rootProcessId;
    private int _nextInvocation;
    private long _authoredBoundaryFileTime;
    private uint _barrierIssued;
    private uint _barrierAcknowledged;
    private Exception? _failure;

    internal PowerShellCompilationSemanticWindowsProcessObserver(int maximumItems)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Windows Job Object process observation requires Windows.");
        if (maximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        _maximumItems = maximumItems;
        _job = CreateJobObject(IntPtr.Zero, null);
        if (_job == IntPtr.Zero)
            throw NewWin32Exception("CreateJobObject");
        _completionPort = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
        if (_completionPort == IntPtr.Zero)
        {
            CloseHandle(_job);
            throw NewWin32Exception("CreateIoCompletionPort");
        }
        var limits = new JobObjectExtendedLimit
        {
            BasicLimitInformation = new JobObjectBasicLimit
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };
        if (!SetInformationJobObject(
                _job,
                JobObjectExtendedLimitInformation,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimit>()))
        {
            CloseHandle(_completionPort);
            CloseHandle(_job);
            throw NewWin32Exception("SetInformationJobObject(KillOnJobClose)");
        }
        var association = new JobObjectAssociateCompletionPort
        {
            CompletionKey = new IntPtr(unchecked((long)JobCompletionKey)),
            CompletionPort = _completionPort
        };
        if (!SetInformationJobObject(
                _job,
                JobObjectAssociateCompletionPortInformation,
                ref association,
                (uint)Marshal.SizeOf<JobObjectAssociateCompletionPort>()))
        {
            CloseHandle(_completionPort);
            CloseHandle(_job);
            throw NewWin32Exception("SetInformationJobObject");
        }
        _monitor = Task.Factory.StartNew(
            MonitorCompletionPort,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    internal void Attach(int processId)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        lock (_sync)
        {
            if (_rootProcessId != 0) throw new InvalidOperationException("A semantic process observer can attach only one host.");
            _rootProcessId = processId;
        }
        using var process = Process.GetProcessById(processId);
        if (!AssignProcessToJobObject(_job, process.Handle))
            throw NewWin32Exception("AssignProcessToJobObject");
    }

    internal void BeginAuthoredObservation(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new TimeoutException("The semantic process-observation start gate exceeded its deadline.");
        WaitForCompleteLaunchHistory(timeout, requireNoActiveProcesses: false);
        lock (_sync)
        {
            ThrowIfFailed();
            if (_rootProcessId == 0) throw new InvalidOperationException("The semantic process observer is not attached.");
            if (_authoredBoundaryFileTime != 0) throw new InvalidOperationException("Authored process observation has already started.");
            GetSystemTimePreciseAsFileTime(out var boundary);
            _authoredBoundaryFileTime = ToInt64(boundary);
        }
    }

    internal PowerShellCompilationSemanticProcessEffectObservation[] Complete(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var stopwatch = Stopwatch.StartNew();
        WaitForCompleteLaunchHistory(Remaining(timeout, stopwatch), requireNoActiveProcesses: false);

        lock (_sync)
        {
            ThrowIfFailed();
            foreach (var process in _directProcesses.Values)
            {
                if (WaitForSingleObject(process.Handle.DangerousGetHandle(), 0) != WaitObject0)
                    _activeAtCompletionBoundary.Add(process.ProcessId);
            }
        }

        var accounting = QueryAccounting();
        if (accounting.ActiveProcesses > 0 && !TerminateJobObject(_job, 1))
            throw NewWin32Exception("TerminateJobObject");
        WaitForCompleteLaunchHistory(Remaining(timeout, stopwatch), requireNoActiveProcesses: true);

        lock (_sync)
        {
            ThrowIfFailed();
            foreach (var process in _directProcesses.Values.ToArray())
            {
                if (!_activeAtCompletionBoundary.Contains(process.ProcessId) &&
                    WaitForSingleObject(process.Handle.DangerousGetHandle(), 0) != WaitObject0)
                    throw new InvalidOperationException(
                        $"Direct child invocation {process.Invocation} remained active after Job Object containment closed.");
                _directProcesses.Remove(process.ProcessId);
                if (!_activeAtCompletionBoundary.Contains(process.ProcessId))
                    AddExitEffect(process);
                process.Handle.Dispose();
            }
            return _effects.Select(Clone).ToArray();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PostQueuedCompletionStatus(_completionPort, 0, new UIntPtr(ShutdownCompletionKey), IntPtr.Zero);
        try { _monitor.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        lock (_sync)
        {
            foreach (var process in _directProcesses.Values) process.Handle.Dispose();
            _directProcesses.Clear();
        }
        CloseHandle(_completionPort);
        CloseHandle(_job);
    }

    private void MonitorCompletionPort()
    {
        while (!_disposed)
        {
            if (!GetQueuedCompletionStatus(
                    _completionPort,
                    out var message,
                    out var completionKey,
                    out var processPointer,
                    250))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == WaitTimeout) continue;
                Fail(NewWin32Exception("GetQueuedCompletionStatus", error));
                return;
            }
            var key = completionKey.ToUInt64();
            if (_disposed || key == ShutdownCompletionKey) return;
            try
            {
                if (key == BarrierCompletionKey)
                {
                    lock (_sync)
                    {
                        _barrierAcknowledged = Math.Max(_barrierAcknowledged, message);
                        Monitor.PulseAll(_sync);
                    }
                    continue;
                }
                if (key != JobCompletionKey)
                    throw new InvalidOperationException($"Unexpected process-observation completion key '{key}'.");
                var processId = unchecked((uint)processPointer.ToInt64());
                switch (message)
                {
                    case JobObjectMessageNewProcess:
                        ObserveLaunch(processId);
                        break;
                    case JobObjectMessageExitProcess:
                    case JobObjectMessageAbnormalExitProcess:
                        ObserveExit(processId);
                        break;
                    case JobObjectMessageActiveProcessZero:
                        lock (_sync)
                        {
                            Monitor.PulseAll(_sync);
                        }
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception);
                return;
            }
        }
    }

    private void ObserveLaunch(uint processId)
    {
        int rootProcessId;
        long authoredBoundary;
        lock (_sync)
        {
            rootProcessId = _rootProcessId;
            authoredBoundary = _authoredBoundaryFileTime;
        }
        var handle = OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, processId);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw NewWin32Exception($"OpenProcess({processId})");
        }
        if (!IsProcessInJob(handle.DangerousGetHandle(), _job, out var belongsToJob))
        {
            handle.Dispose();
            throw NewWin32Exception($"IsProcessInJob({processId})");
        }
        if (!belongsToJob)
        {
            handle.Dispose();
            throw new InvalidOperationException($"Process {processId} no longer belongs to the semantic observation Job Object.");
        }
        if (!GetProcessTimes(handle.DangerousGetHandle(), out var created, out _, out _, out _))
        {
            handle.Dispose();
            throw NewWin32Exception($"GetProcessTimes({processId})");
        }
        var creationFileTime = ToInt64(created);
        lock (_sync)
        {
            ThrowIfFailed();
            if (_observedJobProcesses.Count >= _maximumItems)
            {
                handle.Dispose();
                throw new InvalidOperationException(
                    $"Job process launches exceed the {_maximumItems}-item semantic observation limit.");
            }
            if (!_observedJobProcesses.Add(processId))
            {
                handle.Dispose();
                throw new InvalidOperationException($"Job process id {processId} was observed more than once; PID reuse cannot produce portable evidence.");
            }
            Monitor.PulseAll(_sync);
        }
        if (processId == rootProcessId || !IsAuthoredLaunch(creationFileTime, authoredBoundary))
        {
            handle.Dispose();
            return;
        }
        if (!TryGetParentProcessId(handle, out var parentProcessId))
        {
            handle.Dispose();
            throw NewWin32Exception($"NtQueryInformationProcess({processId})");
        }
        if (parentProcessId != unchecked((uint)rootProcessId))
        {
            handle.Dispose();
            return;
        }
        var executable = GetExecutableName(handle, processId);
        lock (_sync)
        {
            ThrowIfFailed();
            EnsureCapacity();
            if (_directProcesses.ContainsKey(processId))
                throw new InvalidOperationException($"Direct child process {processId} launched twice without an exit.");
            var invocation = ++_nextInvocation;
            _directProcesses.Add(processId, new DirectProcess(processId, invocation, executable, handle));
            _effects.Add(new PowerShellCompilationSemanticProcessEffectObservation
            {
                Sequence = _effects.Count + 1,
                Invocation = invocation,
                Kind = "NativeProcessLaunch",
                Executable = executable,
                ObservationSource = SourceIdentity
            });
            Monitor.PulseAll(_sync);
        }
    }

    private void ObserveExit(uint processId)
    {
        lock (_sync)
        {
            if (processId == _rootProcessId)
            {
                Monitor.PulseAll(_sync);
                return;
            }
            if (!_directProcesses.TryGetValue(processId, out var process)) return;
            _directProcesses.Remove(processId);
            if (!_activeAtCompletionBoundary.Contains(processId))
                AddExitEffect(process);
            process.Handle.Dispose();
            Monitor.PulseAll(_sync);
        }
    }

    private void AddExitEffect(DirectProcess process)
    {
        if (!GetExitCodeProcess(process.Handle.DangerousGetHandle(), out var nativeExitCode))
            throw NewWin32Exception($"GetExitCodeProcess({process.ProcessId})");
        EnsureCapacity();
        _effects.Add(new PowerShellCompilationSemanticProcessEffectObservation
        {
            Sequence = _effects.Count + 1,
            Invocation = process.Invocation,
            Kind = "NativeProcessExit",
            Executable = process.Executable,
            ExitCode = unchecked((int)nativeExitCode),
            ObservationSource = SourceIdentity
        });
    }

    private void WaitForCompleteLaunchHistory(TimeSpan timeout, bool requireNoActiveProcesses)
    {
        if (timeout <= TimeSpan.Zero)
            throw new TimeoutException("The semantic process-observation boundary exceeded its deadline.");
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var barrier = PostBarrier();
            lock (_sync)
            {
                while (_failure is null && _barrierAcknowledged < barrier && stopwatch.Elapsed < timeout)
                    Monitor.Wait(_sync, TimeSpan.FromMilliseconds(25));
                ThrowIfFailed();
                if (_barrierAcknowledged < barrier)
                    throw new TimeoutException("Timed out draining the semantic process-observation completion queue.");
            }

            var accounting = QueryAccounting();
            lock (_sync)
            {
                ThrowIfFailed();
                var observed = (uint)_observedJobProcesses.Count;
                if (IsCompleteLaunchHistory(observed, accounting.TotalProcesses) &&
                    (!requireNoActiveProcesses || accounting.ActiveProcesses == 0))
                    return;
            }

            if (stopwatch.Elapsed >= timeout)
                throw new TimeoutException(
                    "Job Object launch accounting did not match completion-port evidence before the observation boundary closed.");
            Thread.Sleep(10);
        }
    }

    private static TimeSpan Remaining(TimeSpan timeout, Stopwatch stopwatch)
    {
        var remaining = timeout - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException("The semantic process-observation boundary exceeded its deadline.");
        return remaining;
    }

    internal static bool IsCompleteLaunchHistory(uint observedLaunchPackets, uint totalJobProcesses)
    {
        if (observedLaunchPackets > totalJobProcesses)
            throw new InvalidOperationException(
                $"Job Object reported {totalJobProcesses} process launches but {observedLaunchPackets} unique launch packets were observed.");
        return observedLaunchPackets == totalJobProcesses;
    }

    internal static bool IsAuthoredLaunch(long creationFileTime, long authoredBoundaryFileTime)
    {
        if (authoredBoundaryFileTime == 0 || creationFileTime < authoredBoundaryFileTime)
            return false;
        if (creationFileTime == authoredBoundaryFileTime)
            throw new InvalidOperationException(
                "A process has an ambiguous creation timestamp at the authored-source boundary.");
        return true;
    }

    private uint PostBarrier()
    {
        uint barrier;
        lock (_sync)
        {
            ThrowIfFailed();
            barrier = ++_barrierIssued;
        }
        if (!PostQueuedCompletionStatus(_completionPort, barrier, new UIntPtr(BarrierCompletionKey), IntPtr.Zero))
            throw NewWin32Exception("PostQueuedCompletionStatus(Barrier)");
        return barrier;
    }

    private JobObjectBasicAccounting QueryAccounting()
    {
        var accounting = new JobObjectBasicAccounting();
        if (!QueryInformationJobObject(
                _job,
                JobObjectBasicAccountingInformation,
                ref accounting,
                (uint)Marshal.SizeOf<JobObjectBasicAccounting>(),
                out _))
            throw NewWin32Exception("QueryInformationJobObject(BasicAccounting)");
        return accounting;
    }

    private void EnsureCapacity()
    {
        if (_effects.Count >= _maximumItems)
            throw new InvalidOperationException(
                $"Direct child-process effects exceed the {_maximumItems}-item semantic observation limit.");
    }

    private void Fail(Exception exception)
    {
        lock (_sync)
        {
            _failure ??= exception;
            Monitor.PulseAll(_sync);
        }
    }

    private void ThrowIfFailed()
    {
        if (_failure is not null)
            throw new InvalidOperationException("Direct child-process Job Object observation failed.", _failure);
    }

    private static string GetExecutableName(SafeFileHandle process, uint processId)
    {
        var capacity = 32768;
        var path = new StringBuilder(capacity);
        if (!QueryFullProcessImageName(process.DangerousGetHandle(), 0, path, ref capacity))
            throw NewWin32Exception($"QueryFullProcessImageName({processId})");
        var executable = Path.GetFileName(path.ToString());
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException($"Direct child process {processId} has no portable executable name.");
        return executable;
    }

    private static bool TryGetParentProcessId(SafeFileHandle process, out uint parentProcessId)
    {
        var information = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(
            process.DangerousGetHandle(),
            0,
            ref information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        parentProcessId = unchecked((uint)information.InheritedFromUniqueProcessId.ToInt64());
        return status == 0;
    }

    private static PowerShellCompilationSemanticProcessEffectObservation Clone(
        PowerShellCompilationSemanticProcessEffectObservation effect)
        => new()
        {
            Sequence = effect.Sequence,
            Invocation = effect.Invocation,
            Kind = effect.Kind,
            Executable = effect.Executable,
            ExitCode = effect.ExitCode,
            ObservationSource = effect.ObservationSource
        };

    private static System.ComponentModel.Win32Exception NewWin32Exception(string operation, int? error = null)
    {
        var exception = error.HasValue
            ? new System.ComponentModel.Win32Exception(error.Value)
            : new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new System.ComponentModel.Win32Exception(exception.NativeErrorCode, $"{operation} failed: {exception.Message}");
    }

    private sealed class DirectProcess
    {
        internal DirectProcess(uint processId, int invocation, string executable, SafeFileHandle handle)
        {
            ProcessId = processId;
            Invocation = invocation;
            Executable = executable;
            Handle = handle;
        }

        internal uint ProcessId { get; }
        internal int Invocation { get; }
        internal string Executable { get; }
        internal SafeFileHandle Handle { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccounting
    {
        internal long TotalUserTime;
        internal long TotalKernelTime;
        internal long ThisPeriodTotalUserTime;
        internal long ThisPeriodTotalKernelTime;
        internal uint TotalPageFaultCount;
        internal uint TotalProcesses;
        internal uint ActiveProcesses;
        internal uint TotalTerminatedProcesses;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        internal uint Low;
        internal uint High;
    }

    private static long ToInt64(FileTime value)
        => unchecked((long)(((ulong)value.High << 32) | value.Low));

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectAssociateCompletionPort
    {
        internal IntPtr CompletionKey;
        internal IntPtr CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimit
    {
        internal long PerProcessUserTimeLimit;
        internal long PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize;
        internal UIntPtr MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass;
        internal uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount;
        internal ulong WriteOperationCount;
        internal ulong OtherOperationCount;
        internal ulong ReadTransferCount;
        internal ulong WriteTransferCount;
        internal ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimit
    {
        internal JobObjectBasicLimit BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit;
        internal UIntPtr JobMemoryLimit;
        internal UIntPtr PeakProcessMemoryUsed;
        internal UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2_0;
        internal IntPtr Reserved2_1;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        uint informationClass,
        ref JobObjectAssociateCompletionPort information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        uint informationClass,
        ref JobObjectExtendedLimit information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        uint informationClass,
        ref JobObjectBasicAccounting information,
        uint informationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateIoCompletionPort(
        IntPtr fileHandle,
        IntPtr existingCompletionPort,
        UIntPtr completionKey,
        uint concurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetQueuedCompletionStatus(
        IntPtr completionPort,
        out uint numberOfBytes,
        out UIntPtr completionKey,
        out IntPtr overlapped,
        uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostQueuedCompletionStatus(
        IntPtr completionPort,
        uint numberOfBytes,
        UIntPtr completionKey,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        IntPtr process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll")]
    private static extern void GetSystemTimePreciseAsFileTime(out FileTime systemTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        uint flags,
        StringBuilder executableName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr process,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);
}

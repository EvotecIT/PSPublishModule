using Avalonia.Threading;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Queue;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class BuildViewModel
{
    // A verbose build must not enqueue a dispatcher callback for every process line.
    private sealed class BufferedBuildProgress(Action<string> append) : IProgress<ReleaseBuildProgress>
    {
        private const int MaxPendingCharacters = 128 * 1024;
        private readonly object _gate = new();
        private readonly Queue<string> _pending = new();
        private int _pendingCharacters;
        private bool _flushScheduled;

        public void Report(ReleaseBuildProgress update)
        {
            var detail = StudioOutputSanitizer.Sanitize($"{update.Phase} · {update.State} · {update.Detail}");
            var line = $"\n[{DateTime.Now:HH:mm:ss}] {detail}";
            lock (_gate)
            {
                _pending.Enqueue(line);
                _pendingCharacters += line.Length;
                while (_pendingCharacters > MaxPendingCharacters)
                    _pendingCharacters -= _pending.Dequeue().Length;
                if (_flushScheduled) return;
                _flushScheduled = true;
            }

            Dispatcher.UIThread.Post(() => DispatcherTimer.RunOnce(Flush, TimeSpan.FromMilliseconds(50)));
        }

        private void Flush()
        {
            string text;
            lock (_gate)
            {
                text = string.Concat(_pending);
                _pending.Clear();
                _pendingCharacters = 0;
                _flushScheduled = false;
            }
            if (text.Length > 0) append(text);
        }

        public void FlushNow() => Flush();
    }
}

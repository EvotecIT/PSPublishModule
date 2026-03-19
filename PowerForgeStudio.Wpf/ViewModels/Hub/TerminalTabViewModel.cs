using PowerForgeStudio.Orchestrator.Terminal;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class TerminalTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly TerminalSessionService _sessionService;
    private readonly string _workingDirectory;
    private ITerminalSession? _session;
    private bool _isConnected;
    private string _title;
    private bool _disposed;

    public TerminalTabViewModel(TerminalSessionService sessionService, string workingDirectory)
    {
        _sessionService = sessionService;
        _workingDirectory = workingDirectory;
        _title = $"Terminal — {System.IO.Path.GetFileName(workingDirectory)}";
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string WorkingDirectory => _workingDirectory;

    public event Action<byte[]>? OutputAvailable;

    /// <summary>
    /// Called by the View once WebView2 and xterm.js are fully initialized.
    /// Only then do we start the ConPTY session so no output is lost.
    /// </summary>
    public async void StartSession(int initialCols = 120, int initialRows = 30)
    {
        if (_disposed) return;

        // Dispose old session if restarting
        if (_session is not null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.Exited -= OnExited;
            await _session.DisposeAsync().ConfigureAwait(true);
            _session = null;
        }

        try
        {
            _session = _sessionService.CreateSession(_workingDirectory, initialCols, initialRows);
            _session.OutputReceived += OnOutputReceived;
            _session.Exited += OnExited;
            IsConnected = true;
        }
        catch (Exception ex)
        {
            Title = $"Terminal — failed: {ex.Message}";
        }
    }

    public void SendInput(byte[] data)
    {
        if (_disposed || _session is null) return;
        _session.WriteInput(data);
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || _session is null || cols <= 0 || rows <= 0) return;
        _session.Resize(cols, rows);
    }

    private void OnOutputReceived(byte[] data)
    {
        OutputAvailable?.Invoke(data);
    }

    private void OnExited(int exitCode)
    {
        // Marshal to UI thread since this fires from the ConPTY read thread
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsConnected = false;
            Title = $"Terminal (exited: {exitCode})";
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_session is not null)
        {
            _session.OutputReceived -= OnOutputReceived;
            _session.Exited -= OnExited;
            await _session.DisposeAsync().ConfigureAwait(false);
            _session = null;
        }
    }
}

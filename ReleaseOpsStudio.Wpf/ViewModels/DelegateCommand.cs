using System.Windows.Input;

namespace ReleaseOpsStudio.Wpf.ViewModels;

public sealed class DelegateCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public DelegateCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
        => _canExecute?.Invoke((T?)parameter) ?? true;

    public void Execute(object? parameter)
        => _execute((T?)parameter);

    public void RaiseCanExecuteChanged()
        => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

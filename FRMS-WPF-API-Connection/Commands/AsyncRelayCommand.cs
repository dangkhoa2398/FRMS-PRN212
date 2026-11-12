using System.Windows.Input;

namespace FRMS.Wpf.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute) => _execute = execute;

    public bool CanExecute(object? parameter) => !_isRunning;

    public event EventHandler? CanExecuteChanged;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

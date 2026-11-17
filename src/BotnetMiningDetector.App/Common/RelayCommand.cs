using System.Windows.Input;

namespace BotnetMiningDetector.App.Common;

// comando para acciones de la interfaz
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private bool _isRunning;

    public event EventHandler? CanExecuteChanged;

    // crea un comando asincrono sin parametro
    public RelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        : this(_ => executeAsync(), canExecute is null ? null : _ => canExecute())
    {
    }

    // crea un comando sincronico
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ =>
        {
            execute();
            return Task.CompletedTask;
        }, canExecute is null ? null : _ => canExecute())
    {
    }

    // crea un comando asincrono con parametro
    public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    // indica si el comando puede ejecutarse
    public bool CanExecute(object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke(parameter) ?? true);
    }

    // ejecuta la accion del comando
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            _isRunning = true;
            RaiseCanExecuteChanged();
            await _executeAsync(parameter);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    // actualiza el estado del comando
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

using System;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IFrmsHealthService _healthService;
    private string _status = "Not tested";
    private string _details = "Click the button to verify FRMS API and its database health check.";
    private bool _isChecking;

    public MainViewModel(IFrmsHealthService healthService)
    {
        _healthService = healthService ?? throw new ArgumentNullException(nameof(healthService));
        CheckConnectionCommand = new CheckConnectionAsyncCommand(this);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public string Details
    {
        get => _details;
        private set
        {
            if (_details == value) return;
            _details = value;
            OnPropertyChanged();
        }
    }

    // Keep the same property name used by MainWindow.xaml.
    public ICommand CheckConnectionCommand { get; }

    private async Task CheckConnectionAsync()
    {
        if (_isChecking) return;
        _isChecking = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Checking...";
        Details = "Contacting http://localhost:5164/health.";

        try
        {
            var result = await _healthService.CheckAsync();
            Status = result.IsHealthy ? "Connected" : "Unhealthy";
            Details = $"HTTP {result.StatusCode}; response: {result.Body}";
        }
        catch (HttpRequestException ex)
        {
            Status = "Connection failed";
            Details = $"HTTP connection failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            Status = "Timeout";
            Details = "The backend did not respond before the request timeout.";
        }
        catch (Exception ex)
        {
            Status = "Error";
            Details = ex.Message;
        }
        finally
        {
            _isChecking = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class CheckConnectionAsyncCommand(MainViewModel owner) : ICommand
    {
        public bool CanExecute(object? parameter) => !owner._isChecking;
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public async void Execute(object? parameter) => await owner.CheckConnectionAsync();
    }
}

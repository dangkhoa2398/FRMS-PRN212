using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public partial class MainViewModel(IFrmsHealthService healthService) : ObservableObject
{
    [ObservableProperty]
    private string status = "Not tested";

    [ObservableProperty]
    private string details = "Click the button to verify FRMS API and its database health check.";

    [RelayCommand]
    private async Task CheckConnectionAsync()
    {
        Status = "Checking...";
        Details = "Contacting http://localhost:5164/health.";

        try
        {
            var result = await healthService.CheckAsync();
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
    }
}

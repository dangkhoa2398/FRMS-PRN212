using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CustomerApiService _api;
    private readonly IFrmsHealthService _health;
    private string _currentRoute = "home";

    public MainViewModel(CustomerApiService api, IFrmsHealthService health)
    {
        _api = api;
        _health = health;
        CurrentPage = new CustomerPageViewModel("Home", "Your customer overview.");
    }

    [ObservableProperty] private string _phoneNumber = "";
    [ObservableProperty] private string _loginError = "";
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private bool _isLoggingIn;
    [ObservableProperty] private string _status = "Not tested";
    [ObservableProperty] private string _details = "Check your backend database connection.";
    [ObservableProperty] private CustomerPageViewModel _currentPage;

    [RelayCommand]
    private async Task LoginAsync(PasswordBox passwordBox)
    {
        if (IsLoggingIn || passwordBox is null) return;
        LoginError = "";
        if (string.IsNullOrWhiteSpace(PhoneNumber) || passwordBox.Password.Length == 0)
        {
            LoginError = "Enter your phone number and password.";
            return;
        }

        IsLoggingIn = true;
        try
        {
            var (success, error) = await _api.LoginAsync(PhoneNumber.Trim(), passwordBox.Password);
            if (!success) { LoginError = error; return; }
            passwordBox.Clear();
            IsAuthenticated = true;
            await NavigateAsync("home");
        }
        catch (HttpRequestException ex) { LoginError = $"Backend connection failed: {ex.Message}"; }
        catch (TaskCanceledException) { LoginError = "Login request timed out."; }
        catch (JsonException) { LoginError = "Unexpected API response format."; }
        finally { IsLoggingIn = false; }
    }

    [RelayCommand]
    private void Logout()
    {
        _api.Logout();
        PhoneNumber = "";
        LoginError = "";
        IsAuthenticated = false;
    }

    [RelayCommand]
    private async Task NavigateAsync(string? route)
    {
        if (!IsAuthenticated) return;
        _currentRoute = route ?? "home";
        (string title, string desc, string path) = _currentRoute switch
        {
            "home" => ("Home", "Customer account overview", "api/v1/auth/me"),
            "storage" => ("Find Storage", "Browse available storage facilities", "api/v1/facilities?page=1&pageSize=20"),
            "reservations" => ("My Reservations", "Your storage reservations", "api/v1/reservations?page=1&pageSize=20"),
            "contracts" => ("My Contracts", "Your current and past rental contracts", "api/v1/contracts?page=1&pageSize=20"),
            "invoices" => ("Invoices", "Your invoices and payment status", "api/v1/invoices?page=1&pageSize=20"),
            "support" => ("Support", "Customer support tickets", "api/v1/support-tickets?page=1&pageSize=20"),
            "account" => ("Account Settings", "Your profile", "api/v1/auth/me"),
            _ => ("Home", "Customer account overview", "api/v1/auth/me")
        };
        var page = new CustomerPageViewModel(title, desc);
        CurrentPage = page;
        page.IsLoading = true;
        try
        {
            using var json = await _api.GetAsync(path);
            if (CurrentPage != page) return;
            var root = json.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            JsonElement array = data;
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("items", out var items)) array = items;
            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                    page.Items.Add(Describe(item));
                page.Message = page.Items.Count == 0 ? "No records found." : $"{page.Items.Count} records loaded.";
            }
            else
            {
                page.Items.Add(Describe(data));
                page.Message = "Loaded from FRMS API.";
            }
        }
        catch (HttpRequestException ex) { page.Message = $"API error: {ex.Message}"; }
        catch (JsonException) { page.Message = "Could not parse the API response."; }
        finally { page.IsLoading = false; }
    }

    private static string Describe(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return item.ToString();
        var parts = new System.Collections.Generic.List<string>();
        foreach (var prop in item.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                parts.Add($"{prop.Name}: {prop.Value}");
            if (parts.Count >= 7) break;
        }
        return parts.Count == 0 ? "Record available (see API for details)" : string.Join("   |   ", parts);
    }

    [RelayCommand]
    private Task RefreshAsync() => NavigateAsync(_currentRoute);

    [RelayCommand]
    private async Task CheckConnectionAsync()
    {
        Status = "Checking...";
        try
        {
            var result = await _health.CheckAsync();
            Status = result.IsHealthy ? "Connected" : "Unhealthy";
            Details = $"HTTP {result.StatusCode}: {result.Body}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Status = "Unavailable";
            Details = ex.Message;
        }
    }
}

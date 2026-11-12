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
        LoginCommand = new AsyncRelayCommand<PasswordBox>(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CheckConnectionCommand = new AsyncRelayCommand(CheckConnectionAsync);
    }

    private string _phoneNumber = "";
    public string PhoneNumber { get => _phoneNumber; set => SetProperty(ref _phoneNumber, value); }
    private string _loginError = "";
    public string LoginError { get => _loginError; set => SetProperty(ref _loginError, value); }
    private bool _isAuthenticated;
    public bool IsAuthenticated { get => _isAuthenticated; set => SetProperty(ref _isAuthenticated, value); }
    private bool _isLoggingIn;
    public bool IsLoggingIn { get => _isLoggingIn; set => SetProperty(ref _isLoggingIn, value); }
    private string _status = "Not tested";
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    private string _details = "Check your backend database connection.";
    public string Details { get => _details; set => SetProperty(ref _details, value); }
    private CustomerPageViewModel _currentPage = new("Home", "Customer account overview.");
    public CustomerPageViewModel CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }

    public IAsyncRelayCommand<PasswordBox> LoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CheckConnectionCommand { get; }

    private async Task LoginAsync(PasswordBox? passwordBox)
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

    private void Logout()
    {
        _api.Logout();
        PhoneNumber = "";
        LoginError = "";
        IsAuthenticated = false;
    }

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

    private Task RefreshAsync() => NavigateAsync(_currentRoute);

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

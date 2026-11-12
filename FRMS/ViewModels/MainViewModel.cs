using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CustomerApiService _api;
    private readonly IFrmsHealthService _health;
    private CancellationTokenSource? _navigationCts;
    private string _currentRoute = "home";

    public MainViewModel(CustomerApiService api, IFrmsHealthService health)
    {
        _api = api;
        _health = health;
        LoginCommand = new AsyncRelayCommand<PasswordBox>(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
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
    private CustomerPageViewModel _currentPage = new("home", "Home", "Customer account overview.");
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
        _navigationCts?.Cancel();
        _api.Logout();
        PhoneNumber = "";
        LoginError = "";
        IsAuthenticated = false;
        CurrentPage = new CustomerPageViewModel("home", "Home", "Customer account overview.");
    }

    private async Task NavigateAsync(string? route)
    {
        if (!IsAuthenticated) return;
        _currentRoute = route ?? "home";
        var selectedRoute = _currentRoute;
        var (title, description, path) = selectedRoute switch
        {
            "home" => ("Home", "Customer information and account overview", "api/v1/auth/me"),
            "storage" => ("Find Storage", "Browse facilities and find storage space", "api/v1/facilities?page=1&pageSize=20"),
            "reservations" => ("My Reservations", "Track storage bookings and handover status", "api/v1/reservations?page=1&pageSize=20"),
            "contracts" => ("My Contracts", "Manage your rental contracts", "api/v1/contracts?page=1&pageSize=20"),
            "invoices" => ("Invoices", "Track rental invoices and payments", "api/v1/invoices?page=1&pageSize=20"),
            "support" => ("Support Requests", "Your customer support tickets", "api/v1/support-tickets?page=1&pageSize=20"),
            "account" => ("Account Settings", "Your profile information", "api/v1/auth/me"),
            _ => ("Home", "Customer information and account overview", "api/v1/auth/me")
        };

        // Cancel previous HTTP navigation; a stale response must not replace the selected page.
        _navigationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _navigationCts = cts;
        var page = new CustomerPageViewModel(selectedRoute, title, description);
        CurrentPage = page;
        page.IsLoading = true;

        try
        {
            using var json = await _api.GetAsync(path, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(CurrentPage, page) || !IsAuthenticated)
                return;
            var root = json.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var items = data;
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("items", out var i)) items = i;
                else if (data.TryGetProperty("data", out var inner)) items = inner;
            }

            var records = new List<CustomerRecord>();
            if (items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                    records.Add(ToRecord(item, selectedRoute));
            }
            else if (items.ValueKind == JsonValueKind.Object)
            {
                records.Add(ToRecord(items, selectedRoute));
            }
            page.SetRecords(records);
            page.Message = records.Count == 0 ? "No data returned for this page." :
                $"{records.Count} record(s) loaded from the backend.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (HttpRequestException ex)
        {
            if (ReferenceEquals(CurrentPage, page))
                page.Message = $"Could not load this page: {ex.Message}";
        }
        catch (JsonException)
        {
            if (ReferenceEquals(CurrentPage, page))
                page.Message = "The API returned an unexpected JSON response.";
        }
        finally
        {
            if (ReferenceEquals(CurrentPage, page))
                page.IsLoading = false;
            if (ReferenceEquals(_navigationCts, cts))
                _navigationCts = null;
            cts.Dispose();
        }
    }

    private static CustomerRecord ToRecord(JsonElement element, string route)
    {
        static string Field(JsonElement obj, params string[] names)
        {
            if (obj.ValueKind != JsonValueKind.Object) return "";
            foreach (var name in names)
                if (obj.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    return value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? "" : value.ToString();
            return "";
        }
        string title = route switch
        {
            "storage" => Field(element, "name", "facilityName", "unitTypeName"),
            "reservations" => Field(element, "unitTypeName", "facilityName", "reservationId"),
            "contracts" => Field(element, "unitTypeName", "storageUnitCode", "contractId"),
            "invoices" => Field(element, "invoiceNumber", "invoiceCode", "invoiceId"),
            "support" => Field(element, "title", "subject", "ticketId"),
            _ => Field(element, "fullName", "name", "phoneNumber", "userAccountId")
        };
        string subtitle = route switch
        {
            "storage" => Field(element, "address", "location"),
            "reservations" => Field(element, "facilityName", "reservationId"),
            "contracts" => Field(element, "facilityName", "contractId"),
            "invoices" => Field(element, "contractId", "invoiceId"),
            "support" => Field(element, "ticketId", "description"),
            _ => Field(element, "email", "phoneNumber", "role")
        };
        var fields = new List<string>();
        if (element.ValueKind != JsonValueKind.Object)
            return new CustomerRecord { Title = element.ToString() };
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                or JsonValueKind.True or JsonValueKind.False)
                fields.Add($"{property.Name}: {property.Value}");
        }
        return new CustomerRecord
        {
            Title = string.IsNullOrWhiteSpace(title) ? "FRMS record" : title,
            Subtitle = subtitle,
            Status = Field(element, "status", "paymentStatus"),
            Detail1 = fields.Count > 0 ? fields[0] : "",
            Detail2 = fields.Count > 1 ? fields[1] : "",
            Detail3 = fields.Count > 2 ? fields[2] : "",
            Detail4 = fields.Count > 3 ? fields[3] : ""
        };
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

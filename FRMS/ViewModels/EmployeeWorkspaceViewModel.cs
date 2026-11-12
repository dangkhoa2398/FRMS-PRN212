using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class EmployeeWorkItem
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail1 { get; init; } = "";
    public string Detail2 { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed class EmployeeWorkspaceViewModel : ObservableObject
{
    private readonly CustomerApiService _api;
    private CancellationTokenSource? _loading;
    public EmployeeWorkspaceViewModel(CustomerApiService api)
    {
        _api = api;
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
    }

    private string _role = "";
    public string Role { get => _role; private set => SetProperty(ref _role, value); }
    public bool IsManager => Role == "FACILITY_MANAGER";
    private string _displayName = "Employee";
    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    private string _facilityId = "";
    public string FacilityId { get => _facilityId; private set => SetProperty(ref _facilityId, value); }
    private string _route = "booking";
    public string Route { get => _route; private set => SetProperty(ref _route, value); }
    private string _title = "Bookings";
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    private string _description = "";
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    private string _message = "";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public ObservableCollection<EmployeeWorkItem> Items { get; } = new();

    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    public async Task InitializeAsync(string role)
    {
        _loading?.Cancel();
        Role = role;
        OnPropertyChanged(nameof(IsManager));
        try
        {
            using var doc = await _api.GetAsync("api/v1/auth/me");
            var data = JsonField(doc.RootElement, "data");
            var profile = JsonField(data, "profile");
            DisplayName = Field(profile, "fullName") is var name && !string.IsNullOrWhiteSpace(name) ? name : "Employee";
            FacilityId = Field(profile, "facilityId");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Message = "Employee profile could not be loaded: " + ex.Message;
        }
        await NavigateAsync("booking");
    }

    public void Reset()
    {
        _loading?.Cancel();
        Items.Clear();
        DisplayName = "Employee";
        FacilityId = "";
        Role = "";
        OnPropertyChanged(nameof(IsManager));
        Message = "";
    }

    private Task RefreshAsync() => NavigateAsync(Route);

    private async Task NavigateAsync(string? route)
    {
        _loading?.Cancel();
        var cts = new CancellationTokenSource();
        _loading = cts;
        Route = route ?? "booking";
        Items.Clear();
        (string title, string description, string? path) = Route switch
        {
            "booking" => ("Booking Assistance", "Reservations, customer arrivals and handover work", IsManager ? "api/v1/reservations?page=1&pageSize=20" : "api/v1/staff/work-items?page=1&pageSize=20"),
            "storage" => ("Storage Management", "Rental units and facility operations", IsManager && Guid.TryParse(FacilityId, out _) ? $"api/v1/facilities/{FacilityId}/storage-units?page=1&pageSize=20" : "api/v1/contracts?page=1&pageSize=20"),
            "access" => ("Check-in / Check-out", "Scheduled visits and access activity", IsManager ? "api/v1/visits?page=1&pageSize=20" : "api/v1/staff/work-items?page=1&pageSize=20"),
            "support" => ("Support Tickets", "Requests submitted by customers", "api/v1/support-tickets?page=1&pageSize=20"),
            _ => ("Booking Assistance", "Reservations and handover work", "api/v1/staff/work-items?page=1&pageSize=20")
        };
        Title = title;
        Description = description;
        Message = "Loading...";
        if (path is null) { Message = "Facility assignment is required before viewing storage units."; return; }
        IsLoading = true;
        try
        {
            using var doc = await _api.GetAsync(path, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_loading, cts)) return;
            var data = JsonField(doc.RootElement, "data");
            if (data.ValueKind == JsonValueKind.Undefined) data = doc.RootElement;
            if (data.ValueKind == JsonValueKind.Object)
            {
                var nested = JsonField(data, "items");
                if (nested.ValueKind == JsonValueKind.Array) data = nested;
            }
            if (data.ValueKind == JsonValueKind.Array)
                foreach (var element in data.EnumerateArray()) Items.Add(Map(element));
            else if (data.ValueKind == JsonValueKind.Object) Items.Add(Map(data));
            Message = Items.Count == 0 ? "No work items found." : $"{Items.Count} record(s) loaded.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ReferenceEquals(_loading, cts)) Message = "Unable to load this module: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_loading, cts)) { _loading = null; IsLoading = false; }
            cts.Dispose();
        }
    }

    private static JsonElement JsonField(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : default;

    private static string Field(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            var val = JsonField(obj, name);
            if (val.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                return val.ToString();
        }
        return "";
    }

    private EmployeeWorkItem Map(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return new EmployeeWorkItem { Title = obj.ToString() };
        var customer = JsonField(obj, "customer");
        string code = Field(obj, "unitCode", "storageUnitCode", "unitTypeName", "workType", "visitType", "category", "invoiceType");
        string status = Field(obj, "status");
        string subject = Field(obj, "title", "subject", "name", "description");
        string customerName = Field(customer, "fullName");
        return Route switch
        {
            "booking" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Reservation",
                Subtitle = !string.IsNullOrEmpty(customerName) ? customerName : Field(obj, "facilityName"),
                Status = status,
                Detail1 = "Start: " + Field(obj, "startMonth", "scheduledDate"),
                Detail2 = "End: " + Field(obj, "endMonth"),
                Description = Field(obj, "unitTypeName", "resultNote")
            },
            "storage" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code : "Storage unit",
                Subtitle = Field(obj, "unitTypeName", "location", "unitTypeMode"),
                Status = status,
                Detail1 = "Type: " + Field(obj, "unitTypeMode", "unitTypeName"),
                Detail2 = "Period: " + Field(obj, "startMonth") + " – " + Field(obj, "endMonth"),
                Description = Field(obj, "description")
            },
            "access" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Facility visit",
                Subtitle = customerName,
                Status = status,
                Detail1 = "Scheduled: " + Field(obj, "scheduledDate", "visitDate"),
                Detail2 = "Visit type: " + Field(obj, "visitType", "workType"),
                Description = Field(obj, "resultNote")
            },
            _ => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Support request",
                Subtitle = !string.IsNullOrEmpty(subject) ? subject : "Customer support",
                Status = status,
                Detail1 = "Assignee: " + Field(obj, "assignedEmployeeName", "employeeName"),
                Description = Field(obj, "content", "description", "message")
            }
        };
    }
}

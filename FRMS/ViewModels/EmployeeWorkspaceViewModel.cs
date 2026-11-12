using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
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
    public string Category { get; init; } = "";
    public string DateValue { get; init; } = "";
    public string SearchText => string.Join(" ", Title, Subtitle, Status, Detail1, Detail2, Description, Category);
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
    private string _route = "task";
    public string Route { get => _route; private set => SetProperty(ref _route, value); }
    private string _title = "Task";
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    private string _description = "";
    public string Description { get => _description; private set => SetProperty(ref _description, value); }
    private string _message = "";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    private readonly List<EmployeeWorkItem> _allItems = new();
    public ObservableCollection<EmployeeWorkItem> Items { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "All" };
    public ObservableCollection<string> CategoryOptions { get; } = new() { "All" };
    public bool ShowFilters => Route is "storage" or "access" or "support";
    public bool ShowDateFilter => Route == "access";
    private string _searchText = "";
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) ApplyFilters(); } }
    private string _selectedStatus = "All";
    public string SelectedStatus { get => _selectedStatus; set { if (SetProperty(ref _selectedStatus, value)) ApplyFilters(); } }
    private string _selectedCategory = "All";
    public string SelectedCategory { get => _selectedCategory; set { if (SetProperty(ref _selectedCategory, value)) ApplyFilters(); } }
    private DateTime? _selectedDate;
    public DateTime? SelectedDate { get => _selectedDate; set { if (SetProperty(ref _selectedDate, value)) ApplyFilters(); } }

    private void UpdateFilters(IEnumerable<EmployeeWorkItem> items)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        StatusOptions.Clear();
        StatusOptions.Add("All");
        foreach (var status in _allItems.Select(x => x.Status).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            StatusOptions.Add(status);
        CategoryOptions.Clear();
        CategoryOptions.Add("All");
        foreach (var category in _allItems.Select(x => x.Category).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            CategoryOptions.Add(category);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var result = _allItems.Where(x =>
            (SelectedStatus == "All" || x.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase)) &&
            (SelectedCategory == "All" || x.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SearchText) || x.SearchText.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (!ShowDateFilter || !SelectedDate.HasValue || (DateTime.TryParse(x.DateValue, out var date) && date.Date == SelectedDate.Value.Date)));
        Items.Clear();
        foreach (var item in result) Items.Add(item);
    }

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
        await NavigateAsync("task");
    }

    public void Reset()
    {
        _loading?.Cancel();
        Items.Clear();
        _allItems.Clear();
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
        Route = route is "booking" ? "task" : route ?? "task";
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(ShowDateFilter));
        _allItems.Clear();
        Items.Clear();
        SearchText = "";
        SelectedStatus = "All";
        SelectedCategory = "All";
        SelectedDate = null;
        (string title, string description, string? path) = Route switch
        {
            "task" => ("Task", "Today’s assigned work, reservations and customer handovers", IsManager ? "api/v1/reservations?page=1&pageSize=20" : "api/v1/staff/work-items?page=1&pageSize=20"),
            "storage" => ("Storage Management", "Rental units and facility operations", IsManager && Guid.TryParse(FacilityId, out _) ? $"api/v1/facilities/{FacilityId}/storage-units?page=1&pageSize=20" : "api/v1/contracts?page=1&pageSize=20"),
            "access" => ("Check-in / Check-out", "Scheduled visits and access activity", IsManager ? "api/v1/visits?page=1&pageSize=20" : "api/v1/staff/work-items?page=1&pageSize=20"),
            "support" => ("Support Tickets", "Requests submitted by customers", "api/v1/support-tickets?page=1&pageSize=20"),
            _ => ("Task", "Reservations and handover work", IsManager ? "api/v1/reservations?page=1&pageSize=20" : "api/v1/staff/work-items?page=1&pageSize=20")
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
            var records = new List<EmployeeWorkItem>();
            if (data.ValueKind == JsonValueKind.Array)
                foreach (var element in data.EnumerateArray()) records.Add(Map(element));
            else if (data.ValueKind == JsonValueKind.Object) records.Add(Map(data));
            UpdateFilters(records);
            Message = records.Count == 0 ? "No work items found." : $"{records.Count} record(s) loaded (filters apply to this page).";
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
            "task" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Reservation",
                Subtitle = !string.IsNullOrEmpty(customerName) ? customerName : Field(obj, "facilityName"),
                Status = status,
                Detail1 = "Start: " + Field(obj, "startMonth", "scheduledDate"),
                Detail2 = "End: " + Field(obj, "endMonth"),
                Description = Field(obj, "unitTypeName", "resultNote"),
                Category = Field(obj, "workType", "unitTypeMode"),
                DateValue = Field(obj, "scheduledDate")
            },
            "storage" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code : "Storage unit",
                Subtitle = Field(obj, "unitTypeName", "location", "unitTypeMode"),
                Status = status,
                Detail1 = "Type: " + Field(obj, "unitTypeMode", "unitTypeName", "mode"),
                Detail2 = !string.IsNullOrWhiteSpace(Field(obj, "startMonth")) ?
                    "Rental period: " + Field(obj, "startMonth") + " – " + Field(obj, "endMonth") :
                    "Location: " + Field(obj, "location", "locationDescription"),
                Description = Field(obj, "description", "size"),
                Category = Field(obj, "unitTypeName", "unitTypeMode", "mode")
            },
            "access" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Facility visit",
                Subtitle = customerName,
                Status = status,
                Detail1 = "Scheduled: " + Field(obj, "scheduledDate", "visitDate"),
                Detail2 = "Visit type: " + Field(obj, "visitType", "workType"),
                Description = Field(obj, "resultNote", "customerName"),
                Category = Field(obj, "visitType", "workType"),
                DateValue = Field(obj, "scheduledDate", "visitDate")
            },
            _ => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Support request",
                Subtitle = !string.IsNullOrEmpty(subject) ? subject : "Customer support",
                Status = status,
                Detail1 = "Assignee: " + (Field(obj, "assignedEmployeeName", "employeeName") is var employee && !string.IsNullOrWhiteSpace(employee) ? employee : "Unassigned"),
                Detail2 = "Category: " + Field(obj, "category", "ticketCategory"),
                Description = Field(obj, "content", "description", "message"),
                Category = Field(obj, "category", "ticketCategory")
            }
        };
    }
}

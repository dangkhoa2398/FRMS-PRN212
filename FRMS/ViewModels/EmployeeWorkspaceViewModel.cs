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
    public string ReferenceId { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string StorageUnitId { get; init; } = "";
    public string WorkType { get; init; } = "";
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
        TaskDateChangedCommand = new AsyncRelayCommand(TaskDateChangedAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        CheckInCommand = new AsyncRelayCommand<EmployeeWorkItem>(CheckInAsync);
        CheckOutCommand = new AsyncRelayCommand<EmployeeWorkItem>(CheckOutAsync);
        CompleteTicketCommand = new AsyncRelayCommand<EmployeeWorkItem>(CompleteTicketAsync);
        AssignTicketCommand = new AsyncRelayCommand<EmployeeWorkItem>(AssignTicketAsync);
        CreateStorageUnitCommand = new AsyncRelayCommand(CreateStorageUnitAsync);
        ChangeStorageStatusCommand = new AsyncRelayCommand<EmployeeWorkItem>(ChangeStorageStatusAsync);
        AssignHandoverUnitCommand = new AsyncRelayCommand<EmployeeWorkItem>(AssignHandoverUnitAsync);
        CompleteHandoverCommand = new AsyncRelayCommand<EmployeeWorkItem>(CompleteHandoverAsync);
        ConfirmReturnCommand = new AsyncRelayCommand<EmployeeWorkItem>(ConfirmReturnAsync);
    }

    private string _role = "";
    public string Role { get => _role; private set => SetProperty(ref _role, value); }
    public bool IsManager => Role == "FACILITY_MANAGER";
    public bool IsStaff => Role == "FACILITY_STAFF";
    private string _displayName = "Employee";
    public string DisplayName { get => _displayName; private set => SetProperty(ref _displayName, value); }
    private string _email = "";
    public string Email { get => _email; private set => SetProperty(ref _email, value); }
    private string _phone = "";
    public string Phone { get => _phone; private set => SetProperty(ref _phone, value); }
    public bool IsHome => Route == "home";
    public bool HasRecords => Route != "home";
    private string _facilityName = "";
    public string FacilityName { get => _facilityName; private set => SetProperty(ref _facilityName, value); }
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
    public bool ShowFilters => Route != "home";
    public bool ShowDateFilter => Route is "access" or "task";
    private DateTime _taskDate = DateTime.Today;
    public DateTime TaskDate { get => _taskDate; set => SetProperty(ref _taskDate, value); }
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
        // Re-select All after rebuilding ComboBox items. WPF may clear SelectedItem
        // when its previous item is removed, which otherwise hides every result.
        SelectedStatus = "All";
        SelectedCategory = "All";
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var result = _allItems.Where(x =>
            (string.IsNullOrWhiteSpace(SelectedStatus) || SelectedStatus == "All" || x.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SelectedCategory) || SelectedCategory == "All" || x.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(SearchText) || x.SearchText.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (Route == "task" || !ShowDateFilter || !SelectedDate.HasValue ||
             (DateTime.TryParse(x.DateValue, out var date) && date.Date == SelectedDate.Value.Date)));
        Items.Clear();
        foreach (var item in result) Items.Add(item);
    }

    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand TaskDateChangedCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> CheckInCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> CheckOutCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> CompleteTicketCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> AssignTicketCommand { get; }
    public IAsyncRelayCommand CreateStorageUnitCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> ChangeStorageStatusCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> AssignHandoverUnitCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> CompleteHandoverCommand { get; }
    public IAsyncRelayCommand<EmployeeWorkItem> ConfirmReturnCommand { get; }
    private string _actionInput = "";
    public string ActionInput { get => _actionInput; set => SetProperty(ref _actionInput, value); }
    private string _actionNote = "";
    public string ActionNote { get => _actionNote; set => SetProperty(ref _actionNote, value); }
    private string _newUnitTypeId = "";
    public string NewUnitTypeId { get => _newUnitTypeId; set => SetProperty(ref _newUnitTypeId, value); }
    private string _newUnitCode = "";
    public string NewUnitCode { get => _newUnitCode; set => SetProperty(ref _newUnitCode, value); }
    private string _newUnitLocation = "";
    public string NewUnitLocation { get => _newUnitLocation; set => SetProperty(ref _newUnitLocation, value); }
    private string _storageStatus = "AVAILABLE";
    public string StorageStatus { get => _storageStatus; set => SetProperty(ref _storageStatus, value); }
    public string[] StorageStatuses { get; } = { "AVAILABLE", "MAINTENANCE" };

    private async Task ReadFacilityNameAsync()
    {
        FacilityName = "";
        if (!Guid.TryParse(FacilityId, out _)) return;
        try
        {
            using var doc = await _api.GetAsync($"api/v1/facilities/{FacilityId}");
            var facility = JsonField(doc.RootElement, "data");
            FacilityName = Field(facility, "name", "facilityName");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Employee profile remains available if public facility information cannot be read.
        }
    }

    private Task TaskDateChangedAsync() => Route == "task" ? NavigateAsync("task") : Task.CompletedTask;

    public async Task InitializeAsync(string role)
    {
        _loading?.Cancel();
        Role = role;
        OnPropertyChanged(nameof(IsManager));
        OnPropertyChanged(nameof(IsStaff));
        try
        {
            using var doc = await _api.GetAsync("api/v1/auth/me");
            var data = JsonField(doc.RootElement, "data");
            var profile = JsonField(data, "profile");
            DisplayName = Field(profile, "fullName") is var name && !string.IsNullOrWhiteSpace(name) ? name : "Employee";
            FacilityId = Field(profile, "facilityId");
            Email = Field(data, "email");
            Phone = Field(data, "phoneNumber");
            await ReadFacilityNameAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Message = "Employee profile could not be loaded: " + ex.Message;
        }
        await NavigateAsync("home");
    }

    public void Reset()
    {
        _loading?.Cancel();
        Items.Clear();
        _allItems.Clear();
        DisplayName = "Employee";
        FacilityId = "";
        FacilityName = "";
        Email = "";
        Phone = "";
        Role = "";
        OnPropertyChanged(nameof(IsManager));
        Message = "";
    }

    private async Task RefreshAsync()
    {
        if (Route == "home")
        {
            try
            {
                using var doc = await _api.GetAsync("api/v1/auth/me");
                var data = JsonField(doc.RootElement, "data");
                var profile = JsonField(data, "profile");
                DisplayName = Field(profile, "fullName") is var name && !string.IsNullOrWhiteSpace(name) ? name : "Employee";
                FacilityId = Field(profile, "facilityId");
                Email = Field(data, "email");
                Phone = Field(data, "phoneNumber");
                await ReadFacilityNameAsync();
                Message = "Profile refreshed.";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Message = "Could not refresh profile: " + ex.Message;
            }
            return;
        }
        await NavigateAsync(Route);
    }

    private async Task NavigateAsync(string? route)
    {
        _loading?.Cancel();
        var cts = new CancellationTokenSource();
        _loading = cts;
        Route = route is "booking" ? "task" : route ?? "home";
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(ShowDateFilter));
        _allItems.Clear();
        Items.Clear();
        SearchText = "";
        SelectedStatus = "All";
        SelectedCategory = "All";
        SelectedDate = Route == "task" ? TaskDate : null;
        (string title, string description, string? path) = Route switch
        {
            "home" => ("Home", "Your employee profile and assigned facility", null),
            "reservations" => ("Reservations", "Customer bookings at your assigned facility", "api/v1/reservations?page=1&pageSize=20"),
            "task" => ("Task", "Daily work: reservations, arrivals, access and handovers", IsManager
                ? "api/v1/reservations?page=1&pageSize=20"
                : $"api/v1/staff/work-items?date={TaskDate:yyyy-MM-dd}&page=1&pageSize=20"),
            "storage" => IsManager
                ? ("Storage Unit Control", "Physical storage units at your facility: code, location and operational status", Guid.TryParse(FacilityId, out _) ? $"api/v1/facilities/{FacilityId}/storage-units?page=1&pageSize=20" : null)
                : ("Customer Contracts", "Customer rental contracts and unit assignments", "api/v1/contracts?page=1&pageSize=20"),
            "access" => IsManager
                ? ("Visit Oversight", "Facility visits and customer access schedules", "api/v1/visits?page=1&pageSize=20")
                : ("Customer Check-in / Out", "Daily customer access and handover work", $"api/v1/staff/work-items?date={TaskDate:yyyy-MM-dd}&page=1&pageSize=20"),
            "support" => IsManager
                ? ("Ticket Assignment", "Review and coordinate customer support requests", "api/v1/support-tickets?page=1&pageSize=20")
                : ("Resolve Tickets", "Follow up on customer tickets assigned to staff", "api/v1/support-tickets?page=1&pageSize=20"),
            _ => ("Task", "Daily work", IsManager ? "api/v1/reservations?page=1&pageSize=20" : $"api/v1/staff/work-items?date={TaskDate:yyyy-MM-dd}&page=1&pageSize=20")
        };
        Title = title;
        Description = description;
        Message = "Loading...";
        if (Route == "home")
        {
            Message = "Employee details are loaded from the authenticated profile.";
            if (ReferenceEquals(_loading, cts)) _loading = null;
            cts.Dispose();
            return;
        }
        if (path is null) { Message = "Manager facility ID is missing from the authenticated profile; storage units cannot be loaded."; _loading = null; cts.Dispose(); return; }
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
                else
                {
                    nested = JsonField(data, "data");
                    if (nested.ValueKind == JsonValueKind.Array) data = nested;
                }
            }
            var records = new List<EmployeeWorkItem>();
            if (data.ValueKind == JsonValueKind.Array)
                foreach (var element in data.EnumerateArray()) records.Add(Map(element));
            else if (data.ValueKind == JsonValueKind.Object) records.Add(Map(data));
            UpdateFilters(records);
            Message = records.Count == 0
                ? (Route == "task" ? $"No tasks found for {TaskDate:dd/MM/yyyy}." : "No records found.")
                : $"{Items.Count} of {records.Count} record(s) shown.";
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

    private Task CheckInAsync(EmployeeWorkItem? item) => SendVisitCommandAsync(item, "check-in");
    private Task CheckOutAsync(EmployeeWorkItem? item) => SendVisitCommandAsync(item, "check-out");
    private async Task SendVisitCommandAsync(EmployeeWorkItem? item, string command)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var visitId)) { Message = "Visit ID is missing."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/visits/" + visitId.ToString("D") + "/" + command); Message = "Visit operation completed."; await NavigateAsync(Route); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Visit operation failed: " + ex.Message; }
    }
    private async Task CompleteTicketAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var ticketId)) { Message = "Ticket ID is missing."; return; }
        if (string.IsNullOrWhiteSpace(ActionNote) || ActionNote.Trim().Length > 1000) { Message = "Enter a completion note (1–1000 characters)."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/support-tickets/" + ticketId.ToString("D") + "/complete", new { resultNote = ActionNote.Trim() }); ActionNote = ""; Message = "Support ticket completed."; await NavigateAsync("support"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Ticket could not be completed: " + ex.Message; }
    }
    private async Task AssignTicketAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var ticketId) || !Guid.TryParse(ActionInput.Trim(), out var employeeId)) { Message = "Enter a valid Facility Staff Employee ID."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/support-tickets/" + ticketId.ToString("D") + "/assign", new { employeeId }); ActionInput = ""; Message = "Support ticket assigned."; await NavigateAsync("support"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Ticket assignment failed: " + ex.Message; }
    }
    private async Task CreateStorageUnitAsync()
    {
        if (!Guid.TryParse(FacilityId, out var facilityId) || !Guid.TryParse(NewUnitTypeId.Trim(), out var unitTypeId) || string.IsNullOrWhiteSpace(NewUnitCode)) { Message = "Enter a valid Unit Type ID and Unit Code. Manager facility must be loaded."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/facilities/" + facilityId.ToString("D") + "/storage-units", new { unitTypeId, unitCode = NewUnitCode.Trim(), locationInfo = NewUnitLocation.Trim() }); NewUnitCode = ""; NewUnitLocation = ""; Message = "Storage unit created."; await NavigateAsync("storage"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Storage unit was not created: " + ex.Message; }
    }
    private async Task ChangeStorageStatusAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.StorageUnitId, out var unitId)) { Message = "Storage Unit ID is missing."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/storage-units/" + unitId.ToString("D") + "/status", new { status = StorageStatus }); Message = "Storage unit status updated."; await NavigateAsync("storage"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Storage status was not changed: " + ex.Message; }
    }
    private async Task AssignHandoverUnitAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var reservationId) || !Guid.TryParse(ActionInput.Trim(), out var storageUnitId)) { Message = "Enter a valid Storage Unit ID for this reservation."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Put, "api/v1/reservations/" + reservationId.ToString("D") + "/handover-unit-assignment", new { storageUnitId }); ActionInput = ""; Message = "Handover unit assignment saved."; await NavigateAsync("reservations"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Unit assignment failed: " + ex.Message; }
    }
    private async Task CompleteHandoverAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var reservationId) || !Guid.TryParse(item.EntityId, out var visitId) || !Guid.TryParse(ActionInput.Trim(), out var storageUnitId)) { Message = "This work item needs Reservation ID, Visit ID and a valid Storage Unit ID."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/reservations/" + reservationId.ToString("D") + "/complete-handover", new { visitId, storageUnitId, discountId = (Guid?)null }); ActionInput = ""; Message = "Handover completed and contract created."; await NavigateAsync("task"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Handover could not be completed: " + ex.Message; }
    }
    private async Task ConfirmReturnAsync(EmployeeWorkItem? item)
    {
        if (item is null || !Guid.TryParse(item.ReferenceId, out var visitId) || !DateOnly.TryParseExact(ActionInput.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var actualReturnDate)) { Message = "Enter actual return date as yyyy-MM-dd."; return; }
        try { using var response = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/visits/" + visitId.ToString("D") + "/confirm-return", new { actualReturnDate = actualReturnDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) }); ActionInput = ""; Message = "Actual return recorded; inspection has been created."; await NavigateAsync("task"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Message = "Return confirmation failed: " + ex.Message; }
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
            "task" or "reservations" => new EmployeeWorkItem
            {
                Title = !string.IsNullOrWhiteSpace(Field(obj, "unitTypeName"))
                    ? Field(obj, "unitTypeName")
                    : !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Customer reservation",
                Subtitle = !string.IsNullOrEmpty(customerName) ? customerName : Field(obj, "facilityName"),
                Status = status,
                Detail1 = Route == "task"
                    ? "Scheduled: " + Field(obj, "scheduledDate", "startMonth")
                    : "Rental start: " + Field(obj, "startMonth"),
                Detail2 = Route == "task"
                    ? "Customer phone: " + Field(customer, "phoneNumber")
                    : "Rental end: " + Field(obj, "endMonth"),
                Description = Route == "task"
                    ? "Work: " + Field(obj, "workType", "resultNote")
                    : "Unit type: " + Field(obj, "unitTypeMode", "unitTypeName"),
                Category = Route == "task"
                    ? Field(obj, "workType") : Field(obj, "unitTypeMode", "unitTypeName"),
                DateValue = Field(obj, "scheduledDate"),
                ReferenceId = Field(obj, "referenceId", "reservationId"),
                EntityId = Field(obj, "entityId"),
                WorkType = Field(obj, "workType")
            },
            "storage" when IsManager => new EmployeeWorkItem
            {
                Title = Field(obj, "unitCode") is var codeName && !string.IsNullOrWhiteSpace(codeName)
                    ? codeName : "Storage unit",
                Subtitle = "Location: " + (Field(obj, "locationInfo") is var location && !string.IsNullOrWhiteSpace(location) ? location : "Not specified"),
                Status = status,
                Detail1 = "Unit type ID: " + Field(obj, "unitTypeId"),
                Detail2 = "Storage unit ID: " + Field(obj, "storageUnitId"),
                StorageUnitId = Field(obj, "storageUnitId"),
                Description = "Facility: " + (string.IsNullOrWhiteSpace(FacilityName) ? Field(obj, "facilityId") : FacilityName),
                Category = Field(obj, "unitTypeId")
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
                Description = "Contract: " + Field(obj, "contractId"),
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
                DateValue = Field(obj, "scheduledDate", "visitDate"),
                ReferenceId = Field(obj, "referenceId", "visitId"),
                EntityId = Field(obj, "entityId"),
                WorkType = Field(obj, "workType", "visitType")
            },
            _ => new EmployeeWorkItem
            {
                Title = !string.IsNullOrEmpty(code) ? code.Replace('_', ' ') : "Support request",
                Subtitle = !string.IsNullOrEmpty(subject) ? subject : "Customer support",
                Status = status,
                Detail1 = "Assignee: " + (Field(obj, "assignedEmployeeName", "employeeName") is var employee && !string.IsNullOrWhiteSpace(employee) ? employee : "Unassigned"),
                Detail2 = "Category: " + Field(obj, "category", "ticketCategory"),
                Description = Field(obj, "description", "content", "message"),
                Category = Field(obj, "category", "ticketCategory"),
                ReferenceId = Field(obj, "supportTicketId", "referenceId"),
                EntityId = Field(obj, "contractId")
            }
        };
    }
}

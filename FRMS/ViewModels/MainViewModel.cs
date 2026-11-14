using System;
using System.Collections.Generic;
using System.Globalization;
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
    private string _pendingContractId = "";

    public MainViewModel(CustomerApiService api, IFrmsHealthService health)
    {
        _api = api;
        _health = health;
        EmployeeWorkspace = new EmployeeWorkspaceViewModel(api);
        LoginCommand = new AsyncRelayCommand<PasswordBox>(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        FindStorageCommand = new AsyncRelayCommand(FindStorageAsync);
        OpenContractCommand = new AsyncRelayCommand<CustomerRecord>(OpenContractAsync);
        CreateVisitCommand = new AsyncRelayCommand<CustomerRecord>(CreateVisitAsync);
        CreateReservationCommand = new AsyncRelayCommand(CreateReservationAsync);
        LoadUnitTypesCommand = new AsyncRelayCommand(LoadUnitTypesAsync);
        CreateSupportTicketCommand = new AsyncRelayCommand(CreateSupportTicketAsync);
        CheckConnectionCommand = new AsyncRelayCommand(CheckConnectionAsync);
    }

    public EmployeeWorkspaceViewModel EmployeeWorkspace { get; }

    private string _loginMode = "Customer";
    public string LoginMode { get => _loginMode; set => SetProperty(ref _loginMode, value); }
    public string[] LoginModes { get; } = { "Customer", "Staff / Manager" };
    private string _employeeEmail = "";
    public string EmployeeEmail { get => _employeeEmail; set => SetProperty(ref _employeeEmail, value); }
    private string _employeeRole = "";
    public string EmployeeRole { get => _employeeRole; private set => SetProperty(ref _employeeRole, value); }
    public bool IsEmployee => !string.IsNullOrWhiteSpace(EmployeeRole);

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
    private string _customerName = "Customer";
    public string CustomerName { get => _customerName; set => SetProperty(ref _customerName, value); }
    private CustomerPageViewModel _currentPage = new("home", "Home", "Customer account overview.");
    public CustomerPageViewModel CurrentPage { get => _currentPage; set => SetProperty(ref _currentPage, value); }

    public IAsyncRelayCommand<PasswordBox> LoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IAsyncRelayCommand<string> NavigateCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand FindStorageCommand { get; }
    public IAsyncRelayCommand<CustomerRecord> OpenContractCommand { get; }
    public IAsyncRelayCommand<CustomerRecord> CreateVisitCommand { get; }
    public IAsyncRelayCommand CreateReservationCommand { get; }
    public IAsyncRelayCommand LoadUnitTypesCommand { get; }
    public IAsyncRelayCommand CreateSupportTicketCommand { get; }
    public IAsyncRelayCommand CheckConnectionCommand { get; }

    private async Task LoginAsync(PasswordBox? passwordBox)
    {
        if (IsLoggingIn || passwordBox is null) return;
        LoginError = "";
        var isEmployeeLogin = LoginMode == "Staff / Manager";
        if (string.IsNullOrWhiteSpace(isEmployeeLogin ? EmployeeEmail : PhoneNumber) || passwordBox.Password.Length == 0)
        {
            LoginError = isEmployeeLogin ? "Enter your employee email and password." : "Enter your phone number and password.";
            return;
        }
        IsLoggingIn = true;
        try
        {
            if (isEmployeeLogin)
            {
                var (success, error, role) = await _api.LoginEmployeeAsync(EmployeeEmail.Trim(), passwordBox.Password);
                if (!success) { LoginError = error; return; }
                EmployeeRole = role;
                OnPropertyChanged(nameof(IsEmployee));
                passwordBox.Clear();
                IsAuthenticated = true;
                await EmployeeWorkspace.InitializeAsync(role);
            }
            else
            {
                var (success, error) = await _api.LoginAsync(PhoneNumber.Trim(), passwordBox.Password);
                if (!success) { LoginError = error; return; }
                passwordBox.Clear();
                EmployeeRole = "";
                OnPropertyChanged(nameof(IsEmployee));
                IsAuthenticated = true;
                await NavigateAsync("home");
            }
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
        EmployeeWorkspace.Reset();
        EmployeeRole = "";
        OnPropertyChanged(nameof(IsEmployee));
        EmployeeEmail = "";
        PhoneNumber = "";
        CustomerName = "Customer";
        _pendingContractId = "";
        LoginError = "";
        IsAuthenticated = false;
        CurrentPage = new CustomerPageViewModel("home", "Home", "Customer account overview.");
    }

    private Task NavigateAsync(string? route) => LoadPageAsync(route ?? "home", false);

    private async Task FindStorageAsync()
    {
        if (!IsAuthenticated || !CurrentPage.IsStorage) return;
        var page = CurrentPage;
        if (page.StartMonth.HasValue != page.EndMonth.HasValue)
        {
            page.Message = "Select both check-in and check-out months, or leave both blank.";
            return;
        }
        if (page.StartMonth.HasValue)
        {
            var start = new DateTime(page.StartMonth.Value.Year, page.StartMonth.Value.Month, 1);
            var end = new DateTime(page.EndMonth!.Value.Year, page.EndMonth.Value.Month, 1);
            var nextMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
            if (start < nextMonth || end < start)
            {
                page.Message = "Check-in must be after this month and check-out cannot precede check-in.";
                return;
            }
        }
        var values = new List<string> { "page=1", "pageSize=20" };
        if (!string.IsNullOrWhiteSpace(page.FacilityQuery))
            values.Add("location=" + Uri.EscapeDataString(page.FacilityQuery.Trim()));
        if (page.StorageMode is "PRIVATE" or "PUBLIC")
            values.Add("mode=" + Uri.EscapeDataString(page.StorageMode));
        if (!string.IsNullOrWhiteSpace(page.StorageSize))
            values.Add("size=" + Uri.EscapeDataString(page.StorageSize.Trim()));
        if (page.StartMonth.HasValue)
        {
            values.Add("startMonth=" + page.StartMonth.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            values.Add("endMonth=" + page.EndMonth!.Value.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
        await LoadPageAsync("storage", true, string.Join("&", values), page);
    }

    private async Task OpenContractAsync(CustomerRecord? record)
    {
        if (record is null || !record.HasContract || !IsAuthenticated) return;
        _pendingContractId = record.ContractId;
        await LoadPageAsync("contracts", false);
    }

    private async Task LoadUnitTypesAsync()
    {
        var page = CurrentPage;
        if (!Guid.TryParse(page.ReservationFacilityId.Trim(), out var facilityId)) { page.Message = "Enter a valid Facility ID first."; return; }
        page.IsLoading = true;
        try
        {
            using var json = await _api.GetAsync("api/v1/facilities/" + facilityId.ToString("D") + "/unit-types?page=1&pageSize=100");
            var data = Property(json.RootElement, "data");
            page.UnitTypes.Clear();
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = Field(item, "unitTypeId");
                    if (Guid.TryParse(id, out _))
                    {
                        var name = Field(item, "name");
                        var mode = Field(item, "mode");
                        var size = Field(item, "size");
                        var price = Money(Field(item, "rentalPrice"));
                        page.UnitTypes.Add(new CustomerUnitTypeOption { UnitTypeId = id, DisplayName = string.Join(" | ", new[] { name, mode, size, price }.Where(x => !string.IsNullOrWhiteSpace(x))) });
                    }
                }
            }
            page.Message = page.UnitTypes.Count == 0 ? "No Unit Types are available for this facility and selected period." : page.UnitTypes.Count + " Unit Type option(s) loaded.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { page.Message = "Could not load Unit Types: " + ex.Message; }
        finally { page.IsLoading = false; }
    }
    private async Task CreateReservationAsync()
    {
        var page = CurrentPage;
        if (!Guid.TryParse(page.ReservationFacilityId.Trim(), out var facilityId) ||
            !Guid.TryParse(page.ReservationUnitTypeId.Trim(), out var unitTypeId))
        {
            page.Message = "Enter valid Facility ID and Unit Type ID from the search results.";
            return;
        }
        if (!DateOnly.TryParseExact(page.ReservationStartMonth, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(page.ReservationEndMonth, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end) || end < start)
        {
            page.Message = "Enter valid rental months in yyyy-MM format; end month must not precede start month.";
            return;
        }
        var nextMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
        if (start < nextMonth) { page.Message = "Reservation must start after the current business month."; return; }
        page.IsLoading = true;
        page.Message = "Submitting reservation...";
        try
        {
            using var result = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/reservations",
                new { facilityId, unitTypeId, startMonth = page.ReservationStartMonth.Trim(), endMonth = page.ReservationEndMonth.Trim() });
            page.Message = "Reservation created. Check My Reservations for its deposit invoice and current status.";
            await LoadPageAsync("reservations", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            page.Message = "Reservation was not created: " + ex.Message;
            page.IsLoading = false;
        }
    }

    private async Task CreateSupportTicketAsync()
    {
        var page = CurrentPage;
        if (!Guid.TryParse(page.TicketContractId.Trim(), out var contractId))
        {
            page.Message = "Enter a valid Contract ID from My Contracts.";
            return;
        }
        if (string.IsNullOrWhiteSpace(page.TicketDescription) || page.TicketDescription.Trim().Length > 1000)
        {
            page.Message = "Describe the issue (1–1000 characters).";
            return;
        }
        page.IsLoading = true;
        page.Message = "Submitting support ticket...";
        try
        {
            using var result = await _api.SendJsonAsync(HttpMethod.Post, "api/v1/support-tickets",
                new { contractId, category = page.TicketCategory, description = page.TicketDescription.Trim() });
            page.TicketDescription = "";
            page.Message = "Support ticket created successfully.";
            await LoadPageAsync("support", false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            page.Message = "Support ticket was not created: " + ex.Message;
            page.IsLoading = false;
        }
    }

    private async Task CreateVisitAsync(CustomerRecord? record)
    {
        if (record is null || !record.CanCreateVisit || record.VisitDate is null || !IsAuthenticated) return;
        if (!Guid.TryParse(record.ContractId, out var contractId)) return;
        if (record.VisitDate.Value.Date < DateTime.Today)
        {
            CurrentPage.Message = "Please select today or a future visit date.";
            return;
        }
        var page = CurrentPage;
        page.Message = "Creating your access visit...";
        try
        {
            await _api.CreateAccessVisitAsync(contractId, record.VisitDate.Value.Date);
            if (ReferenceEquals(CurrentPage, page))
                page.Message = "Access visit created successfully.";
        }
        catch (HttpRequestException ex)
        {
            if (ReferenceEquals(CurrentPage, page))
                page.Message = $"Visit could not be created: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            if (ReferenceEquals(CurrentPage, page))
                page.Message = "Visit request timed out.";
        }
    }

    private async Task LoadPageAsync(string route, bool runStorageSearch, string? storageQuery = null, CustomerPageViewModel? existingPage = null)
    {
        if (!IsAuthenticated) return;
        _currentRoute = route;
        var (title, description, path) = route switch
        {
            "home" => ("Home", "Your account overview", "api/v1/auth/me"),
            "storage" => ("Find Storage", "Search facilities and available storage space", "api/v1/facilities?page=1&pageSize=20"),
            "reservations" => ("My Reservations", "Your booked storage types and rental dates", "api/v1/reservations?page=1&pageSize=20"),
            "contracts" => ("My Contracts", "Your rental units and visits", "api/v1/contracts?page=1&pageSize=20"),
            "invoices" => ("Invoices", "Billing and payment information", "api/v1/invoices?page=1&pageSize=20"),
            "support" => ("Support Requests", "Your requests and assigned support", "api/v1/support-tickets?page=1&pageSize=20"),
            _ => ("Home", "Your account overview", "api/v1/auth/me")
        };

        _navigationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _navigationCts = cts;
        var page = existingPage ?? new CustomerPageViewModel(route, title, description);
        if (route == "contracts" && !string.IsNullOrEmpty(_pendingContractId))
        {
            page.ContractFilter = _pendingContractId;
            // Fetch the exact related contract; it may be outside the first list page.
            path = $"api/v1/contracts/{_pendingContractId}";
            _pendingContractId = "";
        }
        if (route == "storage" && runStorageSearch)
        {
            path = "api/v1/facilities?" + (storageQuery ?? "page=1&pageSize=20");
            page.HasSearched = true;
            page.Search = "";
            page.SetRecords(Array.Empty<CustomerRecord>());
        }
        CurrentPage = page;
        if (route == "storage" && !runStorageSearch)
        {
            page.Message = "Enter a facility or location, then click Find Storage.";
            if (ReferenceEquals(_navigationCts, cts)) _navigationCts = null;
            cts.Dispose();
            return;
        }

        page.IsLoading = true;
        try
        {
            using var json = await _api.GetAsync(path, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(CurrentPage, page) || !IsAuthenticated) return;
            var root = json.RootElement;
            var data = Property(root, "data");
            if (data.ValueKind == JsonValueKind.Undefined) data = root;
            var items = data;
            if (data.ValueKind == JsonValueKind.Object)
            {
                var nested = Property(data, "items", "data");
                if (nested.ValueKind != JsonValueKind.Undefined) items = nested;
            }

            var records = new List<CustomerRecord>();
            if (items.ValueKind == JsonValueKind.Array)
                foreach (var item in items.EnumerateArray()) records.Add(ToRecord(item, route));
            else if (items.ValueKind == JsonValueKind.Object)
                records.Add(ToRecord(items, route));

            if (route == "home")
            {
                var profile = Property(data, "profile");
                var name = Field(profile, "fullName", "name");
                if (!string.IsNullOrWhiteSpace(name)) CustomerName = name;
            }
            if (route == "reservations" || route == "invoices")
                await EnrichStorageAsync(records, route, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(CurrentPage, page) || !IsAuthenticated) return;
            page.SetRecords(records);
            if (route == "contracts" && !string.IsNullOrEmpty(page.ContractFilter))
                page.Message = page.VisibleRecords.Count == 0
                    ? "The related contract could not be found."
                    : "Showing the contract related to your support request.";
            else
                page.Message = records.Count == 0 ? "No matching records." : $"{page.VisibleRecords.Count} result(s) available.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (TaskCanceledException)
        {
            if (ReferenceEquals(CurrentPage, page)) page.Message = "The backend request timed out. Please retry.";
        }
        catch (HttpRequestException ex)
        {
            if (ReferenceEquals(CurrentPage, page)) page.Message = $"Could not load data: {ex.Message}";
        }
        catch (JsonException)
        {
            if (ReferenceEquals(CurrentPage, page)) page.Message = "The backend returned invalid JSON.";
        }
        finally
        {
            if (ReferenceEquals(CurrentPage, page)) page.IsLoading = false;
            if (ReferenceEquals(_navigationCts, cts)) _navigationCts = null;
            cts.Dispose();
        }
    }

    private static JsonElement Property(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return default;
        foreach (var name in names)
            if (obj.TryGetProperty(name, out var value)) return value;
        return default;
    }

    private static string Field(JsonElement obj, params string[] names)
    {
        var value = Property(obj, names);
        return value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object
            or JsonValueKind.Array ? "" : value.ToString();
    }

    private static string DateLabel(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
        return value.Length == 7 && value[4] == '-' ? value[5..] + "/" + value[..4] : value;
    }

    private static string Money(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)) return "";
        return amount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " ₫";
    }

    private static string ShortId(string value) =>
        Guid.TryParse(value, out _) ? value[..8].ToUpperInvariant() : value;

    private static CustomerRecord ToRecord(JsonElement element, string route)
    {
        if (element.ValueKind != JsonValueKind.Object) return new CustomerRecord { Title = element.ToString() };
        var profile = Property(element, "profile");
        string fullName = Field(profile, "fullName", "name");
        string facility = Field(element, "facilityName", "facilityCode");
        if (string.IsNullOrWhiteSpace(facility))
        {
            var facilityId = Field(element, "facilityId");
            if (!string.IsNullOrEmpty(facilityId)) facility = "Facility " + ShortId(facilityId);
        }
        string unitCode = Field(element, "unitCode", "storageUnitCode");
        string contractId = Field(element, "contractId");
        string status = Field(element, "status", "paymentStatus");
        return route switch
        {
            "home" => new CustomerRecord
            {
                Title = !string.IsNullOrWhiteSpace(fullName) ? fullName : "Customer profile",
                Subtitle = Field(element, "email"),
                Status = status,
                Detail1 = "Phone: " + Field(element, "phoneNumber"),
                Detail2 = "Email: " + Field(element, "email"),
                Detail3 = "Role: Customer"
            },
            "storage" => new CustomerRecord
            {
                Title = Field(element, "name", "facilityName"),
                Subtitle = Field(element, "address", "location"),
                Status = status,
                Detail1 = Field(element, "description"),
                Detail2 = "Contact: " + Field(element, "contactInfo"),
                Detail3 = "Facility ID: " + Field(element, "facilityId")
            },
            "contracts" => new CustomerRecord
            {
                Title = string.IsNullOrWhiteSpace(unitCode) ? "Rental contract" : "Unit " + unitCode,
                Subtitle = "Contract: " + ShortId(contractId),
                Status = status,
                Detail1 = "Storage ID: " + ShortId(Field(element, "storageUnitId")),
                Detail2 = "Storage type: " + Field(element, "unitTypeName", "unitTypeMode"),
                Detail3 = "Start: " + DateLabel(Field(element, "startMonth")),
                Detail4 = "End: " + DateLabel(Field(element, "endMonth")) + " | Contract ID: " + contractId,
                ContractId = contractId
            },
            "invoices" => new CustomerRecord
            {
                Title = Field(element, "invoiceType", "type"),
                Subtitle = "Due: " + DateLabel(Field(element, "dueDate", "billingMonth", "createdAt")),
                Status = status,
                Amount = Money(Field(element, "amountDue", "totalAmount", "amount")),
                Detail1 = Field(element, "invoiceType", "type").Equals("DEPOSIT", StringComparison.OrdinalIgnoreCase)
                    ? "" : "Billing period: " + DateLabel(Field(element, "billingMonth")),
                Detail2 = "Payment: " + (!string.IsNullOrWhiteSpace(Field(element, "paidAt")) ? DateLabel(Field(element, "paidAt")) : status),
                RelatedEntityId = Field(element, "entityId"),
                RelatedStorage = Field(element, "unitCode", "unitTypeName", "facilityName")
            },
            "support" => new CustomerRecord
            {
                Title = Field(element, "category", "ticketCategory", "subject"),
                Subtitle = "Assigned: " + (Field(element, "employeeName", "assignedEmployeeName") is var emp && !string.IsNullOrWhiteSpace(emp) ? emp : "Not assigned"),
                Status = status,
                Content = Field(element, "content", "description", "message", "details"),
                ContractId = contractId
            },
            "reservations" => new CustomerRecord
            {
                Title = Field(element, "unitTypeName", "unitTypeMode") is var type && !string.IsNullOrWhiteSpace(type) ? type : "Booked storage type",
                Subtitle = "Reservation: " + ShortId(Field(element, "reservationId")),
                Status = status,
                Detail1 = "Unit Type ID: " + ShortId(Field(element, "unitTypeId")),
                Detail2 = "Period: " + DateLabel(Field(element, "startMonth")) + " – " + DateLabel(Field(element, "endMonth")),
                Detail3 = "Deposit: " + Money(Field(element, "depositAmount", "lockedDepositAmount")),
                UnitType = Field(element, "unitTypeName", "unitTypeMode") is var typeName && !string.IsNullOrWhiteSpace(typeName) ? typeName : "Type " + ShortId(Field(element, "unitTypeId")),
                UnitTypeId = Field(element, "unitTypeId")
            },
            _ => new CustomerRecord { Title = "Record", Status = status }
        };
    }

    private async Task EnrichStorageAsync(List<CustomerRecord> records, string route, CancellationToken token)
    {
        // The list DTOs can omit the storage name. Resolve only associated public/owned resources.
        var typeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            if (route == "reservations")
            {
                if (!Guid.TryParse(record.UnitTypeId, out _)) continue;
                if (!typeNames.TryGetValue(record.UnitTypeId, out var name))
                {
                    try
                    {
                        using var typeDoc = await _api.GetAsync("api/v1/unit-types/" + record.UnitTypeId, token);
                        var typeData = Property(typeDoc.RootElement, "data");
                        name = Field(typeData, "name", "unitTypeName");
                        if (string.IsNullOrWhiteSpace(name)) name = record.UnitType;
                        typeNames[record.UnitTypeId] = name;
                    }
                    catch (HttpRequestException) { name = record.UnitType; }
                    catch (JsonException) { name = record.UnitType; }
                }
                record.RelatedStorage = name;
                record.UnitType = name;
                record.Title = name;
            }
            else if (route == "invoices" && Guid.TryParse(record.RelatedEntityId, out _))
            {
                try
                {
                    string resource = record.Title.Equals("DEPOSIT", StringComparison.OrdinalIgnoreCase)
                        ? "reservations" : "contracts";
                    using var detail = await _api.GetAsync($"api/v1/{resource}/{record.RelatedEntityId}", token);
                    var data = Property(detail.RootElement, "data");
                    if (resource == "contracts")
                    {
                        var unit = Field(data, "unitCode", "storageUnitCode");
                        if (!string.IsNullOrWhiteSpace(unit))
                            record.RelatedStorage = "Unit " + unit;
                        else
                        {
                            unit = Field(data, "storageUnitId");
                            if (!string.IsNullOrWhiteSpace(unit)) record.RelatedStorage = "Storage ID: " + ShortId(unit);
                        }
                    }
                    else
                    {
                        var typeName = Field(data, "unitTypeName", "unitTypeMode");
                        var typeId = Field(data, "unitTypeId");
                        if (string.IsNullOrWhiteSpace(typeName) && Guid.TryParse(typeId, out _))
                        {
                            if (!typeNames.TryGetValue(typeId, out typeName))
                            {
                                using var typeDoc = await _api.GetAsync("api/v1/unit-types/" + typeId, token);
                                typeName = Field(Property(typeDoc.RootElement, "data"), "name", "unitTypeName");
                                typeNames[typeId] = typeName;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(typeName)) record.RelatedStorage = "Storage type: " + typeName;
                        else if (!string.IsNullOrWhiteSpace(typeId)) record.RelatedStorage = "Unit Type ID: " + ShortId(typeId);
                    }
                }
                catch (HttpRequestException) { /* Keep base invoice data when detail is unavailable. */ }
                catch (JsonException) { /* Malformed optional detail does not hide the invoice. */ }
            }
        }
    }

    private async Task RefreshAsync()
    {
        if (_currentRoute == "storage")
        {
            if (CurrentPage.HasSearched) await FindStorageAsync();
            return;
        }
        await LoadPageAsync(_currentRoute, false);
    }

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

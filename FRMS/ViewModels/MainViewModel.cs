using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.Theming;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly CustomerApiService _api;
    private readonly IFrmsHealthService _health;
    private CancellationTokenSource? _navigationCts;
    private string _currentRoute = "home";
    private string _pendingContractId = "";
    private readonly Dictionary<Guid, PaymentQrViewModel> _paymentSessions = new();
    private CancellationTokenSource? _paymentDialogLifetime;

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
        SelectFacilityCommand = new AsyncRelayCommand<CustomerRecord>(SelectFacilityAsync);
        CreateSupportTicketCommand = new AsyncRelayCommand(CreateSupportTicketAsync);
        PayInvoiceCommand = new AsyncRelayCommand<CustomerRecord>(PayInvoiceAsync);
        CheckConnectionCommand = new AsyncRelayCommand(CheckConnectionAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
    }

    public EmployeeWorkspaceViewModel EmployeeWorkspace { get; }

    public string ThemeToggleText => ThemeManager.IsDark ? "Light mode" : "Dark mode";
    public bool IsDarkTheme => ThemeManager.IsDark;
    public IRelayCommand ToggleThemeCommand { get; }

    private void ToggleTheme()
    {
        ThemeManager.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

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
    public IAsyncRelayCommand<CustomerRecord> SelectFacilityCommand { get; }
    public IAsyncRelayCommand CreateSupportTicketCommand { get; }
    public IAsyncRelayCommand CheckConnectionCommand { get; }
    public IAsyncRelayCommand<CustomerRecord> PayInvoiceCommand { get; }
    public event Action<PaymentQrViewModel, CancellationToken>? PaymentRequested;

    private async Task PayInvoiceAsync(CustomerRecord? record)
    {
        if (!IsAuthenticated || IsEmployee || !CurrentPage.IsInvoices || record?.CanPayInvoice != true
            || PaymentRequested is null)
            return;

        if (!_paymentSessions.TryGetValue(record.InvoiceId, out var payment))
        {
            payment = new PaymentQrViewModel(_api, record.InvoiceId);
            _paymentSessions.Add(record.InvoiceId, payment);
        }

        using var lifetime = new CancellationTokenSource();
        _paymentDialogLifetime = lifetime;
        try { PaymentRequested.Invoke(payment, lifetime.Token); }
        finally { _paymentDialogLifetime = null; }
        if ((payment.Succeeded || payment.InvoiceNeedsRefresh) && IsAuthenticated && CurrentPage.IsInvoices)
            await RefreshAsync();
    }

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
                await EmployeeWorkspace.InitializeAsync(role);
                IsAuthenticated = true;
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
        _paymentDialogLifetime?.Cancel();
        _paymentSessions.Clear();
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
        if (!Guid.TryParse(page.SelectedFacilityId, out var facilityId))
        {
            page.Message = "Please choose a Facility before searching for storage.";
            return;
        }
        // Query the selected Facility directly. Apply size, mode and period only at
        // its Unit Type catalog to avoid filtering out the Facility prematurely.
        await LoadPageAsync("storage", true, facilityId.ToString("D"), page);
    }

    private async Task OpenContractAsync(CustomerRecord? record)
    {
        if (record is null || !record.HasContract || !IsAuthenticated) return;
        _pendingContractId = record.ContractId;
        await LoadPageAsync("contracts", false);
    }

    private async Task SelectFacilityAsync(CustomerRecord? facility)
    {
        if (facility is null || !Guid.TryParse(facility.FacilityId, out _)) return;
        var page = CurrentPage;
        page.ReservationFacilityId = facility.FacilityId;
        page.ReservationFacilityName = string.IsNullOrWhiteSpace(facility.FacilityName) ? facility.Title : facility.FacilityName;
        page.SelectedUnitTypeId = "";
        page.UnitTypes.Clear();
        await LoadUnitTypesAsync(page);
        if (!string.IsNullOrWhiteSpace(facility.UnitTypeId) && page.UnitTypes.Any(x => x.UnitTypeId == facility.UnitTypeId))
            page.SelectedUnitTypeId = facility.UnitTypeId;
    }

    private async Task LoadUnitTypesAsync(CustomerPageViewModel page)
    {
        if (!Guid.TryParse(page.ReservationFacilityId, out var facilityId))
        {
            page.Message = "Select a storage type from the results first.";
            return;
        }
        if (!DateOnly.TryParseExact(page.ReservationStartMonth, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(page.ReservationEndMonth, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end < start)
        {
            page.Message = "Select a valid rental period before choosing a facility.";
            return;
        }
        page.IsLoading = true;
        page.Message = "Loading available Unit Types for the selected facility and rental period...";
        try
        {
            var query = "startMonth=" + Uri.EscapeDataString(start.ToString("yyyy-MM", CultureInfo.InvariantCulture)) +
                        "&endMonth=" + Uri.EscapeDataString(end.ToString("yyyy-MM", CultureInfo.InvariantCulture)) +
                        "&page=1&pageSize=100";
            using var json = await _api.GetAsync("api/v1/facilities/" + facilityId.ToString("D") + "/unit-types?" + query);
            var data = Property(json.RootElement, "data");
            if (data.ValueKind == JsonValueKind.Object)
            {
                var nested = Property(data, "items", "data");
                if (nested.ValueKind != JsonValueKind.Undefined) data = nested;
            }
            page.UnitTypes.Clear();
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = Field(item, "unitTypeId");
                    if (!Guid.TryParse(id, out _)) continue;
                    var name = Field(item, "name", "unitTypeName");
                    var mode = Field(item, "mode");
                    var size = Field(item, "size");
                    var price = Money(Field(item, "rentalPrice"));
                    var label = string.Join(" | ", new[] { name, mode, size, price }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    page.UnitTypes.Add(new CustomerUnitTypeOption { UnitTypeId = id, DisplayName = string.IsNullOrWhiteSpace(label) ? "Unit Type " + ShortId(id) : label });
                }
            }
            page.Message = page.UnitTypes.Count == 0 ? "No Unit Types are available for this facility and selected rental period." : page.UnitTypes.Count + " available Unit Type option(s).";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            page.Message = "Could not load Unit Types: " + ex.Message;
        }
        finally { page.IsLoading = false; }
    }

    private async Task CreateReservationAsync()
    {
        var page = CurrentPage;
        if (!Guid.TryParse(page.ReservationFacilityId.Trim(), out var facilityId) ||
            !Guid.TryParse(page.ReservationUnitTypeId.Trim(), out var unitTypeId))
        {
            page.Message = "Choose an available storage type first.";
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
            page.Message = "Choose a contract from the list.";
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
            {
                page.Message = "Access visit created successfully.";
                await LoadPageAsync("access", false);
            }
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
        if (route != "invoices") _paymentDialogLifetime?.Cancel();
        if (!IsAuthenticated) return;
        _currentRoute = route;
        var (title, description, path) = route switch
        {
            "home" => ("Home", "Your account overview", "api/v1/auth/me"),
            "storage" => ("Find Storage", "Search facilities and available storage space", "api/v1/facilities?page=1&pageSize=20"),
            "reservations" => ("My Reservations", "Your booked storage types and rental dates", "api/v1/reservations?page=1&pageSize=20"),
            "contracts" => ("My Contracts", "Your rental units and visits", "api/v1/contracts?page=1&pageSize=20"),
            "access" => ("Access Visits", "Choose an active contract and schedule an access visit", "api/v1/contracts?page=1&pageSize=100"),
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
            path = "api/v1/facilities/" + storageQuery;
            page.HasSearched = true;
            page.Search = "";
            page.SetRecords(Array.Empty<CustomerRecord>());
        }
        // My Reservations loads its records immediately when the menu is opened.
        // Only Find Storage intentionally waits for an explicit customer search.
        if (route == "reservations")
            page.HasSearched = true;
        CurrentPage = page;
        if (route == "storage" && !runStorageSearch)
        {
            page.IsLoading = true;
            try
            {
                await LoadFacilityOptionsAsync(page, cts.Token);
                page.Message = page.FacilityOptions.Count == 0
                    ? "No active facilities found. Check backend catalog or try Refresh."
                    : "Choose a facility, size and rental period, then click Find Storage.";
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                page.Message = "Cannot load Facilities: " + ex.Message;
            }
            finally
            {
                page.IsLoading = false;
                if (ReferenceEquals(_navigationCts, cts)) _navigationCts = null;
                cts.Dispose();
            }
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
            if (route == "storage")
                records = await ExpandStorageTypesAsync(records, page, cts.Token);
            if (route == "reservations" || route == "invoices")
                await EnrichStorageAsync(records, route, cts.Token);
            if (route == "reservations")
                await EnrichFacilityNamesAsync(records, cts.Token);
            if (route == "support")
                await LoadTicketContractsAsync(page, cts.Token);
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
                FacilityName = Field(element, "name", "facilityName"),
                Status = status,
                Detail1 = Field(element, "description"),
                Detail2 = "Contact: " + Field(element, "contactInfo"),
                Detail3 = "Facility ID: " + Field(element, "facilityId"),
                FacilityId = Field(element, "facilityId")
            },
            "access" => new CustomerRecord
            {
                Title = string.IsNullOrWhiteSpace(unitCode) ? "Rental contract" : "Unit " + unitCode,
                Subtitle = "Contract: " + ShortId(contractId),
                Status = status,
                Detail1 = "Storage ID: " + ShortId(Field(element, "storageUnitId")),
                Detail2 = "Storage type: " + Field(element, "unitTypeName", "unitTypeMode"),
                Detail3 = "Rental period: " + DateLabel(Field(element, "startMonth")) + " – " + DateLabel(Field(element, "endMonth")),
                Detail4 = "Contract ID: " + contractId,
                ContractId = contractId
            },
            "contracts" => new CustomerRecord
            {
                Title = string.IsNullOrWhiteSpace(unitCode) ? "Rental contract" : "Unit " + unitCode,
                Subtitle = "Contract: " + ShortId(contractId),
                Status = status,
                Detail1 = "Storage ID: " + ShortId(Field(element, "storageUnitId")),
                Detail2 = "Storage type: " + Field(element, "unitTypeName", "unitTypeMode"),
                Detail3 = "Start: " + DateLabel(Field(element, "startMonth")),
                Detail4 = "End: " + DateLabel(Field(element, "endMonth")),
                ContractId = contractId
            },
            "invoices" => new CustomerRecord
            {
                Title = Field(element, "invoiceType", "type"),
                InvoiceId = Guid.TryParse(Field(element, "invoiceId"), out var invoiceId) ? invoiceId : Guid.Empty,
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
                UnitTypeId = Field(element, "unitTypeId"),
                FacilityId = Field(element, "facilityId"),
                FacilityName = Field(element, "facilityName")
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


    private async Task LoadFacilityOptionsAsync(CustomerPageViewModel page, CancellationToken token)
    {
        var previous = page.SelectedFacilityId;
        page.FacilityOptions.Clear();
        // Facility catalog is paginated. Load all active facilities instead of only page 1.
        for (var pageNumber = 1; pageNumber <= 50; pageNumber++)
        {
            token.ThrowIfCancellationRequested();
            using var doc = await _api.GetAsync(
                $"api/v1/facilities?page={pageNumber}&pageSize=100", token);
            var data = Property(doc.RootElement, "data");
            if (data.ValueKind == JsonValueKind.Object)
            {
                var inner = Property(data, "items", "data");
                if (inner.ValueKind != JsonValueKind.Undefined) data = inner;
            }
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) break;
            foreach (var item in data.EnumerateArray())
            {
                var id = Field(item, "facilityId");
                if (!Guid.TryParse(id, out _) ||
                    page.FacilityOptions.Any(x => x.FacilityId == id)) continue;
                var name = Field(item, "name", "facilityName");
                var address = Field(item, "address", "location");
                var label = string.Join(" | ", new[] { name, address }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                page.FacilityOptions.Add(new CustomerFacilityOption {
                    FacilityId = id,
                    DisplayName = string.IsNullOrWhiteSpace(label) ? "Facility " + ShortId(id) : label
                });
            }
            if (data.GetArrayLength() < 100) break;
        }
        page.SelectedFacilityId = page.FacilityOptions.Any(x => x.FacilityId == previous)
            ? previous : "";
    }

    // Search results represent bookable Unit Types, not physical unit assignments.
    // A physical storage unit is selected by the Manager during handover.
    private async Task<List<CustomerRecord>> ExpandStorageTypesAsync(
        List<CustomerRecord> facilities, CustomerPageViewModel page, CancellationToken token)
    {
        var result = new List<CustomerRecord>();
        var start = page.StartMonth?.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ?? page.ReservationStartMonth;
        var end = page.EndMonth?.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            ?? page.ReservationEndMonth;
        page.ReservationStartMonth = start;
        page.ReservationEndMonth = end;
        foreach (var facility in facilities)
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParse(facility.FacilityId, out var facilityId)) continue;
            try
            {
                var q = "startMonth=" + Uri.EscapeDataString(start) +
                    "&endMonth=" + Uri.EscapeDataString(end) +
                    "&availableOnly=true&page=1&pageSize=100";
                if (page.StorageMode is "PRIVATE" or "PUBLIC")
                    q += "&mode=" + Uri.EscapeDataString(page.StorageMode);
                using var doc = await _api.GetAsync(
                    $"api/v1/facilities/{facilityId:D}/unit-types?{q}", token);
                var data = Property(doc.RootElement, "data");
                if (data.ValueKind == JsonValueKind.Object)
                {
                    var items = Property(data, "items", "data");
                    if (items.ValueKind != JsonValueKind.Undefined) data = items;
                }
                if (data.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in data.EnumerateArray())
                {
                    var id = Field(item, "unitTypeId");
                    if (!Guid.TryParse(id, out _)) continue;
                    var name = Field(item, "name", "unitTypeName");
                    var mode = Field(item, "mode");
                    var size = Field(item, "size");
                    if (page.StorageSize != "All" &&
                        !size.Equals(page.StorageSize, StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains(page.StorageSize, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new CustomerRecord {
                        Title = string.IsNullOrWhiteSpace(name) ? "Storage " + size : name,
                        Subtitle = facility.Title + " | " + facility.Subtitle,
                        FacilityName = facility.Title,
                        FacilityId = facility.FacilityId,
                        UnitTypeId = id,
                        UnitType = name,
                        Status = "AVAILABLE",
                        Detail1 = "Size: " + size + " | Type: " + mode,
                        Detail2 = "Rental: " + Money(Field(item, "rentalPrice")),
                        Detail3 = "Unit Type: " + ShortId(id)
                    });
                }
            }
            catch (HttpRequestException) { /* An unavailable facility does not hide the others. */ }
            catch (JsonException) { /* Ignore malformed optional catalog entries. */ }
        }
        return result;
    }

    private async Task LoadTicketContractsAsync(CustomerPageViewModel page, CancellationToken token)
    {
        page.TicketContracts.Clear();
        try
        {
            using var doc = await _api.GetAsync("api/v1/contracts?page=1&pageSize=100", token);
            var data = Property(doc.RootElement, "data");
            if (data.ValueKind == JsonValueKind.Object)
            {
                var items = Property(data, "items", "data");
                if (items.ValueKind != JsonValueKind.Undefined) data = items;
            }
            if (data.ValueKind != JsonValueKind.Array) return;
            foreach (var item in data.EnumerateArray())
            {
                var id = Field(item, "contractId");
                if (!Guid.TryParse(id, out _)) continue;
                var unit = Field(item, "unitCode", "storageUnitCode");
                var facility = Field(item, "facilityName");
                var type = Field(item, "unitTypeName", "unitTypeMode");
                var label = string.Join(" | ", new[] {
                    string.IsNullOrWhiteSpace(unit) ? "Contract " + ShortId(id) : "Unit " + unit,
                    facility, type }.Where(x => !string.IsNullOrWhiteSpace(x)));
                page.TicketContracts.Add(new CustomerContractOption { ContractId = id, DisplayName = label });
            }
            if (page.TicketContracts.Count == 1)
                page.TicketContractId = page.TicketContracts[0].ContractId;
        }
        catch (HttpRequestException ex) { page.Message = "Cannot load your contracts: " + ex.Message; }
        catch (JsonException ex) { page.Message = "Invalid contract list: " + ex.Message; }
    }

    private async Task EnrichFacilityNamesAsync(List<CustomerRecord> records, CancellationToken token)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(record.FacilityName) ||
                !Guid.TryParse(record.FacilityId, out var facilityId)) continue;
            if (!names.TryGetValue(record.FacilityId, out var name))
            {
                try
                {
                    using var doc = await _api.GetAsync($"api/v1/facilities/{facilityId:D}", token);
                    name = Field(Property(doc.RootElement, "data"), "name", "facilityName");
                }
                catch (HttpRequestException) { name = ""; }
                catch (JsonException) { name = ""; }
                names[record.FacilityId] = name;
            }
            record.FacilityName = string.IsNullOrWhiteSpace(name) ? "Facility " + ShortId(record.FacilityId) : name;
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

using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class CustomerRecord : ObservableObject
{
    public string Title { get; set; } = "";
    public string Subtitle { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail1 { get; init; } = "";
    public string Detail2 { get; init; } = "";
    public string Detail3 { get; init; } = "";
    public string Detail4 { get; init; } = "";
    public string Amount { get; init; } = "";
    public string Content { get; init; } = "";
    public string UnitType { get; set; } = "";
    public string UnitTypeId { get; init; } = "";
    public string RelatedEntityId { get; init; } = "";
    public string RelatedStorage { get; set; } = "";
    public string ContractId { get; init; } = "";
    public bool HasContract => Guid.TryParse(ContractId, out _);
    public bool CanCreateVisit => HasContract && Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);
    private DateTime? _visitDate = DateTime.Today.AddDays(1);
    public DateTime? VisitDate { get => _visitDate; set => SetProperty(ref _visitDate, value); }
}

public sealed class CustomerPageViewModel : ObservableObject
{
    public CustomerPageViewModel(string route, string title, string description)
    {
        Route = route;
        Title = title;
        Description = description;
        _message = route == "storage" ? "Choose your filters and select Find Storage." : "Loading from FRMS API...";
    }

    public string Route { get; }
    public string Title { get; }
    public string Description { get; }
    public bool IsHome => Route == "home";
    public bool IsStorage => Route == "storage";
    public bool IsReservations => Route == "reservations";
    public bool IsContracts => Route == "contracts";
    public bool IsInvoices => Route == "invoices";
    public bool IsSupport => Route == "support";

    private string _reservationFacilityId = "";
    public string ReservationFacilityId { get => _reservationFacilityId; set => SetProperty(ref _reservationFacilityId, value); }
    private string _reservationUnitTypeId = "";
    public string ReservationUnitTypeId { get => _reservationUnitTypeId; set => SetProperty(ref _reservationUnitTypeId, value); }
    private string _reservationStartMonth = DateTime.Today.AddMonths(1).ToString("yyyy-MM");
    public string ReservationStartMonth { get => _reservationStartMonth; set => SetProperty(ref _reservationStartMonth, value); }
    private string _reservationEndMonth = DateTime.Today.AddMonths(2).ToString("yyyy-MM");
    public string ReservationEndMonth { get => _reservationEndMonth; set => SetProperty(ref _reservationEndMonth, value); }
    private string _ticketContractId = "";
    public string TicketContractId { get => _ticketContractId; set => SetProperty(ref _ticketContractId, value); }
    private string _ticketCategory = "OTHER";
    public string TicketCategory { get => _ticketCategory; set => SetProperty(ref _ticketCategory, value); }
    public string[] TicketCategories { get; } = { "UNIT_ISSUE", "LOCK_KEY_ISSUE", "ACCESS_CARD_CODE_ISSUE", "PAYMENT_ISSUE", "STORED_ITEM_ISSUE", "DAMAGE_ISSUE", "OTHER" };
    private string _ticketDescription = "";
    public string TicketDescription { get => _ticketDescription; set => SetProperty(ref _ticketDescription, value); }
    private string _facilityQuery = "";
    public string FacilityQuery { get => _facilityQuery; set => SetProperty(ref _facilityQuery, value); }
    private string _storageMode = "All";
    public string StorageMode { get => _storageMode; set => SetProperty(ref _storageMode, value); }
    public string[] StorageModes { get; } = { "All", "PRIVATE", "PUBLIC" };
    private string _storageSize = "";
    public string StorageSize { get => _storageSize; set => SetProperty(ref _storageSize, value); }
    private DateTime? _startMonth;
    public DateTime? StartMonth { get => _startMonth; set => SetProperty(ref _startMonth, value); }
    private DateTime? _endMonth;
    public DateTime? EndMonth { get => _endMonth; set => SetProperty(ref _endMonth, value); }

    private bool _hasSearched;
    public bool HasSearched { get => _hasSearched; set => SetProperty(ref _hasSearched, value); }

    public ObservableCollection<CustomerRecord> Records { get; } = new();
    public ObservableCollection<CustomerRecord> VisibleRecords { get; } = new();

    public string[] StatusOptions => Route switch
    {
        "reservations" => new[] { "All", "PENDING", "CONFIRMED", "COMPLETED", "CANCELLED" },
        "contracts" => new[] { "All", "ACTIVE", "ENDED" },
        "invoices" => new[] { "All", "UNPAID", "PAID", "OVERDUE" },
        "support" => new[] { "All", "OPEN", "IN_PROGRESS", "COMPLETED", "CANCELLED" },
        _ => new[] { "All" }
    };

    private string _search = "";
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    private string _unitTypeFilter = "All";
    public string UnitTypeFilter { get => _unitTypeFilter; set { if (SetProperty(ref _unitTypeFilter, value)) ApplyFilter(); } }
    public ObservableCollection<string> UnitTypeOptions { get; } = new() { "All" };
    private string _contractFilter = "";
    public string ContractFilter { get => _contractFilter; set { if (SetProperty(ref _contractFilter, value)) ApplyFilter(); } }
    private string _selectedStatus = "All";
    public string SelectedStatus { get => _selectedStatus; set { if (SetProperty(ref _selectedStatus, value)) ApplyFilter(); } }
    private string _message;
    public string Message { get => _message; set => SetProperty(ref _message, value); }
    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public void SetRecords(System.Collections.Generic.IEnumerable<CustomerRecord> data)
    {
        Records.Clear();
        foreach (var record in data) Records.Add(record);
        UnitTypeOptions.Clear();
        UnitTypeOptions.Add("All");
        if (IsReservations)
            foreach (var type in Records.Select(x => x.UnitType).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
                UnitTypeOptions.Add(type);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = Records.Where(x =>
            (SelectedStatus == "All" || x.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase)) &&
            (!IsReservations || UnitTypeFilter == "All" || x.UnitType.Equals(UnitTypeFilter, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(ContractFilter) || x.ContractId.Equals(ContractFilter, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(Search) ||
             (x.Title + " " + x.Subtitle + " " + x.Status + " " + x.Detail1 + " " +
              x.Detail2 + " " + x.Detail3 + " " + x.Detail4 + " " + x.Content)
             .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)));
        VisibleRecords.Clear();
        foreach (var item in query) VisibleRecords.Add(item);
    }
}

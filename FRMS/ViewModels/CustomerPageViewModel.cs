using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class CustomerRecord : ObservableObject
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail1 { get; init; } = "";
    public string Detail2 { get; init; } = "";
    public string Detail3 { get; init; } = "";
    public string Detail4 { get; init; } = "";
    public string Amount { get; init; } = "";
    public string Content { get; init; } = "";
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
    public bool IsAccount => Route == "account";
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
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = Records.Where(x =>
            (SelectedStatus == "All" || x.Status.Equals(SelectedStatus, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(ContractFilter) || x.ContractId.Equals(ContractFilter, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(Search) ||
             (x.Title + " " + x.Subtitle + " " + x.Status + " " + x.Detail1 + " " +
              x.Detail2 + " " + x.Detail3 + " " + x.Detail4 + " " + x.Content)
             .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase)));
        VisibleRecords.Clear();
        foreach (var item in query) VisibleRecords.Add(item);
    }
}

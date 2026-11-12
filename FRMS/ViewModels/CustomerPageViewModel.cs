using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class CustomerRecord
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail1 { get; init; } = "";
    public string Detail2 { get; init; } = "";
    public string Detail3 { get; init; } = "";
    public string Detail4 { get; init; } = "";
}

public sealed class CustomerPageViewModel : ObservableObject
{
    public CustomerPageViewModel(string route, string title, string description)
    {
        Route = route;
        Title = title;
        Description = description;
        _message = "Loading from FRMS API...";
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

    public ObservableCollection<CustomerRecord> Records { get; } = new();
    public ObservableCollection<CustomerRecord> VisibleRecords { get; } = new();

    public string[] StatusOptions => Route switch
    {
        "reservations" => new[] { "All", "PENDING", "CONFIRMED", "COMPLETED", "CANCELLED" },
        "contracts" => new[] { "All", "ACTIVE", "ENDED" },
        "invoices" => new[] { "All", "UNPAID", "PAID", "OVERDUE" },
        _ => new[] { "All" }
    };

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (SetProperty(ref _search, value)) ApplyFilter(); }
    }

    private string _selectedStatus = "All";
    public string SelectedStatus
    {
        get => _selectedStatus;
        set { if (SetProperty(ref _selectedStatus, value)) ApplyFilter(); }
    }

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
            (string.IsNullOrWhiteSpace(Search) ||
             (x.Title + " " + x.Subtitle + " " + x.Status + " " + x.Detail1 + " " + x.Detail2)
              .Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        VisibleRecords.Clear();
        foreach (var item in query) VisibleRecords.Add(item);
    }
}

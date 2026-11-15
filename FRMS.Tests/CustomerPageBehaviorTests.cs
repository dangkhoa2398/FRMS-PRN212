using FRMS.PRN212.WpfConnection.ViewModels;
using Xunit;

namespace FRMS.Tests;

public sealed class CustomerPageBehaviorTests
{
    [Theory]
    [InlineData("home", true, false, false, false, false, false, false)]
    [InlineData("storage", false, true, false, false, false, false, false)]
    [InlineData("reservations", false, false, true, false, false, false, false)]
    [InlineData("contracts", false, false, false, true, false, false, false)]
    [InlineData("invoices", false, false, false, false, true, false, false)]
    [InlineData("support", false, false, false, false, false, true, false)]
    [InlineData("access", false, false, false, false, false, false, true)]
    public void RouteFlags_AreMutuallyExclusive(string route, bool home, bool storage,
        bool reservations, bool contracts, bool invoices, bool support, bool access)
    {
        var vm = Page(route);
        Assert.Equal(home, vm.IsHome);
        Assert.Equal(storage, vm.IsStorage);
        Assert.Equal(reservations, vm.IsReservations);
        Assert.Equal(contracts, vm.IsContracts);
        Assert.Equal(invoices, vm.IsInvoices);
        Assert.Equal(support, vm.IsSupport);
        Assert.Equal(access, vm.IsAccess);
    }

    [Theory]
    [InlineData("reservations", "CANCELLED")]
    [InlineData("contracts", "ENDED")]
    [InlineData("invoices", "OVERDUE")]
    [InlineData("support", "IN_PROGRESS")]
    public void StatusFilters_IncludeDomainStatus(string route, string expected)
    {
        Assert.Contains(expected, Page(route).StatusOptions);
    }

    [Fact]
    public void Storage_DoesNotRequireSearchUntilExplicitlyRequested()
    {
        var page = Page("storage");
        Assert.False(page.HasSearched);
        Assert.Contains("Choose your filters", page.Message);
        page.HasSearched = true;
        Assert.True(page.HasSearched);
    }

    [Fact]
    public void ReservationForm_SelectedUnitTypeUpdatesReservation()
    {
        var page = Page("reservations");
        page.SelectedUnitTypeId = Guid.NewGuid().ToString();
        Assert.Equal(page.SelectedUnitTypeId, page.ReservationUnitTypeId);
    }

    [Fact]
    public void CustomerRecord_ActiveValidContractAllowsVisit()
    {
        var record = new CustomerRecord { ContractId = Guid.NewGuid().ToString(), Status = "active" };
        Assert.True(record.HasContract);
        Assert.True(record.CanCreateVisit);
        record.VisitDate = DateTime.Today.AddDays(3);
        Assert.Equal(DateTime.Today.AddDays(3), record.VisitDate);
    }

    [Theory]
    [InlineData("", "ACTIVE")]
    [InlineData("not-a-guid", "ACTIVE")]
    [InlineData("aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "ENDED")]
    [InlineData("aaaa1111-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "PENDING")]
    public void CustomerRecord_InvalidOrInactiveContractDisablesVisit(string contractId, string status)
    {
        var record = new CustomerRecord { ContractId = contractId, Status = status };
        Assert.False(record.CanCreateVisit);
    }

    [Theory]
    [InlineData("support")]
    [InlineData("contracts")]
    [InlineData("invoices")]
    [InlineData("reservations")]
    public void StatusFilter_FiltersRecordsCaseInsensitively(string route)
    {
        var page = Page(route);
        page.SetRecords(new[]
        {
            new CustomerRecord { Title = "First", Status = "PENDING" },
            new CustomerRecord { Title = "Second", Status = "COMPLETED" }
        });
        page.SelectedStatus = "completed";
        Assert.Single(page.VisibleRecords);
        Assert.Equal("Second", page.VisibleRecords[0].Title);
    }

    [Fact]
    public void Search_ExaminesDescriptionAndFacilityCaseInsensitively()
    {
        var page = Page("support");
        page.SetRecords(new[]
        {
            new CustomerRecord { Title = "Ticket", Content = "Gate LOCK jammed", FacilityName = "North" },
            new CustomerRecord { Title = "Ticket", Content = "Invoice inquiry", FacilityName = "South" }
        });
        page.Search = "  lock  ";
        Assert.Single(page.VisibleRecords);
        Assert.Equal("North", page.VisibleRecords[0].FacilityName);
        page.Search = "south";
        Assert.Single(page.VisibleRecords);
        Assert.Equal("South", page.VisibleRecords[0].FacilityName);
    }

    [Fact]
    public void ReservationUnitTypeOptions_AreDistinctAndSorted()
    {
        var page = Page("reservations");
        page.SetRecords(new[]
        {
            new CustomerRecord { UnitType = "Large" },
            new CustomerRecord { UnitType = "small" },
            new CustomerRecord { UnitType = "SMALL" },
            new CustomerRecord { UnitType = "" }
        });
        Assert.Equal(3, page.UnitTypeOptions.Count);
        Assert.Equal("All", page.UnitTypeOptions[0]);
        Assert.Equal("Large", page.UnitTypeOptions[1]);
        Assert.Equal("small", page.UnitTypeOptions[2]);
    }

    [Fact]
    public void ClearingSearch_RestoresResults()
    {
        var page = Page("support");
        page.SetRecords(new[] { new CustomerRecord { Title = "A" }, new CustomerRecord { Title = "B" } });
        page.Search = "not found";
        Assert.Empty(page.VisibleRecords);
        page.Search = "";
        Assert.Equal(2, page.VisibleRecords.Count);
    }

    [Fact]
    public void SetRecords_ResetsOldUnitTypeAndRaisesChangeNotification()
    {
        var page = Page("reservations");
        page.SetRecords(new[] { new CustomerRecord { UnitType = "SMALL" } });
        page.UnitTypeFilter = "SMALL";
        var changes = new List<string?>();
        page.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        page.SetRecords(new[] { new CustomerRecord { UnitType = "LARGE" } });
        Assert.Equal("All", page.UnitTypeFilter);
        Assert.Contains(nameof(page.UnitTypeFilter), changes);
        Assert.Single(page.VisibleRecords);
    }

    [Fact]
    public void UnrelatedRoute_IgnoresUnitTypeFilter()
    {
        var page = Page("contracts");
        page.SetRecords(new[] { new CustomerRecord { Title = "Rental A" }, new CustomerRecord { Title = "Rental B" } });
        page.UnitTypeFilter = "DOES_NOT_EXIST";
        Assert.Equal(2, page.VisibleRecords.Count);
    }

    [Fact]
    public void ValidReservationPeriod_DefaultsToFutureMonths()
    {
        var page = Page("reservations");
        Assert.True(DateTime.TryParseExact(page.ReservationStartMonth + "-01", "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var start));
        Assert.True(DateTime.TryParseExact(page.ReservationEndMonth + "-01", "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var end));
        Assert.True(start < end);
    }

    [Fact]
    public void TicketCategory_ContainsSupportedBackendValues()
    {
        var page = Page("support");
        Assert.Equal("OTHER", page.TicketCategory);
        Assert.Equal(7, page.TicketCategories.Length);
        Assert.Contains("PAYMENT_ISSUE", page.TicketCategories);
        Assert.Contains("LOCK_KEY_ISSUE", page.TicketCategories);
    }

    [Fact]
    public void Notifications_OnVisibleProperties_AreRaised()
    {
        var page = Page("support");
        var raised = new List<string?>();
        page.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
        page.TicketDescription = "Broken gate";
        page.Message = "Loading";
        page.IsLoading = true;
        Assert.Contains(nameof(page.TicketDescription), raised);
        Assert.Contains(nameof(page.Message), raised);
        Assert.Contains(nameof(page.IsLoading), raised);
    }

    private static CustomerPageViewModel Page(string route) => new(route, "title", "description");
}

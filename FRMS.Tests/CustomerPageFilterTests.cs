using FRMS.PRN212.WpfConnection.ViewModels;
using Xunit;

namespace FRMS.Tests;

public sealed class CustomerPageFilterTests
{
    [Fact]
    public void Reservations_FilterByUnitTypeStatusAndCaseInsensitiveSearch()
    {
        var page = new CustomerPageViewModel("reservations", "Reservations", "Test");
        page.SetRecords(new[]
        {
            new CustomerRecord { Title = "Unit A", UnitType = "SMALL", Status = "PENDING", FacilityName = "Central Facility" },
            new CustomerRecord { Title = "Unit B", UnitType = "LARGE", Status = "CANCELLED", FacilityName = "West Facility" }
        });

        page.UnitTypeFilter = "SMALL";
        Assert.Single(page.VisibleRecords);
        Assert.Equal("Unit A", page.VisibleRecords[0].Title);

        page.SelectedStatus = "CANCELLED";
        Assert.Empty(page.VisibleRecords);

        page.SelectedStatus = "All";
        page.Search = "cEnTrAl";
        Assert.Single(page.VisibleRecords);
        Assert.Equal("Unit A", page.VisibleRecords[0].Title);
    }

    [Fact]
    public void Refresh_RebuildsUnitTypeOptionsAndResetsStaleFilter()
    {
        var page = new CustomerPageViewModel("reservations", "Reservations", "Test");
        page.SetRecords(new[] { new CustomerRecord { UnitType = "SMALL", Status = "PENDING" } });
        page.UnitTypeFilter = "SMALL";

        page.SetRecords(new[] { new CustomerRecord { UnitType = "LARGE", Status = "PENDING" } });

        Assert.Equal("All", page.UnitTypeFilter);
        Assert.Contains("LARGE", page.UnitTypeOptions);
        Assert.DoesNotContain("SMALL", page.UnitTypeOptions);
        Assert.Single(page.VisibleRecords);
    }

    [Fact]
    public void Contracts_FilterByContractId()
    {
        var page = new CustomerPageViewModel("contracts", "Contracts", "Test");
        page.SetRecords(new[]
        {
            new CustomerRecord { Title = "First", ContractId = "ABC-1" },
            new CustomerRecord { Title = "Second", ContractId = "ABC-2" }
        });
        page.ContractFilter = "abc-2";

        Assert.Single(page.VisibleRecords);
        Assert.Equal("Second", page.VisibleRecords[0].Title);
    }
}

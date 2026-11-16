using System.Net;
using System.Net.Http;
using System.Text.Json;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.ViewModels;
using Xunit;

namespace FRMS.WpfTests;

public sealed class EmployeeWorkspaceTests
{
    private static async Task<EmployeeWorkspaceViewModel> NewEmployeeAsync(FakeBackend server, string role)
    {
        var vm = new EmployeeWorkspaceViewModel(new CustomerApiService(server.NewHttpClient()));
        await vm.InitializeAsync(role);
        return vm;
    }

    [Theory]
    [InlineData("FACILITY_MANAGER", true)]
    [InlineData("FACILITY_STAFF", false)]
    public Task Initialize_ReadsProfileAndSetsRole(string role, bool manager) => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, role);
        Assert.Equal(manager, vm.IsManager);
        Assert.Equal(!manager, vm.IsStaff);
        Assert.Equal("Demo Employee", vm.DisplayName);
        Assert.Equal("Demo Facility", vm.FacilityName);
        Assert.Equal(FakeBackend.FacilityId, vm.FacilityId);
        Assert.Equal("staff@demo.test", vm.Email);
        Assert.True(vm.IsHome);
        Assert.False(vm.HasRecords);
        Assert.False(vm.ShowFilters);
        Assert.Equal(2, server.Requests.Count);
    });

    [Theory]
    [InlineData("FACILITY_MANAGER", "task", "/api/v1/reservations", "page=1&pageSize=20")]
    [InlineData("FACILITY_STAFF", "task", "/api/v1/staff/work-items", "includeHistory=true")]
    [InlineData("FACILITY_STAFF", "access", "/api/v1/staff/work-items", "allDates=true")]
    [InlineData("FACILITY_MANAGER", "access", "/api/v1/visits", "page=1")]
    [InlineData("FACILITY_MANAGER", "reservations", "/api/v1/reservations", "page=1")]
    [InlineData("FACILITY_STAFF", "storage", "/api/v1/contracts", "page=1")]
    [InlineData("FACILITY_MANAGER", "storage", "/api/v1/facilities/11111111-1111-1111-1111-111111111111/storage-units", "page=1")]
    [InlineData("FACILITY_MANAGER", "support", "/api/v1/support-tickets", "page=1")]
    [InlineData("FACILITY_STAFF", "support", "/api/v1/support-tickets", "page=1")]
    public Task Navigation_UsesRoleSpecificApiRoute(string role, string route, string path, string queryPart) =>
        StaTest.RunAsync(async () =>
        {
            var server = new FakeBackend();
            var vm = await NewEmployeeAsync(server, role);
            await vm.NavigateCommand.ExecuteAsync(route);
            Assert.Equal(route, vm.Route);
            Assert.Contains(server.Requests, r => r.Path == path && r.Query.Contains(queryPart));
            Assert.False(vm.IsLoading);
        });

    [Fact]
    public Task HomeNavigation_ClearsLoadingWhenItCancelsAnOpenRequest() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        server.ReplyAsync = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeBackend.Json();
        };

        var pending = vm.NavigateCommand.ExecuteAsync("task");
        Assert.True(vm.IsLoading);
        await vm.NavigateCommand.ExecuteAsync("home");
        await pending;

        Assert.True(vm.IsHome);
        Assert.False(vm.IsLoading);
        Assert.Empty(vm.Items);
    });

    [Fact]
    public Task Initialize_PreservesProfileErrorOnHome() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend { Reply = _ => FakeBackend.Json(status: HttpStatusCode.ServiceUnavailable) };
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");

        Assert.True(vm.IsHome);
        Assert.Contains("Employee profile could not be loaded", vm.Message);
    });

    [Fact]
    public Task Staff_AccessShowsOnlyVisitsAndCanFilterByDate() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend
        {
            Reply = r => r.Path == "/api/v1/staff/work-items"
                ? FakeBackend.Json("""
                {"data":{"items":[
                    {"workType":"ACCESS_VISIT","status":"SCHEDULED","referenceId":"44444444-4444-4444-4444-444444444444","scheduledDate":"2026-11-18"},
                    {"workType":"INSPECTION","status":"COMPLETED","inspectionId":"88888888-8888-8888-8888-888888888888","scheduledDate":"2026-11-18"},
                    {"workType":"RETURN_VISIT","status":"COMPLETED","scheduledDate":"2026-11-19"}
                ]}}
                """)
                : r.Path == "/api/v1/auth/me" || r.Path == "/api/v1/facilities/" + FakeBackend.FacilityId
                    ? FakeProfile(r)
                    : FakeBackend.Json("""{"data":{"items":[]}}""")
        };
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        await vm.NavigateCommand.ExecuteAsync("access");
        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, x => Assert.True(x.IsVisit));
        vm.SelectedDate = new DateTime(2026, 11, 18);
        Assert.Single(vm.Items);
        Assert.Equal("SCHEDULED", vm.Items[0].Status);
        vm.SelectedDate = null;
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.ShowDateFilter);
    });

    [Fact]
    public Task Staff_TaskShowsHistoryAndFiltersStatusAndSearch() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend { Reply = r => r.Path == "/api/v1/staff/work-items"
            ? FakeBackend.Json("""
                {"data":{"items":[
                {"workType":"ACCESS_VISIT","status":"SCHEDULED","scheduledDate":"2026-11-19"},
                {"workType":"RETURN_VISIT","status":"COMPLETED","scheduledDate":"2026-11-20"},
                {"workType":"INSPECTION","status":"COMPLETED","scheduledDate":"2026-11-21"}
            ]}}
            """)
            : FakeProfile(r) };
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        await vm.NavigateCommand.ExecuteAsync("task");
        Assert.Equal(3, vm.Items.Count);
        Assert.Contains("COMPLETED", vm.StatusOptions);
        vm.SelectedStatus = "COMPLETED";
        Assert.Equal(2, vm.Items.Count);
        vm.SearchText = "INSPECTION";
        Assert.Single(vm.Items);
        Assert.Equal("INSPECTION", vm.Items[0].WorkType);
        vm.ClearWorkDateFilterCommand.Execute(null);
        Assert.Null(vm.SelectedDate);
    });

    [Theory]
    [InlineData("SCHEDULED", "ACCESS_VISIT", true, false)]
    [InlineData("CHECKED_IN", "ACCESS_VISIT", false, true)]
    [InlineData("CHECKED_IN", "RESERVATION_VISIT", false, true)]
    [InlineData("CHECKED_IN", "RETURN_VISIT", false, true)]
    [InlineData("COMPLETED", "RETURN_VISIT", false, false)]
    [InlineData("SCHEDULED", "INSPECTION", false, false)]
    public void WorkItemActions_RespectStatus(string status, string type, bool canIn, bool canOut)
    {
        var item = new EmployeeWorkItem { Status = status, WorkType = type };
        Assert.Equal(canIn, item.CanCheckIn);
        Assert.Equal(canOut, item.CanCheckOut);
        Assert.Equal(type == "RESERVATION_VISIT" && status == "CHECKED_IN", item.CanCompleteHandover);
        Assert.Equal(type == "RETURN_VISIT" && status == "CHECKED_IN", item.CanConfirmReturn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task CheckInAndOut_UseVisitCommands(bool checkIn) => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        await vm.NavigateCommand.ExecuteAsync("access");
        var beforeRefresh = server.Requests.Count(r => r.Path == "/api/v1/staff/work-items");
        var item = new EmployeeWorkItem { ReferenceId = FakeBackend.VisitId, Status = checkIn ? "SCHEDULED" : "CHECKED_IN", WorkType = "ACCESS_VISIT" };
        if (checkIn) await vm.CheckInCommand.ExecuteAsync(item);
        else await vm.CheckOutCommand.ExecuteAsync(item);
        Assert.Contains(server.Requests, r => r.Method == HttpMethod.Post &&
            r.Path == "/api/v1/visits/" + FakeBackend.VisitId + (checkIn ? "/check-in" : "/check-out"));
        Assert.Equal(beforeRefresh + 1, server.Requests.Count(r => r.Path == "/api/v1/staff/work-items"));
    });

    [Fact]
    public Task CheckIn_InvalidVisitIdIsRejectedWithoutPost() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        await vm.CheckInCommand.ExecuteAsync(new EmployeeWorkItem { ReferenceId = "bad", WorkType = "ACCESS_VISIT" });
        Assert.Contains("missing", vm.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server.Requests, r => r.Method == HttpMethod.Post);
    });

    [Fact]
    public Task Staff_CompleteTicketValidatesNoteThenPosts() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        var ticket = new EmployeeWorkItem { ReferenceId = FakeBackend.TicketId };
        await vm.CompleteTicketCommand.ExecuteAsync(ticket);
        Assert.Contains("completion note", vm.Message, StringComparison.OrdinalIgnoreCase);
        vm.ActionNote = "  Resolved  ";
        await vm.CompleteTicketCommand.ExecuteAsync(ticket);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/complete"));
        Assert.Equal(HttpMethod.Post, req.Method);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal("Resolved", json.RootElement.GetProperty("resultNote").GetString());
        Assert.Equal("", vm.ActionNote);
    });

    [Fact]
    public Task Manager_AssignTicketValidatesAndPostsEmployeeId() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_MANAGER");
        var ticket = new EmployeeWorkItem { ReferenceId = FakeBackend.TicketId };
        await vm.AssignTicketCommand.ExecuteAsync(ticket);
        Assert.Contains("Choose", vm.Message);
        vm.SelectedStaffId = FakeBackend.EmployeeId;
        await vm.AssignTicketCommand.ExecuteAsync(ticket);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/assign"));
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(FakeBackend.EmployeeId, json.RootElement.GetProperty("employeeId").GetString());
        Assert.Equal("", vm.SelectedStaffId);
    });

    [Fact]
    public Task Manager_CreateStorageValidatesInputsThenPosts() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_MANAGER");
        await vm.CreateStorageUnitCommand.ExecuteAsync(null);
        Assert.Contains("storage type", vm.Message);
        vm.NewUnitTypeId = FakeBackend.UnitTypeId;
        vm.NewUnitCode = " A-103 ";
        vm.NewUnitLocation = " Level 2 ";
        await vm.CreateStorageUnitCommand.ExecuteAsync(null);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/storage-units") && r.Method == HttpMethod.Post);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal("A-103", json.RootElement.GetProperty("unitCode").GetString());
        Assert.Equal("Level 2", json.RootElement.GetProperty("locationInfo").GetString());
        Assert.Equal("", vm.NewUnitCode);
    });

    [Fact]
    public Task Manager_ChangeStorageUnitStatusUsesPhysicalUnitId() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_MANAGER");
        vm.StorageStatus = "MAINTENANCE";
        await vm.ChangeStorageStatusCommand.ExecuteAsync(new EmployeeWorkItem { StorageUnitId = FakeBackend.StorageUnitId });
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/status"));
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal("MAINTENANCE", json.RootElement.GetProperty("status").GetString());
    });

    [Fact]
    public Task Staff_InspectionClaimAndCompletionValidateAndSendNote() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        var inspection = new EmployeeWorkItem { WorkType = "INSPECTION", InspectionId = FakeBackend.InspectionId };
        await vm.CompleteInspectionCommand.ExecuteAsync(inspection);
        Assert.Contains("condition note", vm.Message);
        await vm.ClaimInspectionCommand.ExecuteAsync(inspection);
        inspection.InspectionNote = "  No damage  ";
        await vm.CompleteInspectionCommand.ExecuteAsync(inspection);
        Assert.Contains(server.Requests, r => r.Path.EndsWith("/claim") && r.Method == HttpMethod.Post);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/complete"));
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal("No damage", json.RootElement.GetProperty("conditionNote").GetString());
    });

    [Fact]
    public Task Staff_CompleteHandoverUsesReservationVisitAndUnit() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        var item = new EmployeeWorkItem { EntityId = FakeBackend.ContractId, ReferenceId = FakeBackend.VisitId };
        await vm.CompleteHandoverCommand.ExecuteAsync(item);
        Assert.Contains("Storage Unit ID", vm.Message);
        vm.ActionInput = FakeBackend.StorageUnitId;
        await vm.CompleteHandoverCommand.ExecuteAsync(item);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/complete-handover"));
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(FakeBackend.VisitId, json.RootElement.GetProperty("visitId").GetString());
        Assert.Equal(FakeBackend.StorageUnitId, json.RootElement.GetProperty("storageUnitId").GetString());
    });

    [Fact]
    public Task Staff_ConfirmReturnUsesIsoDate() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_STAFF");
        var item = new EmployeeWorkItem { ReferenceId = FakeBackend.VisitId };
        vm.ActionInput = "15/11/2026";
        await vm.ConfirmReturnCommand.ExecuteAsync(item);
        Assert.Contains("yyyy-MM-dd", vm.Message);
        vm.ActionInput = "2026-11-15";
        await vm.ConfirmReturnCommand.ExecuteAsync(item);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/confirm-return"));
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal("2026-11-15", json.RootElement.GetProperty("actualReturnDate").GetString());
    });

    [Fact]
    public Task Manager_AutomaticHandoverAssignmentSelectsMatchingAvailableUnit() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend { Reply = r =>
        {
            if (r.Path.EndsWith("/handover-unit-assignment")) return FakeBackend.Json();
            if (r.Path == "/api/v1/reservations/" + FakeBackend.ContractId)
                return FakeBackend.Json("{\"data\":{\"facilityId\":\"" + FakeBackend.FacilityId + "\",\"unitTypeId\":\"" + FakeBackend.UnitTypeId + "\"}}");
            if (r.Path.EndsWith("/storage-units"))
                return FakeBackend.Json("{\"data\":{\"items\":[{\"storageUnitId\":\"" + FakeBackend.StorageUnitId + "\",\"unitCode\":\"A-103\"}]}}");
            return FakeProfile(r);
        }};
        var vm = await NewEmployeeAsync(server, "FACILITY_MANAGER");
        await vm.AssignHandoverUnitCommand.ExecuteAsync(new EmployeeWorkItem { ReferenceId = FakeBackend.ContractId });
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/handover-unit-assignment"));
        Assert.Equal(HttpMethod.Put, req.Method);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(FakeBackend.StorageUnitId, json.RootElement.GetProperty("storageUnitId").GetString());
    });

    [Fact]
    public Task Reset_RevokesRoleClearsProfileAndItems() => StaTest.RunAsync(async () =>
    {
        var server = new FakeBackend();
        var vm = await NewEmployeeAsync(server, "FACILITY_MANAGER");
        await vm.NavigateCommand.ExecuteAsync("support");
        vm.Reset();
        Assert.Equal("", vm.Role);
        Assert.Equal("", vm.FacilityId);
        Assert.Equal("Employee", vm.DisplayName);
        Assert.Empty(vm.Items);
        Assert.False(vm.IsManager);
    });

    private static HttpResponseMessage FakeProfile(RequestLog r)
    {
        if (r.Path == "/api/v1/auth/me")
            return FakeBackend.Json("{\"data\":{\"profile\":{\"fullName\":\"Demo Employee\",\"facilityId\":\"" +
                FakeBackend.FacilityId + "\"}}}");
        if (r.Path == "/api/v1/facilities/" + FakeBackend.FacilityId)
            return FakeBackend.Json("""{"data":{"name":"Demo Facility"}}""");
        return FakeBackend.Json("""{"data":{"items":[]}}""");
    }
}

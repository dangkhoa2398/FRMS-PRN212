using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.ViewModels;
using Xunit;

namespace FRMS.WpfTests;

public sealed class MainViewModelTests
{
    private const string AlternativeTypeId = "99999999-9999-9999-9999-999999999999";

    private static FakeBackend NewBackend(Func<RequestLog, HttpResponseMessage?>? overrideReply = null) =>
        new() { Reply = r => overrideReply?.Invoke(r) ?? StandardResponse(r) };

    private static HttpResponseMessage StandardResponse(RequestLog r)
    {
        if (r.Path.EndsWith("/auth/customer/login"))
            return FakeBackend.Json("""{"data":{"accessToken":"customer-jwt","user":{"role":"CUSTOMER"}}}""");
        if (r.Path.EndsWith("/auth/employee/login"))
            return FakeBackend.Json("""{"data":{"accessToken":"employee-jwt","user":{"role":"FACILITY_MANAGER"}}}""");
        if (r.Path == "/api/v1/auth/me")
            return FakeBackend.Json("{\"data\":{\"profile\":{\"fullName\":\"Demo Customer\",\"facilityId\":\"" +
                FakeBackend.FacilityId + "\"},\"email\":\"demo@example.test\"}}");
        if (r.Path == "/api/v1/facilities")
            return FakeBackend.Json("{\"data\":{\"items\":[{\"facilityId\":\"" + FakeBackend.FacilityId +
                "\",\"name\":\"Demo Facility\",\"address\":\"HCMC\"}]}}");
        if (r.Path == "/api/v1/facilities/" + FakeBackend.FacilityId)
            return FakeBackend.Json("{\"data\":{\"facilityId\":\"" + FakeBackend.FacilityId +
                "\",\"name\":\"Demo Facility\",\"status\":\"ACTIVE\"}}");
        if (r.Path.EndsWith("/unit-types"))
            return FakeBackend.Json("{\"data\":{\"items\":[{\"unitTypeId\":\"" + FakeBackend.UnitTypeId +
                "\",\"name\":\"Small Locker\",\"mode\":\"PRIVATE\",\"size\":\"SMALL\",\"rentalPrice\":400000}," +
                "{\"unitTypeId\":\"" + AlternativeTypeId +
                "\",\"name\":\"Large Locker\",\"mode\":\"PUBLIC\",\"size\":\"LARGE\",\"rentalPrice\":900000}]}}");
        if (r.Path == "/api/v1/contracts")
            return FakeBackend.Json("{\"data\":{\"items\":[{\"contractId\":\"" + FakeBackend.ContractId +
                "\",\"unitCode\":\"A-101\",\"status\":\"ACTIVE\",\"facilityName\":\"Demo Facility\"}]}}");
        if (r.Path == "/api/v1/reservations")
            return FakeBackend.Json("{\"data\":{\"items\":[{\"reservationId\":\"" + FakeBackend.ContractId +
                "\",\"unitTypeName\":\"Small Locker\",\"status\":\"PENDING\"}]}}");
        if (r.Path == "/api/v1/support-tickets")
            return FakeBackend.Json("{\"data\":{\"items\":[{\"supportTicketId\":\"" + FakeBackend.TicketId +
                "\",\"category\":\"OTHER\",\"status\":\"OPEN\"}]}}");
        if (r.Path == "/api/v1/invoices")
            return FakeBackend.Json("""{"data":{"items":[{"invoiceType":"RENTAL","status":"UNPAID","amountDue":100000}]}}""");
        return FakeBackend.Json("""{"data":{"items":[]}}""");
    }

    private sealed class FakeHealth : IFrmsHealthService
    {
        public HealthCheckResult Result { get; init; } = new(true, 200, "Healthy");
        public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult(Result);
    }

    private static MainViewModel NewMain(FakeBackend server, IFrmsHealthService? health = null) =>
        new(new CustomerApiService(server.NewHttpClient()), health ?? new FakeHealth());

    private static async Task SignInCustomerAsync(MainViewModel vm)
    {
        vm.PhoneNumber = "0900000000";
        await vm.LoginCommand.ExecuteAsync(new PasswordBox { Password = "correct-password" });
        Assert.True(vm.IsAuthenticated);
    }

    [Fact]
    public Task LoginAndRoleViews_LoadAndLayOutInBothThemes() => StaTest.RunAsync(async () =>
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FRMS;component/Themes/Controls.xaml", UriKind.Relative)
        });
        var employeeRole = "FACILITY_MANAGER";
        var server = NewBackend(r => r.Path.EndsWith("/auth/employee/login")
            ? FakeBackend.Json(JsonSerializer.Serialize(new
            {
                data = new { accessToken = "employee-jwt", user = new { role = employeeRole } }
            }))
            : null);
        var vm = NewMain(server);
        var window = new FRMS.PRN212.WpfConnection.MainWindow(vm);
        var root = Assert.IsType<Grid>(window.Content);
        var loginHost = Assert.IsType<ContentControl>(window.FindName("LoginHost"));
        var customerHost = Assert.IsType<ContentControl>(window.FindName("ShellHost"));
        var employeeHost = Assert.IsType<ContentControl>(window.FindName("EmployeeShellHost"));

        async Task LayoutAsync()
        {
            root.Measure(new Size(1000, 650));
            root.Arrange(new Rect(0, 0, 1000, 650));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            root.UpdateLayout();
        }

        try
        {
            foreach (var theme in new[] { "Light", "Dark" })
            {
                app.Resources.MergedDictionaries[0] = new ResourceDictionary
                {
                    Source = new Uri($"/FRMS;component/Themes/{theme}.xaml", UriKind.Relative)
                };
                await LayoutAsync();
                Assert.Equal(Visibility.Visible, loginHost.Visibility);
                var phone = VisibleLoginField(loginHost);
                var password = Assert.Single(Visuals<PasswordBox>(loginHost));
                phone.SetCurrentValue(TextBox.TextProperty, "0900000000");
                password.Password = "test-password";
                Assert.Equal("0900000000", vm.PhoneNumber);
                AssertInputLayout(phone);
                AssertInputLayout(password);
                var accountType = Assert.Single(Visuals<ComboBox>(loginHost));
                var selection = Assert.Single(Visuals<TextBlock>(accountType), x => x.Text == "Customer");
                Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(selection.Foreground).Color);

                var submit = Assert.Single(Visuals<Button>(loginHost), x => Equals(x.Content, "Sign in"));
                Assert.Same(password, submit.CommandParameter);
                await vm.LoginCommand.ExecuteAsync(submit.CommandParameter as PasswordBox);
                Assert.Empty(password.Password);
                foreach (var route in new[] { "home", "storage", "reservations", "contracts", "invoices", "support" })
                {
                    await vm.NavigateCommand.ExecuteAsync(route);
                    await LayoutAsync();
                    Assert.Equal(route, vm.CurrentPage.Route);
                    Assert.Equal(Visibility.Visible, customerHost.Visibility);
                }
                vm.LogoutCommand.Execute(null);

                foreach (var role in new[] { "FACILITY_MANAGER", "FACILITY_STAFF" })
                {
                    employeeRole = role;
                    vm.LoginMode = "Staff / Manager";
                    await LayoutAsync();
                    var email = VisibleLoginField(loginHost);
                    email.SetCurrentValue(TextBox.TextProperty, "employee@example.test");
                    password.Password = "test-password";
                    Assert.Equal("employee@example.test", vm.EmployeeEmail);
                    AssertInputLayout(email);
                    await vm.LoginCommand.ExecuteAsync(password);
                    Assert.Equal(role, vm.EmployeeRole);
                    var routes = new[] { "home", "storage", "access", "support",
                        role == "FACILITY_MANAGER" ? "reservations" : "task" };
                    foreach (var route in routes)
                    {
                        await vm.EmployeeWorkspace.NavigateCommand.ExecuteAsync(route);
                        await LayoutAsync();
                        Assert.Equal(route, vm.EmployeeWorkspace.Route);
                        Assert.Equal(Visibility.Visible, employeeHost.Visibility);
                        if (route == "access")
                        {
                            var picker = Assert.Single(Visuals<DatePicker>(employeeHost));
                            var dateField = Assert.IsType<System.Windows.Controls.Primitives.DatePickerTextBox>(
                                picker.Template.FindName("PART_TextBox", picker));
                            Assert.Equal(SystemColors.WindowTextColor, Assert.IsType<SolidColorBrush>(dateField.Foreground).Color);
                        }
                    }
                    vm.LogoutCommand.Execute(null);
                }
                vm.LoginMode = "Customer";
            }
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    });

    private static TextBox VisibleLoginField(ContentControl host) =>
        Assert.Single(Visuals<TextBox>(host), input =>
            VisualTreeHelper.GetParent(input) is StackPanel { Visibility: Visibility.Visible });

    private static void AssertInputLayout(Control input)
    {
        var host = Assert.IsType<ScrollViewer>(input.Template.FindName("PART_ContentHost", input));
        var presenter = Assert.Single(Visuals<ScrollContentPresenter>(host));
        Assert.True(presenter.ActualHeight >= input.FontSize,
            $"{input.GetType().Name} has only {presenter.ActualHeight} DIP for its text.");
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }

    [Fact]
    public Task CustomerLogin_LoadsProfileAndAuthenticatedHome() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        Assert.False(vm.IsEmployee);
        Assert.Equal("Demo Customer", vm.CustomerName);
        Assert.True(vm.CurrentPage.IsHome);
        Assert.Contains(server.Requests, r => r.Path == "/api/v1/auth/me" &&
            r.Authorization == "Bearer customer-jwt");
    });

    [Fact]
    public Task EmployeeLogin_SetsRoleAndLoadsEmployeeWorkspace() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        vm.LoginMode = "Staff / Manager";
        vm.EmployeeEmail = "manager@example.test";
        await vm.LoginCommand.ExecuteAsync(new PasswordBox { Password = "password" });
        Assert.True(vm.IsAuthenticated);
        Assert.True(vm.IsEmployee);
        Assert.Equal("FACILITY_MANAGER", vm.EmployeeRole);
        Assert.True(vm.EmployeeWorkspace.IsManager);
        Assert.True(vm.EmployeeWorkspace.IsHome);
        Assert.Equal("Demo Customer", vm.EmployeeWorkspace.DisplayName);
    });

    [Theory]
    [InlineData("Customer", "", "")]
    [InlineData("Staff / Manager", "", "")]
    [InlineData("Customer", "0900000000", "")]
    [InlineData("Staff / Manager", "manager@test", "")]
    public Task LoginWithoutRequiredFields_DoesNotContactApi(string mode, string login, string password) =>
        StaTest.RunAsync(async () =>
        {
            var server = NewBackend();
            var vm = NewMain(server);
            vm.LoginMode = mode;
            vm.PhoneNumber = login;
            vm.EmployeeEmail = login;
            await vm.LoginCommand.ExecuteAsync(new PasswordBox { Password = password });
            Assert.False(vm.IsAuthenticated);
            Assert.NotEmpty(vm.LoginError);
            Assert.Empty(server.Requests);
        });

    [Fact]
    public Task LoginFailure_PreservesSignedOutStateAndShowsError() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend(r => r.Path.Contains("/login") ?
            FakeBackend.Json("{}", HttpStatusCode.Unauthorized) : null);
        var vm = NewMain(server);
        vm.PhoneNumber = "0900000000";
        await vm.LoginCommand.ExecuteAsync(new PasswordBox { Password = "bad" });
        Assert.False(vm.IsAuthenticated);
        Assert.Contains("401", vm.LoginError);
        Assert.False(vm.IsLoggingIn);
    });

    [Fact]
    public Task Logout_ClearsAuthenticationEmployeeStateAndBearerToken() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        vm.LogoutCommand.Execute(null);
        Assert.False(vm.IsAuthenticated);
        Assert.False(vm.IsEmployee);
        Assert.Equal("", vm.PhoneNumber);
        Assert.Equal("Customer", vm.CustomerName);
        Assert.True(vm.CurrentPage.IsHome);
        Assert.False(vm.EmployeeWorkspace.IsManager);
    });

    [Theory]
    [InlineData("reservations", "/api/v1/reservations")]
    [InlineData("contracts", "/api/v1/contracts")]
    [InlineData("access", "/api/v1/contracts")]
    [InlineData("invoices", "/api/v1/invoices")]
    [InlineData("support", "/api/v1/support-tickets")]
    public Task CustomerNavigation_CallsExpectedResourceAndCreatesRecords(string route, string expectedPath) =>
        StaTest.RunAsync(async () =>
        {
            var server = NewBackend();
            var vm = NewMain(server);
            await SignInCustomerAsync(vm);
            await vm.NavigateCommand.ExecuteAsync(route);
            Assert.Equal(route, vm.CurrentPage.Route);
            Assert.Contains(server.Requests, r => r.Path == expectedPath);
            Assert.NotEmpty(vm.CurrentPage.Records);
            Assert.False(vm.CurrentPage.IsLoading);
        });

    [Fact]
    public Task CustomerStorage_SearchAppliesFacilityModeSizeAndPeriod() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("storage");
        var page = vm.CurrentPage;
        Assert.Single(page.FacilityOptions);
        Assert.False(page.HasSearched);
        page.SelectedFacilityId = FakeBackend.FacilityId;
        page.StorageMode = "PRIVATE";
        page.StorageSize = "SMALL";
        await vm.FindStorageCommand.ExecuteAsync(null);
        Assert.True(page.HasSearched);
        Assert.Single(page.Records);
        Assert.Equal("Small Locker", page.Records[0].Title);
        Assert.Contains(server.Requests, r => r.Path.EndsWith("/unit-types") &&
            r.Query.Contains("mode=PRIVATE") && r.Query.Contains("availableOnly=true"));
    });

    [Fact]
    public Task Storage_SearchWithoutFacilityDoesNotIssueFacilityDetailRequest() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("storage");
        await vm.FindStorageCommand.ExecuteAsync(null);
        Assert.Contains("choose a Facility", vm.CurrentPage.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(server.Requests, r => r.Path == "/api/v1/facilities/" + FakeBackend.FacilityId);
    });

    [Fact]
    public Task Storage_SelectFacilityLoadsAvailableUnitTypes() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("storage");
        await vm.SelectFacilityCommand.ExecuteAsync(new CustomerRecord {
            FacilityId = FakeBackend.FacilityId, FacilityName = "Demo Facility", UnitTypeId = FakeBackend.UnitTypeId
        });
        Assert.Equal(FakeBackend.FacilityId, vm.CurrentPage.ReservationFacilityId);
        Assert.Equal(FakeBackend.UnitTypeId, vm.CurrentPage.ReservationUnitTypeId);
        Assert.Equal(2, vm.CurrentPage.UnitTypes.Count);
    });

    [Fact]
    public Task Reservation_InvalidInputsPreventPost() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("storage");
        await vm.CreateReservationCommand.ExecuteAsync(null);
        Assert.Contains("Choose an available storage type", vm.CurrentPage.Message);
        vm.CurrentPage.ReservationFacilityId = FakeBackend.FacilityId;
        vm.CurrentPage.ReservationUnitTypeId = FakeBackend.UnitTypeId;
        vm.CurrentPage.ReservationStartMonth = "2026-13";
        await vm.CreateReservationCommand.ExecuteAsync(null);
        Assert.Contains("valid rental months", vm.CurrentPage.Message);
        Assert.DoesNotContain(server.Requests, r => r.Path == "/api/v1/reservations" && r.Method == HttpMethod.Post);
    });

    [Fact]
    public Task Reservation_ValidInputsPostContractMonthsAndNavigate() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        var page = vm.CurrentPage;
        page.ReservationFacilityId = FakeBackend.FacilityId;
        page.ReservationUnitTypeId = FakeBackend.UnitTypeId;
        page.ReservationStartMonth = DateTime.Today.AddMonths(1).ToString("yyyy-MM");
        page.ReservationEndMonth = DateTime.Today.AddMonths(2).ToString("yyyy-MM");
        await vm.CreateReservationCommand.ExecuteAsync(null);
        var req = Assert.Single(server.Requests, r => r.Path == "/api/v1/reservations" && r.Method == HttpMethod.Post);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(FakeBackend.FacilityId, json.RootElement.GetProperty("facilityId").GetString());
        Assert.Equal(FakeBackend.UnitTypeId, json.RootElement.GetProperty("unitTypeId").GetString());
        Assert.Equal(page.ReservationStartMonth, json.RootElement.GetProperty("startMonth").GetString());
        Assert.True(vm.CurrentPage.IsReservations);
    });

    [Fact]
    public Task Support_RequiresContractAndDescriptionThenCreatesTicket() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("support");
        vm.CurrentPage.TicketContractId = "";
        await vm.CreateSupportTicketCommand.ExecuteAsync(null);
        Assert.Contains("Choose a contract", vm.CurrentPage.Message);
        vm.CurrentPage.TicketContractId = FakeBackend.ContractId;
        await vm.CreateSupportTicketCommand.ExecuteAsync(null);
        Assert.Contains("Describe the issue", vm.CurrentPage.Message);
        vm.CurrentPage.TicketDescription = "  Gate is jammed  ";
        await vm.CreateSupportTicketCommand.ExecuteAsync(null);
        var req = Assert.Single(server.Requests, r => r.Path == "/api/v1/support-tickets" && r.Method == HttpMethod.Post);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(FakeBackend.ContractId, json.RootElement.GetProperty("contractId").GetString());
        Assert.Equal("Gate is jammed", json.RootElement.GetProperty("description").GetString());
        Assert.Equal("OTHER", json.RootElement.GetProperty("category").GetString());
        Assert.True(vm.CurrentPage.IsSupport);
    });

    [Fact]
    public Task AccessVisit_RejectsPastDateAndPostsFutureDate() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        var item = new CustomerRecord { ContractId = FakeBackend.ContractId, Status = "ACTIVE",
            VisitDate = DateTime.Today.AddDays(-1) };
        await vm.CreateVisitCommand.ExecuteAsync(item);
        Assert.Contains("future", vm.CurrentPage.Message);
        Assert.DoesNotContain(server.Requests, r => r.Path.Contains("access-visits"));
        item.VisitDate = DateTime.Today.AddDays(1);
        await vm.CreateVisitCommand.ExecuteAsync(item);
        var req = Assert.Single(server.Requests, r => r.Path.EndsWith("/access-visits"));
        Assert.Equal(HttpMethod.Post, req.Method);
        using var json = JsonDocument.Parse(req.Body!);
        Assert.Equal(DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
            json.RootElement.GetProperty("visitDate").GetString());
        Assert.True(vm.CurrentPage.IsAccess);
    });

    [Fact]
    public Task CustomerMenu_MapsInvoicesAndReservations() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("invoices");
        Assert.Equal("UNPAID", vm.CurrentPage.Records.Single().Status);
        Assert.Contains("₫", vm.CurrentPage.Records.Single().Amount);
        await vm.NavigateCommand.ExecuteAsync("reservations");
        Assert.Equal("Small Locker", vm.CurrentPage.Records.Single().UnitType);
    });

    [Fact]
    public Task Navigation_FailedApiShowsErrorWithoutCrashing() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend(r => r.Path == "/api/v1/invoices" ?
            FakeBackend.Json("{}", HttpStatusCode.ServiceUnavailable) : null);
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("invoices");
        Assert.Contains("Could not load data", vm.CurrentPage.Message);
        Assert.False(vm.CurrentPage.IsLoading);
    });

    [Fact]
    public Task Navigation_InvalidJsonShowsErrorWithoutCrashing() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend(r => r.Path == "/api/v1/invoices" ? FakeBackend.Json("not-json") : null);
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("invoices");
        Assert.Contains("invalid JSON", vm.CurrentPage.Message);
        Assert.False(vm.CurrentPage.IsLoading);
    });

    [Fact]
    public Task HealthCheck_ReportsBackendState() => StaTest.RunAsync(async () =>
    {
        var vm = NewMain(NewBackend(), new FakeHealth { Result = new(false, 503, "Unhealthy") });
        await vm.CheckConnectionCommand.ExecuteAsync(null);
        Assert.Equal("Unhealthy", vm.Status);
        Assert.Contains("503", vm.Details);
    });

    [Fact]
    public Task Refresh_ReloadsCurrentCustomerModule() => StaTest.RunAsync(async () =>
    {
        var server = NewBackend();
        var vm = NewMain(server);
        await SignInCustomerAsync(vm);
        await vm.NavigateCommand.ExecuteAsync("reservations");
        var previousCount = server.Requests.Count(r => r.Path == "/api/v1/reservations" && r.Method == HttpMethod.Get);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(previousCount + 1, server.Requests.Count(r => r.Path == "/api/v1/reservations" && r.Method == HttpMethod.Get));
    });
}

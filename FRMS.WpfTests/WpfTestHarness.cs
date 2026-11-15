using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Threading;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.WpfTests;

internal static class StaTest
{
    // WPF PasswordBox and any command that touches WPF state must run on an STA
    // with a live Dispatcher. Awaited continuations are marshalled back to it.
    public static Task RunAsync(Func<Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); completion.TrySetResult(); }
                catch (Exception e) { completion.TrySetException(e); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

internal sealed record RequestLog(HttpMethod Method, string Path, string Query, string? Authorization, string? Body);

internal sealed class FakeBackend : HttpMessageHandler
{
    public const string FacilityId = "11111111-1111-1111-1111-111111111111";
    public const string UnitTypeId = "22222222-2222-2222-2222-222222222222";
    public const string ContractId = "33333333-3333-3333-3333-333333333333";
    public const string VisitId = "44444444-4444-4444-4444-444444444444";
    public const string TicketId = "55555555-5555-5555-5555-555555555555";
    public const string EmployeeId = "66666666-6666-6666-6666-666666666666";
    public const string StorageUnitId = "77777777-7777-7777-7777-777777777777";
    public const string InspectionId = "88888888-8888-8888-8888-888888888888";

    public List<RequestLog> Requests { get; } = new();
    public Func<RequestLog, HttpResponseMessage>? Reply { get; set; }

    public HttpClient NewHttpClient() => new(this)
    {
        BaseAddress = new Uri("http://localhost:5164/")
    };

    public static HttpResponseMessage Json(string json = "{}", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var captured = new RequestLog(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        Requests.Add(captured);
        if (Reply is not null) return Reply(captured);
        if (captured.Path == "/api/v1/auth/me")
            return Json("""{"data":{"profile":{"fullName":"Demo Employee","facilityId":""" + "\"" + FacilityId + "\"" + """},"email":"staff@demo.test","phoneNumber":"0900000000"}}""");
        if (captured.Path == "/api/v1/facilities/" + FacilityId)
            return Json("""{"data":{"name":"Demo Facility"}}""");
        return Json("""{"data":{"items":[]}}""");
    }
}

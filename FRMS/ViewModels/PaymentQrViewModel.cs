using System.Net;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FRMS.PRN212.WpfConnection.Services;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public sealed class PaymentQrViewModel(CustomerApiService api, Guid invoiceId) : ObservableObject
{
    private Guid _key = Guid.NewGuid();
    private InvoicePayment? _session;
    private string? _status;
    private bool _blocked;
    private bool _retryWithNewKey;
    private bool _isBusy;
    private string _message = "Creating payment session…";

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool Succeeded => _status == "SUCCESS";
    public bool InvoiceNeedsRefresh { get; private set; }
    public bool IsExpired => _session?.ExpiresAt <= DateTimeOffset.UtcNow;
    public bool CanPoll => CanCheck && !IsExpired;
    public bool CanCheck => !_blocked && _session is not null && _status == "PENDING";
    public bool CanStart => !_blocked && (_session is null || _status == "FAILED" || IsExpired);
    public bool HasSession => _session is not null;
    public decimal? Amount => _session?.Amount;
    public DateTimeOffset? ExpiresAt => _session?.ExpiresAt?.ToOffset(TimeSpan.FromHours(7));
    public Uri? CheckoutUri => CanPoll && Uri.TryCreate(_session?.PaymentUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "pay.payos.vn" && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    public string? QrCode => CanPoll ? _session?.QrCode : null;

    public async Task StartAsync(CancellationToken token)
    {
        if (IsBusy || !CanStart) return;
        if (_retryWithNewKey || _status == "FAILED" || IsExpired)
        {
            _key = Guid.NewGuid();
            _session = null;
            _status = null;
            _retryWithNewKey = false;
        }

        IsBusy = true;
        Message = "Creating payment session…";
        try
        {
            var session = await api.StartPayOsAsync(invoiceId, _key, token);
            token.ThrowIfCancellationRequested();
            _session = session;
            ApplyStatus(session.Status);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (HttpRequestException ex) { ReportHttpError(ex, starting: true); }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException)
        {
            Message = "The session could not be confirmed. Retry to recover the same payment attempt.";
        }
        finally { IsBusy = false; }
    }

    public async Task CheckAsync(CancellationToken token)
    {
        if (IsBusy || !CanCheck || _session is null) return;
        IsBusy = true;
        try
        {
            var payment = await api.GetPaymentAsync(_session.PaymentId, invoiceId, token);
            token.ThrowIfCancellationRequested();
            ApplyStatus(payment.Status);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (HttpRequestException ex) { ReportHttpError(ex, starting: false); }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException)
        {
            Message = "Payment status could not be checked. Try checking again.";
        }
        finally { IsBusy = false; }
    }

    private void ApplyStatus(string status)
    {
        _status = status;
        Message = status switch
        {
            "SUCCESS" => "Payment confirmed by FRMS.",
            "FAILED" => "Payment failed. You can start a new attempt.",
            _ when IsExpired => "This checkout session has expired. You can still check its status.",
            _ => "Waiting for payment confirmation."
        };
    }

    private void ReportHttpError(HttpRequestException error, bool starting)
    {
        switch (error.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
            case HttpStatusCode.NotFound:
                _blocked = true;
                Message = error.StatusCode == HttpStatusCode.Unauthorized
                    ? "Sign in again to check or pay this invoice."
                    : "This payment is unavailable for your account.";
                break;
            case HttpStatusCode.Conflict when starting && error is PaymentRequestException { Code: "PAYMENT_SESSION_EXPIRED" }:
                _retryWithNewKey = true;
                Message = "This session has expired. You can start a new attempt.";
                break;
            case HttpStatusCode.Conflict when starting:
                _blocked = true;
                InvoiceNeedsRefresh = true;
                Message = "This checkout conflicts with the current invoice. Close to refresh the invoice.";
                break;
            case HttpStatusCode.ServiceUnavailable when starting && error is PaymentRequestException { Code: "PAYMENT_SESSION_UNAVAILABLE" }:
                _retryWithNewKey = true;
                Message = "This session is unavailable. You can deliberately start a new attempt.";
                break;
            case HttpStatusCode.BadRequest when starting:
                _blocked = true;
                Message = "The server rejected this payment request.";
                break;
            default:
                Message = starting
                    ? "The session could not be confirmed. Retry to recover the same payment attempt."
                    : "Payment status could not be checked. Try checking again.";
                break;
        }
    }
}

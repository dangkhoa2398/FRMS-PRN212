using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FRMS.PRN212.WpfConnection.ViewModels;
using QRCoder;

namespace FRMS.PRN212.WpfConnection.Views;

public partial class PaymentQrWindow : Window
{
    private readonly PaymentQrViewModel _payment;
    private readonly CancellationTokenSource _closing;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string? _shownQr;

    public PaymentQrWindow(PaymentQrViewModel payment, CancellationToken lifetime)
    {
        InitializeComponent();
        _payment = payment;
        _closing = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        DataContext = payment;
        Render();
        ContentRendered += async (_, _) =>
        {
            if (!payment.HasSession) await RunAsync(payment.StartAsync);
            else if (payment.CanCheck) await RunAsync(payment.CheckAsync);
        };
        _timer.Tick += async (_, _) =>
        {
            if (_closing.IsCancellationRequested) Close();
            else if (payment.CanPoll) await RunAsync(payment.CheckAsync);
            else Render();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _closing.Cancel();
            _closing.Dispose();
        };
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_closing.IsCancellationRequested) return;
        var token = _closing.Token;
        var operation = action(token);
        Render();
        await operation;
        if (!token.IsCancellationRequested) Render();
    }

    private void Render()
    {
        var qr = _payment.QrCode;
        if (_shownQr != qr)
        {
            _shownQr = qr;
            QrImage.Source = qr is not null && Encoding.UTF8.GetByteCount(qr) <= 2048 ? RenderQr(qr) : null;
        }
        QrImage.Visibility = QrHint.Visibility = QrImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        ExpiryText.Visibility = _payment.CanPoll && _payment.ExpiresAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ExpiredHint.Visibility = _payment.CanCheck && _payment.IsExpired ? Visibility.Visible : Visibility.Collapsed;
        CheckoutButton.Visibility = _payment.CheckoutUri is not null ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Visibility = _payment.CanCheck ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = _payment.CanStart ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.IsEnabled = RetryButton.IsEnabled = !_payment.IsBusy;
        if (_payment.CanPoll && !_closing.IsCancellationRequested) _timer.Start();
        else _timer.Stop();
    }

    private static BitmapImage RenderQr(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.L);
        using var qr = new PngByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(6));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async void CheckPayment(object sender, RoutedEventArgs e) => await RunAsync(_payment.CheckAsync);
    private async void RetryPayment(object sender, RoutedEventArgs e) => await RunAsync(_payment.StartAsync);

    private void OpenCheckout(object sender, RoutedEventArgs e)
    {
        if (_payment.CheckoutUri is not { } uri) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            CheckoutError.Text = "";
        }
        catch (Win32Exception)
        {
            CheckoutError.Text = "Could not open payOS in your browser.";
        }
    }
}

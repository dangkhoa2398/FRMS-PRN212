using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FRMS.Wpf.Commands;
using FRMS.Wpf.Services;

namespace FRMS.Wpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FrmsApiClient _api = new();
    private string _apiBaseUrl = "http://localhost:5164/";
    private string _status = "Chưa kiểm tra kết nối.";
    private string _responseBody = "Nhấn 'Kiểm tra /health' để bắt đầu.";

    public MainViewModel()
    {
        CheckHealthCommand = new AsyncRelayCommand(() => FetchAsync("health"));
        LoadFacilitiesCommand = new AsyncRelayCommand(() => FetchAsync("api/v1/facilities?page=1&pageSize=10"));
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set { _apiBaseUrl = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public string ResponseBody
    {
        get => _responseBody;
        private set { _responseBody = value; OnPropertyChanged(); }
    }

    public ICommand CheckHealthCommand { get; }
    public ICommand LoadFacilitiesCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private async Task FetchAsync(string path)
    {
        Status = $"Đang gọi GET /{path}...";
        try
        {
            var (statusCode, body) = await _api.GetAsync(ApiBaseUrl, path);
            Status = statusCode is >= 200 and < 300
                ? $"Kết nối thành công — HTTP {statusCode} — GET /{path}"
                : $"Backend phản hồi HTTP {statusCode} — GET /{path}";
            ResponseBody = body;
        }
        catch (HttpRequestException ex)
        {
            Status = "Không thể kết nối đến backend. Kiểm tra API có đang chạy và đúng cổng không.";
            ResponseBody = ex.ToString();
        }
        catch (TaskCanceledException ex)
        {
            Status = "Yêu cầu hết thời gian chờ (15 giây).";
            ResponseBody = ex.Message;
        }
        catch (Exception ex)
        {
            Status = "Lỗi gọi API.";
            ResponseBody = ex.ToString();
        }
    }
}

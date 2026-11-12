using System.Net.Http;
using System.Windows;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FRMS.PRN212.WpfConnection;

public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddHttpClient<IFrmsHealthService, FrmsHealthService>(client =>
        {
            // The FRMS backend is already running on this HTTPS port.
            client.BaseAddress = new Uri("https://localhost:7235/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
        _provider = services.BuildServiceProvider();

        var window = _provider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.Dispose();
        base.OnExit(e);
    }
}

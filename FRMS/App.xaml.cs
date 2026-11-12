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
            // Local FRMS HTTP endpoint; avoids untrusted localhost development TLS certificates.
            // Use HTTPS with a trusted certificate outside local development.
            client.BaseAddress = new Uri("http://localhost:5164/");
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

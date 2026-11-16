using System;
using System.Windows;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using FRMS.PRN212.WpfConnection.Theming;

namespace FRMS.PRN212.WpfConnection;

public partial class App : Application
{
    private ServiceProvider? _provider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Initialize();
        var services = new ServiceCollection();
        var backendAddress = new Uri("https://swp391-self-storage-facility-rental-and.onrender.com/");
        services.AddHttpClient<IFrmsHealthService, FrmsHealthService>(client =>
        {
            client.BaseAddress = backendAddress;
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddHttpClient<CustomerApiService>(client =>
        {
            client.BaseAddress = backendAddress;
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();
        _provider = services.BuildServiceProvider();
        MainWindow = _provider.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _provider?.Dispose();
        base.OnExit(e);
    }
}

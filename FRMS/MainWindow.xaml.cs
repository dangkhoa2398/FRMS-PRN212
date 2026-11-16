using System.Windows;
using FRMS.PRN212.WpfConnection.ViewModels;
using FRMS.PRN212.WpfConnection.Views;

namespace FRMS.PRN212.WpfConnection;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        LoginHost.Content = new LoginView();
        ShellHost.Content = new CustomerShellView();
        EmployeeShellHost.Content = new EmployeeShellView();
        viewModel.PaymentRequested += ShowPayment;
        Closed += (_, _) => viewModel.PaymentRequested -= ShowPayment;
    }

    private void ShowPayment(PaymentQrViewModel payment, CancellationToken lifetime) =>
        new PaymentQrWindow(payment, lifetime) { Owner = this }.ShowDialog();
}

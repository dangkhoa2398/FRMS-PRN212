using System.Windows;
using FRMS.PRN212.WpfConnection.ViewModels;

namespace FRMS.PRN212.WpfConnection;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

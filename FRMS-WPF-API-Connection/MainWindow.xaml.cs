using System.Windows;
using FRMS.Wpf.ViewModels;

namespace FRMS.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}

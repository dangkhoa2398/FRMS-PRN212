using System.Windows.Controls;

namespace FRMS.PRN212.WpfConnection.Views;

public partial class EmployeeShellView : UserControl
{
    public EmployeeShellView()
    {
        InitializeComponent();
        EmployeePageHost.Content = new EmployeePageView();
    }
}

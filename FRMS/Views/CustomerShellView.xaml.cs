using System.Windows.Controls;
using System.Windows.Data;

namespace FRMS.PRN212.WpfConnection.Views;

public partial class CustomerShellView : UserControl
{
    public CustomerShellView()
    {
        InitializeComponent();
        PageHost.Content = new CustomerPageView();
        PageHost.SetBinding(DataContextProperty, new Binding("CurrentPage"));
    }
}

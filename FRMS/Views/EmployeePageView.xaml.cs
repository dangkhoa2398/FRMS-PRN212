using System;
using System.Windows.Controls;
using FRMS.PRN212.WpfConnection.ViewModels;

namespace FRMS.PRN212.WpfConnection.Views;

public partial class EmployeePageView : UserControl
{
    public EmployeePageView() => InitializeComponent();

    private async void WorkDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel main || main.EmployeeWorkspace.Route != "task")
            return;
        if (sender is not DatePicker picker || picker.SelectedDate is not DateTime chosen)
            return;
        if (main.EmployeeWorkspace.TaskDate.Date == chosen.Date)
            return;

        main.EmployeeWorkspace.TaskDate = chosen.Date;
        await main.EmployeeWorkspace.TaskDateChangedCommand.ExecuteAsync(null);
    }
}

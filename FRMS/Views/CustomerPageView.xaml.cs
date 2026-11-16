using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FRMS.PRN212.WpfConnection.Views;

public partial class CustomerPageView : UserControl
{
    public CustomerPageView() => InitializeComponent();

    private void ShowReservationReview(object sender, RoutedEventArgs e)
    {
        // Button.Click is raised before the command updates the review's visibility.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ReservationReview.IsVisible)
                ReservationReview.BringIntoView();
        }));
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FRMS.PRN212.WpfConnection.ViewModels;

public partial class CustomerPageViewModel : ObservableObject
{
    public CustomerPageViewModel(string title, string description)
    {
        Title = title;
        Description = description;
    }

    public string Title { get; }
    public string Description { get; }
    public ObservableCollection<string> Items { get; } = new();
    [ObservableProperty] private string _message = "Loading...";
    [ObservableProperty] private bool _isLoading;
}
